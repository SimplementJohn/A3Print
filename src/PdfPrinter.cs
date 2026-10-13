using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace A3Print
{
    internal enum DuplexMode
    {
        PrinterDefault = 0,
        Simplex = 1,
        DuplexLong = 2,
        DuplexShort = 3
    }

    internal static class PdfPrinter
    {
        private const int TimeoutMs = 180000;

        public static string EnginePath
        {
            get
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                return Path.Combine(dir, "SumatraPDF.exe");
            }
        }

        // Imprime un fichier et attend la fin de l'envoi au spouleur.
        // retryable : l'envoi a été refusé net, un second essai ne risque pas de doublon.
        public static bool Print(string file, string printer, int copies, DuplexMode duplex, bool dryRun,
            out string error, out bool retryable)
        {
            retryable = false;
            error = Validate(file);
            if (error != null)
            {
                return false;
            }
            if (dryRun)
            {
                Thread.Sleep(15);
                return true;
            }
            if (!File.Exists(EnginePath))
            {
                error = "moteur d'impression absent (SumatraPDF.exe) : réinstallez A3Print";
                return false;
            }

            string settings = copies + "x";
            if (duplex == DuplexMode.Simplex) settings += ",simplex";
            else if (duplex == DuplexMode.DuplexLong) settings += ",duplexlong";
            else if (duplex == DuplexMode.DuplexShort) settings += ",duplexshort";

            ProcessStartInfo info = new ProcessStartInfo();
            info.FileName = EnginePath;
            info.Arguments = "-print-to \"" + printer + "\" -print-settings \"" + settings + "\" -silent \"" + file + "\"";
            info.UseShellExecute = false;
            info.CreateNoWindow = true;

            try
            {
                using (Process process = Process.Start(info))
                {
                    if (!process.WaitForExit(TimeoutMs))
                    {
                        try { process.Kill(); } catch (Exception) { }
                        error = "pas de réponse de l'imprimante après 3 minutes (vérifiez si la page est sortie)";
                        return false;
                    }
                    if (process.ExitCode != 0)
                    {
                        error = "envoi refusé par l'imprimante ou son pilote (code " + process.ExitCode + ")";
                        retryable = true;
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "moteur d'impression inaccessible : " + ex.Message;
                return false;
            }
        }

        // Écarte les fichiers qui ne peuvent pas s'imprimer avant de solliciter l'imprimante.
        private static string Validate(string file)
        {
            try
            {
                FileInfo info = new FileInfo(file);
                if (!info.Exists) return "fichier introuvable (déplacé ou supprimé)";
                if (info.Length == 0) return "fichier vide (téléchargement incomplet ?)";

                using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] head = new byte[1024];
                    int read = stream.Read(head, 0, head.Length);
                    if (Encoding.ASCII.GetString(head, 0, read).IndexOf("%PDF", StringComparison.Ordinal) < 0)
                    {
                        return "ce n'est pas un PDF valide (téléchargement incomplet ?)";
                    }
                }
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return "accès au fichier refusé";
            }
            catch (IOException)
            {
                return "fichier verrouillé (encore en téléchargement ou ouvert ailleurs)";
            }
        }
    }
}
