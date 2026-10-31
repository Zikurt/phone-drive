using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PhoneDrive
{
    /// <summary>Запуск PowerShell-скриптов и разбор их ответа (строки KEY=value).</summary>
    internal static class ScriptRunner
    {
        public static string ScriptsDirectory
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scripts"); }
        }

        public static Dictionary<string, string> Run(string scriptName, Dictionary<string, string> environment, int timeoutMs)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string scriptPath = Path.Combine(ScriptsDirectory, scriptName);

            if (!File.Exists(scriptPath))
            {
                result["OK"] = "0";
                result["ERROR"] = "Не найден файл скрипта: " + scriptPath;
                return result;
            }

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = "powershell.exe";
            info.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + scriptPath + "\"";
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.StandardOutputEncoding = new UTF8Encoding(false);
            info.StandardErrorEncoding = new UTF8Encoding(false);
            info.WorkingDirectory = ScriptsDirectory;

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
                result["ERROR"] = "Не удалось запустить PowerShell: " + ex.Message;
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
