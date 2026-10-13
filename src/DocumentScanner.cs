using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace A3Print
{
    internal sealed class DocItem
    {
        public string Path;
        public string Name;
        public long Size;
        public DateTime Time;
        public long LastWriteTicks;
    }

    internal static class DocumentScanner
    {
        private static readonly string[] Prefixes = { "FA", "AV" };
        private static readonly Guid DownloadsFolderId = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
        private static extern string SHGetKnownFolderPath(
            [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string x, string y);

        public static string GetDownloadsFolder()
        {
            try
            {
                return SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero);
            }
            catch (Exception)
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }

        // PDF du dossier dont le nom commence par FA ou AV, créés ou modifiés le jour donné.
        public static List<DocItem> Scan(string folder, DateTime day)
        {
            List<DocItem> result = new List<DocItem>();
            DirectoryInfo dir = new DirectoryInfo(folder);
            if (!dir.Exists)
            {
                return result;
            }

            foreach (FileInfo file in dir.GetFiles("*.pdf"))
            {
                if (!string.Equals(file.Extension, ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!HasPrefix(file.Name))
                {
                    continue;
                }
                if (file.CreationTime.Date != day.Date && file.LastWriteTime.Date != day.Date)
                {
                    continue;
                }

                DocItem item = new DocItem();
                item.Path = file.FullName;
                item.Name = file.Name;
                item.Size = file.Length;
                item.Time = file.CreationTime > file.LastWriteTime ? file.CreationTime : file.LastWriteTime;
                item.LastWriteTicks = file.LastWriteTimeUtc.Ticks;
                result.Add(item);
            }

            result.Sort(delegate(DocItem a, DocItem b) { return StrCmpLogicalW(a.Name, b.Name); });
            return result;
        }

        private static bool HasPrefix(string name)
        {
            foreach (string prefix in Prefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
