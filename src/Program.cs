using System;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace A3Print
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool dryRun = false;
            string folder = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--dry-run", StringComparison.OrdinalIgnoreCase))
                {
                    dryRun = true;
                }
                else if (string.Equals(args[i], "--folder", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    folder = args[++i];
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "A3Print_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("A3Print est déjà ouvert.", "A3Print",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                Application.Run(new MainForm(dryRun, folder));
            }
        }
    }
}
