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

            // Une erreur imprévue ne ferme pas l'application sans explication : elle est journalisée et affichée.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                Log.Error("Erreur inattendue", e.Exception);
                MessageBox.Show(
                    "Une erreur inattendue est survenue :\n" + e.Exception.Message +
                    "\n\nLes documents déjà envoyés restent mémorisés. Détail dans le journal :\n" + Log.FilePath,
                    "A3Print", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null) Log.Error("Erreur fatale", ex);
            };

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
                Log.Write("Démarrage A3Print " + Updater.CurrentVersion + (dryRun ? " (simulation)" : ""));
                Application.Run(new MainForm(dryRun, folder));
            }
        }
    }
}
