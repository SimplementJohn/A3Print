using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace A3Print
{
    // Écritures qui résistent à une coupure : vidage sur disque, remplacement atomique.
    internal static class SafeFile
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        public static void AppendLine(string path, string line)
        {
            byte[] bytes = Utf8.GetBytes(line + "\r\n");
            using (FileStream stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        public static void WriteAll(string path, string content)
        {
            string temp = path + ".tmp";
            byte[] bytes = Utf8.GetBytes(content);
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        public static string[] ReadLines(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllLines(path) : new string[0];
            }
            catch (Exception)
            {
                return new string[0];
            }
        }

        public static void Delete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
            }
        }
    }

    // Préférences et historique des envois, dans %LOCALAPPDATA%\A3Print.
    internal sealed class Settings
    {
        public static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "A3Print");

        private readonly string settingsFile = Path.Combine(DataDir, "settings.ini");
        private readonly string historyFile = Path.Combine(DataDir, "imprimes.txt");
        private readonly HashSet<string> printed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public string Printer = "";
        public int Copies = 1;
        public DuplexMode Duplex = DuplexMode.PrinterDefault;

        public Settings()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                foreach (string line in SafeFile.ReadLines(settingsFile))
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
                foreach (string line in SafeFile.ReadLines(historyFile))
                {
                    if (line.Length > 0) printed.Add(line);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Lecture des préférences", ex);
            }
        }

        public void Save()
        {
            try
            {
                SafeFile.WriteAll(settingsFile,
                    "printer=" + Printer + "\r\ncopies=" + Copies + "\r\nduplex=" + (int)Duplex + "\r\n");
            }
            catch (Exception ex)
            {
                Log.Error("Enregistrement des préférences", ex);
            }
        }

        public static string Key(DocItem doc)
        {
            return doc.Path + "|" + doc.Size + "|" + doc.LastWriteTicks;
        }

        public bool IsPrinted(DocItem doc)
        {
            lock (printed)
            {
                return printed.Contains(Key(doc));
            }
        }

        // Appelé depuis le fil d'impression, juste après l'envoi.
        public void MarkPrinted(DocItem doc)
        {
            string key = Key(doc);
            lock (printed)
            {
                if (!printed.Add(key)) return;
            }
            try
            {
                SafeFile.AppendLine(historyFile, key);
            }
            catch (Exception ex)
            {
                Log.Error("Écriture de l'historique", ex);
            }
        }
    }
}
