using System;
using System.Management;

namespace A3Print
{
    internal sealed class PrinterState
    {
        public bool Ready = true;
        public string Problem;
    }

    // État de l'imprimante vu par Windows (WMI). Indicatif : selon le pilote, une panne peut ne pas remonter.
    internal static class PrinterMonitor
    {
        public static PrinterState Check(string printer)
        {
            PrinterState state = new PrinterState();
            try
            {
                string name = printer.Replace("\\", "\\\\").Replace("'", "\\'");
                ObjectQuery query = new ObjectQuery(
                    "SELECT WorkOffline, PrinterStatus, DetectedErrorState FROM Win32_Printer WHERE Name = '" + name + "'");
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(5);
                    bool found = false;
                    foreach (ManagementBaseObject item in searcher.Get())
                    {
                        found = true;
                        string problem = Describe(item);
                        if (problem != null)
                        {
                            state.Ready = false;
                            state.Problem = problem;
                        }
                    }
                    if (!found)
                    {
                        state.Ready = false;
                        state.Problem = "imprimante introuvable sur ce poste";
                    }
                }
            }
            catch (Exception ex)
            {
                // État inconnu : on ne bloque pas l'impression pour autant.
                Log.Error("Lecture de l'état de l'imprimante", ex);
            }
            return state;
        }

        private static string Describe(ManagementBaseObject printer)
        {
            object offline = printer["WorkOffline"];
            if (offline is bool && (bool)offline) return "hors ligne";

            int status = ToInt(printer["PrinterStatus"]);
            if (status == 7) return "hors ligne";
            if (status == 6) return "arrêtée ou en pause";

            switch (ToInt(printer["DetectedErrorState"]))
            {
                case 4: return "plus de papier";
                case 6: return "plus de toner ou d'encre";
                case 7: return "capot ouvert";
                case 8: return "bourrage papier";
                case 9: return "hors ligne";
                case 11: return "bac de sortie plein";
                default: return null;
            }
        }

        private static int ToInt(object value)
        {
            try
            {
                return value == null ? 0 : Convert.ToInt32(value);
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
