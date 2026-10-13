using System;
using System.Collections.Generic;
using System.IO;

namespace ImprimLot
{
    // Préférences et historique des impressions, dans %LOCALAPPDATA%\ImprimLot.
    internal sealed class Settings
    {
        private readonly string settingsFile;
        private readonly string historyFile;
        private readonly HashSet<string> printed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string Printer = "";
        public int Copies = 1;
        public DuplexMode Duplex = DuplexMode.PrinterDefault;

        public Settings()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImprimLot");
            settingsFile = Path.Combine(dir, "settings.ini");
            historyFile = Path.Combine(dir, "imprimes.txt");

            try
            {
                Directory.CreateDirectory(dir);
                if (File.Exists(settingsFile))
                {
                    foreach (string line in File.ReadAllLines(settingsFile))
                    {
                        int eq = line.IndexOf('=');
                        if (eq <= 0) continue;
                        string key = line.Substring(0, eq);
                        string value = line.Substring(eq + 1);
                        int number;
                        if (key == "printer") Printer = value;
                        else if (key == "copies" && int.TryParse(value, out number)) Copies = Math.Max(1, Math.Min(99, number));
                        else if (key == "duplex" && int.TryParse(value, out number) && number >= 0 && number <= 3) Duplex = (DuplexMode)number;
                    }
                }
                if (File.Exists(historyFile))
                {
                    foreach (string line in File.ReadAllLines(historyFile))
                    {
                        if (line.Length > 0) printed.Add(line);
                    }
                }
            }
            catch (Exception)
            {
                // Préférences illisibles : valeurs par défaut.
            }
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(settingsFile, new string[]
                {
                    "printer=" + Printer,
                    "copies=" + Copies,
                    "duplex=" + (int)Duplex
                });
            }
            catch (Exception)
            {
            }
        }

        public bool IsPrinted(DocItem doc)
        {
            return printed.Contains(Key(doc));
        }

        public void MarkPrinted(DocItem doc)
        {
            string key = Key(doc);
            if (!printed.Add(key)) return;
            try
            {
                File.AppendAllLines(historyFile, new string[] { key });
            }
            catch (Exception)
            {
            }
        }

        private static string Key(DocItem doc)
        {
            return doc.Path + "|" + doc.Size + "|" + doc.LastWriteTicks;
        }
    }
}
