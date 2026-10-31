using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;

namespace PhoneDrive
{
    /// <summary>
    /// Локальная точка доступа без интернета: Wi-Fi Direct в режиме обычной сети
    /// (legacy AP). Windows сама назначает адаптеру 192.168.137.1 и выдаёт
    /// телефонам адреса, профиль подключения и интернет для этого не нужны.
    ///
    /// Объявление живёт только внутри создавшего его процесса, поэтому скрипт
    /// запускается отдельным долгоживущим процессом PowerShell и держится,
    /// пока приложение не попросит остановиться.
    /// </summary>
    internal sealed class WfdHotspot : IDisposable
    {
        public const string ScriptWfd = "wfd.ps1";

        private readonly object _sync = new object();
        private Process _process;
        private string _statusPath;
        private string _stopPath;

        /// <summary>Работает ли сейчас запущенный нами процесс точки доступа.</summary>
        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    try
                    {
                        return _process != null && !_process.HasExited;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// Поднимает точку доступа и ждёт вердикта скрипта. Возвращает такой же
        /// словарь KEY=value, как скрипты: OK=1 + HOSTIP / SSID / MODE или OK=0 + ERROR.
        /// </summary>
        public Dictionary<string, string> Start(string ssid, string password, int timeoutMs)
        {
            Dictionary<string, string> result = NewResult();
            Stop(0); // на случай неудачной прошлой попытки

            string script = Path.Combine(ScriptRunner.ScriptsDirectory, ScriptWfd);
            if (!File.Exists(script))
            {
                result["OK"] = "0";
                result["ERROR"] = "Не найден файл скрипта: " + script;
                return result;
            }

            string status = Path.Combine(Path.GetTempPath(), "PhoneDrive.wfd.status");
            string stop = Path.Combine(Path.GetTempPath(), "PhoneDrive.wfd.stop");
            try
            {
                File.Delete(status);
                File.Delete(stop);
            }
            catch { }

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = "powershell.exe";
            info.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \""
                + script + "\"";
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.WindowStyle = ProcessWindowStyle.Hidden;
            info.WorkingDirectory = ScriptRunner.ScriptsDirectory;
            info.EnvironmentVariables["PD_ACTION"] = "start";
            info.EnvironmentVariables["PD_SSID"] = ssid == null ? "" : ssid;
            info.EnvironmentVariables["PD_PASS"] = password == null ? "" : password;
            info.EnvironmentVariables["PD_STATUS"] = status;
            info.EnvironmentVariables["PD_STOP"] = stop;
            try
            {
                info.EnvironmentVariables["PD_PARENT"] = Process.GetCurrentProcess().Id.ToString();
            }
            catch { }

            Process process;
            try
            {
                process = Process.Start(info);
            }
            catch (Exception ex)
            {
                result["OK"] = "0";
                result["ERROR"] = "Не удалось запустить PowerShell: " + ex.Message;
                return result;
            }

            if (process == null)
            {
                result["OK"] = "0";
                result["ERROR"] = "Не удалось запустить PowerShell.";
                return result;
            }

            lock (_sync)
            {
                _process = process;
                _statusPath = status;
                _stopPath = stop;
            }

            // Скрипт пишет файл, когда точка доступа уже поднята и адаптер
            // получил адрес, — до этого момента телефон подключать рано.
            int waited = 0;
            while (waited < timeoutMs)
            {
                Dictionary<string, string> state = ReadStatus(status);
                if (state != null)
                {
                    if (!ScriptRunner.IsOk(state))
                    {
                        Stop(3000);
                        return state;
                    }

                    // Точка доступа работает; процесс должен жить дальше.
                    try { process.EnableRaisingEvents = true; }
                    catch { }
                    return state;
                }

                bool exited;
                try { exited = process.HasExited; }
                catch { exited = true; }
                if (exited) { break; }

                Thread.Sleep(250);
                waited += 250;
            }

            bool dead;
            try { dead = process.HasExited; }
            catch { dead = true; }

            Stop(0);

            result["OK"] = "0";
            result["ERROR"] = dead
                ? "Точка доступа завершилась, не сообщив результат."
                : "Точка доступа не поднялась за " + (timeoutMs / 1000) + " с.";
            return result;
        }

        /// <summary>
        /// Останавливает точку доступа: просит скрипт завершиться через файл-флаг,
        /// ждёт gracefulMs и при необходимости завершает процесс принудительно.
        /// </summary>
        public void Stop(int gracefulMs)
        {
            Process process;
            string stopPath;
            string statusPath;

            lock (_sync)
            {
                process = _process;
                stopPath = _stopPath;
                statusPath = _statusPath;
                _process = null;
                _stopPath = null;
                _statusPath = null;
            }

            if (!string.IsNullOrEmpty(stopPath))
            {
                try { File.WriteAllText(stopPath, "1", Encoding.ASCII); }
                catch { }
            }

            if (process != null)
            {
                if (gracefulMs > 0)
                {
                    try { process.WaitForExit(gracefulMs); }
                    catch { }
                }

                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit(4000);
                    }
                }
                catch { }

                try { process.Dispose(); }
                catch { }
            }

            if (!string.IsNullOrEmpty(stopPath)) { try { File.Delete(stopPath); } catch { } }
            if (!string.IsNullOrEmpty(statusPath)) { try { File.Delete(statusPath); } catch { } }
        }

        public void Dispose()
        {
            Stop(3000);
        }

        /// <summary>
        /// Слушает ли Windows порт DHCP (67). Если нет — телефон подключится
        /// к точке доступа, но не получит адрес и отвалится через несколько секунд.
        /// </summary>
        public static bool DhcpListening()
        {
            try
            {
                foreach (IPEndPoint endPoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners())
                {
                    if (endPoint.Port == 67) { return true; }
                }
            }
            catch { }
            return false;
        }

        private static Dictionary<string, string> ReadStatus(string path)
        {
            string text;
            try
            {
                if (!File.Exists(path)) { return null; }
                text = File.ReadAllText(path, Encoding.UTF8);
            }
            catch
            {
                return null;
            }

            Dictionary<string, string> data = NewResult();
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                int split = line.IndexOf('=');
                if (split <= 0) { continue; }

                string key = line.Substring(0, split).Trim();
                if (key.Length == 0 || key.IndexOf(' ') >= 0) { continue; }
                data[key] = line.Substring(split + 1).Trim();
            }

            // «STARTING» — ещё не результат.
            if (!data.ContainsKey("OK")) { return null; }
            return data;
        }

        private static Dictionary<string, string> NewResult()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
