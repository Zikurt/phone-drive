using System.Collections.Generic;

namespace PhoneDrive
{
    /// <summary>Тонкая обёртка над скриптами: точка доступа + SMB-папка + брандмауэр.</summary>
    internal sealed class ShareService
    {
        public const string ScriptTether = "tether.ps1";
        public const string ScriptShare = "share.ps1";

        public Dictionary<string, string> TetherOn(string ssid, string password, string band)
        {
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["PD_ACTION"] = "on";
            env["PD_SSID"] = ssid;
            env["PD_PASS"] = password;
            env["PD_BAND"] = band;
            return ScriptRunner.Run(ScriptTether, env, 90000);
        }

        public Dictionary<string, string> TetherOff()
        {
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["PD_ACTION"] = "off";
            return ScriptRunner.Run(ScriptTether, env, 40000);
        }

        public Dictionary<string, string> TetherStatus()
        {
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["PD_ACTION"] = "status";
            return ScriptRunner.Run(ScriptTether, env, 30000);
        }

        public Dictionary<string, string> ShareOn(string folder, string shareName, string login, string password, string subnet, int webPort)
        {
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["PD_ACTION"] = "on";
            env["PD_FOLDER"] = folder;
            env["PD_SHARE"] = shareName;
            env["PD_USER"] = login;
            env["PD_PASSWORD"] = password;
            env["PD_SUBNET"] = subnet;
            if (webPort > 0) { env["PD_WEBPORT"] = webPort.ToString(); }
            return ScriptRunner.Run(ScriptShare, env, 60000);
        }

        public Dictionary<string, string> ShareOff(string folder, string shareName, string login)
        {
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["PD_ACTION"] = "off";
            env["PD_FOLDER"] = folder;
            env["PD_SHARE"] = shareName;
            env["PD_USER"] = login;
            return ScriptRunner.Run(ScriptShare, env, 40000);
        }

        public Dictionary<string, string> Cleanup(string folder, string shareName, string login)
        {
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["PD_ACTION"] = "cleanup";
            env["PD_FOLDER"] = folder;
            env["PD_SHARE"] = shareName;
            env["PD_USER"] = login;
            return ScriptRunner.Run(ScriptShare, env, 60000);
        }

        /// <summary>192.168.137.1 -> 192.168.137.0/24</summary>
        public static string SubnetOf(string ip)
        {
            if (string.IsNullOrEmpty(ip)) { return ""; }
            string[] parts = ip.Split('.');
            if (parts.Length != 4) { return ""; }
            return parts[0] + "." + parts[1] + "." + parts[2] + ".0/24";
        }
    }
}
