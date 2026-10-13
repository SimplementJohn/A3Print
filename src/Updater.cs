using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace ImprimLot
{
    internal sealed class UpdateInfo
    {
        public Version Version;
        public string SetupUrl;
        public string ShaUrl;
    }

    // Mises à jour via GitHub Releases : le setup de la dernière release est téléchargé,
    // contrôlé par SHA-256 puis lancé en silencieux.
    internal static class Updater
    {
        private const string ApiUrl = "https://api.github.com/repos/SimplementJohn/imprimlot/releases/latest";
        private const string SetupName = "ImprimLot-Setup.exe";

        public static Version CurrentVersion
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, v.Build);
            }
        }

        // Renvoie la release disponible si elle est plus récente, sinon null. Lève une exception si injoignable.
        public static UpdateInfo Check()
        {
            string json;
            using (WebClient client = NewClient())
            {
                json = client.DownloadString(ApiUrl);
            }

            Dictionary<string, object> release = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            string tag = Convert.ToString(release["tag_name"]).TrimStart('v', 'V');
            Version parsed;
            if (!Version.TryParse(tag, out parsed))
            {
                return null;
            }

            UpdateInfo info = new UpdateInfo();
            info.Version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            if (info.Version <= CurrentVersion)
            {
                return null;
            }

            foreach (object entry in (ArrayList)release["assets"])
            {
                Dictionary<string, object> asset = (Dictionary<string, object>)entry;
                string name = Convert.ToString(asset["name"]);
                string url = Convert.ToString(asset["browser_download_url"]);
                if (string.Equals(name, SetupName, StringComparison.OrdinalIgnoreCase)) info.SetupUrl = url;
                else if (string.Equals(name, SetupName + ".sha256", StringComparison.OrdinalIgnoreCase)) info.ShaUrl = url;
            }

            if (info.SetupUrl == null || info.ShaUrl == null)
            {
                throw new InvalidOperationException("La version " + info.Version + " est publiée sans installeur ou sans empreinte SHA-256.");
            }
            return info;
        }

        // Télécharge, vérifie et lance l'installeur. L'appelant doit ensuite fermer l'application.
        public static void DownloadAndRun(UpdateInfo info)
        {
            string dir = Path.Combine(Path.GetTempPath(), "ImprimLot-update");
            Directory.CreateDirectory(dir);
            string setup = Path.Combine(dir, SetupName);

            string expected;
            using (WebClient client = NewClient())
            {
                expected = client.DownloadString(info.ShaUrl).Trim().Split(' ', '\t', '\r', '\n')[0];
                client.DownloadFile(info.SetupUrl, setup);
            }

            string actual = Sha256(setup);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(setup);
                throw new InvalidOperationException("Empreinte SHA-256 incorrecte : téléchargement corrompu, mise à jour annulée.");
            }

            Process.Start(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1");
        }

        private static WebClient NewClient()
        {
            WebClient client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "ImprimLot/" + CurrentVersion;
            client.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            client.Encoding = Encoding.UTF8;
            return client;
        }

        private static string Sha256(string file)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(file))
            {
                StringBuilder hex = new StringBuilder();
                foreach (byte b in sha.ComputeHash(stream))
                {
                    hex.Append(b.ToString("x2"));
                }
                return hex.ToString();
            }
        }
    }
}
