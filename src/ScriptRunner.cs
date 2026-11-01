using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace PhoneDrive
{
    /// <summary>
    /// Запуск скриптов и разбор их ответа (строки KEY=value).
    /// Все скрипты зашиты в exe как ресурсы (префикс ldr.), поэтому
    /// отдельно папку со скриптами переносить не нужно. Если рядом с exe
    /// лежит папка scripts — она тоже поддержана (для разработки).
    /// Если Windows PowerShell не установлен, скрипты выполняются из
    /// .bat-вариантов (net.exe/icacls/netsh). Wi-Fi Direct при этом недоступен.
    /// </summary>
    internal static class ScriptRunner
    {
        private const string ResourcePrefix = "ldr.";
        private static bool _extracted;

        /// <summary>Приоритетная папка со скриптами рядом с exe; если её нет — берём ресурсы.</summary>
        public static string ScriptsDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts"); }
        }

        /// <summary>Куда извлекаются скрипты из ресурсов (заглушка из temp-каталога).</summary>
        public static string EmbeddedScriptsDirectory
        {
            get { return Path.Combine(Path.GetTempPath(), "PhoneDrive_scripts"); }
        }

        /// <summary>
        /// Есть ли Windows PowerShell 5.1+ (нужен для WinRT-скриптов:
        /// Mobile Hotspot и Wi-Fi Direct).
        /// </summary>
        public static bool HasPowerShell
        {
            get
            {
                return File.Exists(
                    Path.Combine(Environment.SystemDirectory,
                        "WindowsPowerShell", "v1.0", "powershell.exe"));
            }
        }

        /// <summary>Каталог, из которого реально запускаются скрипты (также их извлекает).</summary>
        public static string ResolveScriptsDirectory()
        {
            if (Directory.Exists(ScriptsDirectory))
            {
                return ScriptsDirectory;
            }

            string embedded = EmbeddedScriptsDirectory;
            if (!_extracted || !Directory.Exists(embedded))
            {
                try
                {
                    Directory.CreateDirectory(embedded);
                    ExtractEmbedded(embedded);
                    _extracted = true;
                }
                catch
                {
                    // если temp недоступен, Run сам вернёт понятную ошибку по пути
                }
            }
            return embedded;
        }

        /// <summary>Полный путь к нужному варианту файла (.ps1 / .bat) после извлечения.</summary>
        public static string ResolveScript(string scriptName)
        {
            string root = ResolveScriptsDirectory();
            string want = scriptName;
            if (!HasPowerShell && want.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                want = Path.GetFileNameWithoutExtension(scriptName) + ".bat";
            }
            return Path.Combine(root, want);
        }

        public static Dictionary<string, string> Run(string scriptName, Dictionary<string, string> environment, int timeoutMs)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string scriptPath = ResolveScript(scriptName);

            if (!File.Exists(scriptPath))
            {
                result["OK"] = "0";
                result["ERROR"] = String.Format(
                    "Не найден скрипт {0}. " + (HasPowerShell
                        ? ""
                        : "PowerShell не установлен — нужна cmd-версия (.bat)."),
                    scriptPath);
                return result;
            }

            bool isCmd = scriptPath.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
                || scriptPath.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);

            ProcessStartInfo info = new ProcessStartInfo();
            if (isCmd)
            {
                info.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                info.Arguments = "/d /c \"" + scriptPath + "\"";
            }
            else
            {
                info.FileName = "powershell.exe";
                info.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + scriptPath + "\"";
            }

            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = new UTF8Encoding(false);
            info.StandardErrorEncoding = new UTF8Encoding(false);
            info.WorkingDirectory = Path.GetDirectoryName(scriptPath);

            foreach (KeyValuePair<string, string> pair in environment)
            {
                info.EnvironmentVariables[pair.Key] = pair.Value == null ? "" : pair.Value;
            }

            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();

            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = info;
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { lock (stdout) { stdout.AppendLine(e.Data); } }
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) { lock (stderr) { stderr.AppendLine(e.Data); } }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { }
                        result["OK"] = "0";
                        result["ERROR"] = "Операция не завершилась за " + (timeoutMs / 1000) + " с. и была прервана.";
                        return result;
                    }

                    process.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                result["OK"] = "0";
                result["ERROR"] = (isCmd ? "Не удалось запустить cmd.exe: " : "Не удалось запустить PowerShell: ") + ex.Message;
                return result;
            }

            StringBuilder log = new StringBuilder();
            string text;
            lock (stdout) { text = stdout.ToString(); }

            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.Length == 0) { continue; }

                int split = line.IndexOf('=');
                if (split > 0)
                {
                    string key = line.Substring(0, split).Trim();
                    string value = line.Substring(split + 1);
                    if (key.Length > 0 && key.IndexOf(' ') < 0)
                    {
                        result[key] = value;
                        continue;
                    }
                }

                log.AppendLine(line);
            }

            string errors;
            lock (stderr) { errors = stderr.ToString().Trim(); }

            if (!result.ContainsKey("OK"))
            {
                result["OK"] = "0";
                result["ERROR"] = errors.Length > 0
                    ? errors.Replace("\r", " ").Replace("\n", " ")
                    : "Скрипт завершился без результата.";
            }

            if (errors.Length > 0)
            {
                result["STDERR"] = errors.Replace("\r", " ").Replace("\n", " ");
            }

            result["LOG"] = log.ToString();
            return result;
        }

        /// <summary>Скопировать скрипты, зашитые в exe, на диск — powershell/cmd ждут файл.</summary>
        private static void ExtractEmbedded(string target)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            foreach (string resource in asm.GetManifestResourceNames())
            {
                if (resource.Length <= ResourcePrefix.Length ||
                    !resource.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string fileName = resource.Substring(ResourcePrefix.Length);
                using (Stream source = asm.GetManifestResourceStream(resource))
                using (Stream destination = File.Open(
                    Path.Combine(target, fileName), FileMode.Create, FileAccess.Write, FileShare.Read))
                {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = source.Read(buffer, 0, buffer.Length)) > 0) { destination.Write(buffer, 0, read); }
                }
            }
        }

        /// <summary>Удаляет извлечённые из ресурсов скрипты (вызывается при закрытии приложения).</summary>
        public static void CleanupEmbeddedScripts()
        {
            _extracted = false;
            try
            {
                if (Directory.Exists(EmbeddedScriptsDirectory))
                {
                    Directory.Delete(EmbeddedScriptsDirectory, true);
                }
            }
            catch
            {
                // не критично: папка остаётся до следующей перезагрузки
            }
        }

        public static bool IsOk(Dictionary<string, string> result)
        {
            if (result == null) { return false; }
            string ok;
            return result.TryGetValue("OK", out ok) && ok == "1";
        }

        public static string Message(Dictionary<string, string> result)
        {
            if (result == null) { return "неизвестная ошибка"; }
            string error;
            if (result.TryGetValue("ERROR", out error) && !string.IsNullOrEmpty(error)) { return error; }
            return "неизвестная ошибка";
        }
    }
}
