using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PhoneDrive
{
    /// <summary>Настройки приложения (обычный ini-файл в %APPDATA%\PhoneDrive).</summary>
    internal sealed class AppConfig
    {
        public string Folder = "";
        public string Ssid = "PhoneDrive";
        public string WifiPassword = "";
        public string Login = "phonedrive";
        public string LoginPassword = "";
        public bool WebEnabled = true;
        public int WebPort = 8080;

        /// <summary>Диапазон точки доступа: "2.4" (по умолчанию), "5" или "auto".</summary>
        public string Band = "2.4";

        /// <summary>Не создавать точку доступа, а работать через текущую домашнюю сеть.</summary>
        public bool UseHomeNetwork = false;

        private const string AlphabetLower = "abcdefghjkmnpqrstuvwxyz";
        private const string AlphabetUpper = "ABCDEFGHJKMNPQRSTUVWXYZ";
        private const string AlphabetDigit = "23456789";

        public static string ConfigDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PhoneDrive");
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDirectory, "settings.ini"); }
        }

        public static string DefaultFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "PhoneDrive");
            }
        }

        public static AppConfig Load()
        {
            AppConfig cfg = new AppConfig();
            cfg.Folder = DefaultFolder;
            cfg.WifiPassword = RandomPassword(10);
            cfg.LoginPassword = RandomPassword(10);

            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return cfg;
                }

                foreach (string rawLine in File.ReadAllLines(ConfigPath, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) { continue; }

                    int split = line.IndexOf('=');
                    if (split <= 0) { continue; }

                    string key = line.Substring(0, split).Trim().ToLowerInvariant();
                    string value = line.Substring(split + 1).Trim();

                    switch (key)
                    {
                        case "folder": cfg.Folder = value; break;
                        case "ssid": cfg.Ssid = value; break;
                        case "wifi_password": cfg.WifiPassword = value; break;
                        case "login": cfg.Login = value; break;
                        case "login_password": cfg.LoginPassword = value; break;
                        case "web_enabled": cfg.WebEnabled = value == "1"; break;
                        case "band": cfg.Band = NormalizeBand(value); break;
                        case "home_network": cfg.UseHomeNetwork = value == "1"; break;
                        case "web_port":
                            int port;
                            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) && port > 0 && port < 65536)
                            {
                                cfg.WebPort = port;
                            }
                            break;
                    }
                }
            }
            catch
            {
                // повреждённый файл настроек не должен мешать запуску
            }

            if (string.IsNullOrWhiteSpace(cfg.Folder)) { cfg.Folder = DefaultFolder; }
            if (string.IsNullOrWhiteSpace(cfg.Ssid)) { cfg.Ssid = "PhoneDrive"; }
            if (string.IsNullOrWhiteSpace(cfg.Login)) { cfg.Login = "phonedrive"; }
            if (string.IsNullOrEmpty(cfg.WifiPassword)) { cfg.WifiPassword = RandomPassword(10); }
            if (string.IsNullOrEmpty(cfg.LoginPassword)) { cfg.LoginPassword = RandomPassword(10); }
            cfg.Band = NormalizeBand(cfg.Band);

            return cfg;
        }

        /// <summary>Приводит значение диапазона к "2.4", "5" или "auto".</summary>
        private static string NormalizeBand(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "5":
                case "5ghz":
                case "5g": return "5";
                case "auto": return "auto";
                default: return "2.4";
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# Настройки Wi-Fi Флешки");
                sb.AppendLine("folder=" + Folder);
                sb.AppendLine("ssid=" + Ssid);
                sb.AppendLine("wifi_password=" + WifiPassword);
                sb.AppendLine("login=" + Login);
                sb.AppendLine("login_password=" + LoginPassword);
                sb.AppendLine("web_enabled=" + (WebEnabled ? "1" : "0"));
                sb.AppendLine("web_port=" + WebPort.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("band=" + Band);
                sb.AppendLine("home_network=" + (UseHomeNetwork ? "1" : "0"));
                File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch
            {
                // не критично
            }
        }

        /// <summary>Пароль из символов, которые легко набрать на телефоне.</summary>
        public static string RandomPassword(int length)
        {
            string all = AlphabetLower + AlphabetUpper + AlphabetDigit;
            char[] chars = new char[length];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                byte[] buffer = new byte[length];
                for (int i = 0; i < length; i++)
                {
                    rng.GetBytes(buffer);
                    chars[i] = all[buffer[i] % all.Length];
                }
            }

            // гарантируем, что политика сложности пароля Windows будет выполнена
            chars[0] = AlphabetUpper[RandomRange(AlphabetUpper.Length)];
            chars[1] = AlphabetLower[RandomRange(AlphabetLower.Length)];
            chars[2] = AlphabetDigit[RandomRange(AlphabetDigit.Length)];

            return new string(chars);
        }

        private static int RandomRange(int max)
        {
            if (max <= 1) { return 0; }
            byte[] b = new byte[1];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(b);
            }
            return b[0] % max;
        }
    }
}
