using System;
using System.IO;

namespace A3Print
{
    // Journal texte dans %LOCALAPPDATA%\A3Print\a3print.log, pour comprendre après coup ce qui s'est passé.
    internal static class Log
    {
        private const long MaxSize = 1024 * 1024;
        private static readonly object Gate = new object();

        public static string FilePath
        {
            get { return Path.Combine(Settings.DataDir, "a3print.log"); }
        }

        public static void Write(string message)
        {
            lock (Gate)
            {
                try
                {
                    Directory.CreateDirectory(Settings.DataDir);
                    FileInfo file = new FileInfo(FilePath);
                    if (file.Exists && file.Length > MaxSize)
                    {
                        string old = FilePath + ".old";
                        SafeFile.Delete(old);
                        file.MoveTo(old);
                    }
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
                }
                catch (Exception)
                {
                    // Le journal ne doit jamais faire échouer l'application.
                }
            }
        }

        public static void Error(string context, Exception ex)
        {
            Write("ERREUR " + context + " : " + ex.GetType().Name + " - " + ex.Message);
        }
    }
}
