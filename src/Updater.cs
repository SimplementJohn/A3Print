using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace A3Print
{
    internal sealed class UpdateInfo
    {
        public Version Version;
        public string SetupName;
        public string SetupUrl;
        public string ShaUrl;
    }

    // Mises à jour via GitHub Releases : le setup de la dernière release est téléchargé,
    // contrôlé par SHA-256 puis lancé en silencieux.
    internal static class Updater
    {
        private const string ApiUrl = "https://api.github.com/repos/SimplementJohn/A3Print/releases/latest";
        private const string SetupPrefix = "A3Print-Setup";
        private const int DownloadAttempts = 3;
        private const int NetworkTimeoutMs = 20000;

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
                if (!name.StartsWith(SetupPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    info.SetupName = name;
                    info.SetupUrl = url;
                }
                else if (name.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase)) info.ShaUrl = url;
            }

            if (info.SetupUrl == null || info.ShaUrl == null)
            {
                throw new InvalidOperationException("La version " + info.Version + " est publiée sans installeur ou sans empreinte SHA-256.");
            }
            return info;
        }

        // Télécharge l'installeur dans un fichier .part, le contrôle par SHA-256, puis seulement le met en place :
        // une coupure en cours de route ne laisse jamais d'installeur partiel exécutable.
        // Trois essais en cas de coupure réseau. Lève OperationCanceledException si cancelled() devient vrai.
        public static string Download(UpdateInfo info, Action<int> progress, Func<bool> cancelled)
        {
            string dir = Path.Combine(Path.GetTempPath(), "A3Print-update");
            Directory.CreateDirectory(dir);
            string setup = Path.Combine(dir, Path.GetFileName(info.SetupName));
            string part = setup + ".part";

            Exception failure = null;
            for (int attempt = 1; attempt <= DownloadAttempts; attempt++)
            {
                try
                {
                    string expected;
                    using (WebClient client = NewClient())
                    {
                        expected = client.DownloadString(info.ShaUrl).Trim().Split(' ', '\t', '\r', '\n')[0];
                    }

                    Fetch(info.SetupUrl, part, progress, cancelled);

                    if (!string.Equals(expected, Sha256(part), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("empreinte SHA-256 incorrecte, fichier corrompu");
                    }

                    SafeFile.Delete(setup);
                    File.Move(part, setup);
                    return setup;
                }
                catch (OperationCanceledException)
                {
                    SafeFile.Delete(part);
                    throw;
                }
                catch (WebException ex)
                {
                    failure = ex;
                }
                catch (IOException ex)
                {
                    failure = ex;
                }

                Log.Write("Téléchargement de la mise à jour, essai " + attempt + " échoué : " + failure.Message);
                SafeFile.Delete(part);
                if (attempt < DownloadAttempts)
                {
                    Thread.Sleep(2000);
                    if (cancelled()) throw new OperationCanceledException();
                }
            }

            throw new InvalidOperationException(
                "Le téléchargement a été coupé " + DownloadAttempts + " fois (" + failure.Message + "). " +
                "Vérifiez la connexion puis réessayez.", failure);
        }

        // Lance l'installeur silencieux, qui relance l'application. L'appelant doit ensuite fermer l'application.
        public static void Launch(string setup)
        {
            Process.Start(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1");
        }

        // Message lisible pour une erreur de vérification ou de téléchargement.
        public static string Describe(Exception ex)
        {
            if (ex is WebException || ex is IOException)
            {
                return "Serveur de mise à jour injoignable (" + ex.Message + "). Vérifiez la connexion Internet.";
            }
            return ex.Message;
        }

        private static void Fetch(string url, string target, Action<int> progress, Func<bool> cancelled)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "A3Print/" + CurrentVersion;
            request.Timeout = NetworkTimeoutMs;
            request.ReadWriteTimeout = NetworkTimeoutMs;

            using (WebResponse response = request.GetResponse())
            using (Stream source = response.GetResponseStream())
            using (FileStream file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                long total = response.ContentLength;
                long done = 0;
                int lastPercent = -1;
                byte[] buffer = new byte[65536];
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (cancelled()) throw new OperationCanceledException();
                    file.Write(buffer, 0, read);
                    done += read;
                    if (total > 0)
                    {
                        int percent = (int)(done * 100 / total);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress(percent);
                        }
                    }
                }
                if (total > 0 && done != total)
                {
                    throw new IOException("téléchargement incomplet (" + done + " octets sur " + total + ")");
                }
                file.Flush(true);
            }
        }

        private static WebClient NewClient()
        {
            WebClient client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "A3Print/" + CurrentVersion;
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
