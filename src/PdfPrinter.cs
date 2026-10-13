using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
        public static bool Print(string file, string printer, int copies, DuplexMode duplex, bool dryRun, out string error)
        {
            error = null;

            if (!File.Exists(file))
            {
                error = "fichier introuvable";
                return false;
            }
            if (dryRun)
            {
                Thread.Sleep(15);
                return true;
            }
            if (!File.Exists(EnginePath))
            {
                error = "SumatraPDF.exe manquant dans le dossier d'installation";
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
                        error = "délai dépassé";
                        return false;
                    }
                    if (process.ExitCode != 0)
                    {
                        error = "échec impression (code " + process.ExitCode + ")";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
