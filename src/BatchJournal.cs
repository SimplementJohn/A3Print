using System;
using System.Collections.Generic;
using System.IO;

namespace A3Print
{
    // Trace sur disque du lot en cours, pour savoir où on en était après une coupure
    // (plantage, fermeture de session, panne de courant) :
    //  - lot-en-cours.txt   : présent tant qu'un lot n'est pas terminé proprement ;
    //  - envoi-en-cours.txt : le document en train de partir, son sort est inconnu si on le retrouve ;
    //  - a-verifier.txt     : documents retrouvés « en train de partir » lors d'un redémarrage.
    internal sealed class BatchJournal
    {
        private readonly bool enabled;
        private readonly string markerFile = Path.Combine(Settings.DataDir, "lot-en-cours.txt");
        private readonly string inFlightFile = Path.Combine(Settings.DataDir, "envoi-en-cours.txt");
        private readonly string uncertainFile = Path.Combine(Settings.DataDir, "a-verifier.txt");
        private readonly HashSet<string> uncertain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool Interrupted;
        public string InterruptedPrinter = "";
        public DateTime InterruptedAt = DateTime.MinValue;
        public string InterruptedDocName;

        public BatchJournal(bool enabled)
        {
            this.enabled = enabled;
            if (!enabled) return;

            try
            {
                foreach (string line in SafeFile.ReadLines(uncertainFile))
                {
                    if (line.Length > 0) uncertain.Add(line);
                }

                string[] inFlight = SafeFile.ReadLines(inFlightFile);
                if (inFlight.Length >= 2)
                {
                    InterruptedDocName = inFlight[1];
                    if (uncertain.Add(inFlight[0])) SafeFile.AppendLine(uncertainFile, inFlight[0]);
                }
                SafeFile.Delete(inFlightFile);

                string[] marker = SafeFile.ReadLines(markerFile);
                if (marker.Length >= 2)
                {
                    Interrupted = true;
                    InterruptedPrinter = marker[0];
                    long ticks;
                    if (long.TryParse(marker[1], out ticks)) InterruptedAt = new DateTime(ticks);
                    Log.Write("Lot interrompu détecté au démarrage (" + InterruptedPrinter + "), document en cours : " +
                              (InterruptedDocName ?? "aucun"));
                }
            }
            catch (Exception ex)
            {
                Log.Error("Lecture du journal de lot", ex);
            }
        }

        // Document dont l'envoi a été coupé : peut-être sorti, peut-être pas.
        public bool IsUncertain(DocItem doc)
        {
            return uncertain.Contains(Settings.Key(doc));
        }

        public void Begin(string printer)
        {
            Interrupted = false;
            if (!enabled) return;
            Try("début de lot", delegate { SafeFile.WriteAll(markerFile, printer + "\r\n" + DateTime.Now.Ticks + "\r\n"); });
        }

        public void Sending(DocItem doc)
        {
            if (!enabled) return;
            Try("envoi", delegate { SafeFile.WriteAll(inFlightFile, Settings.Key(doc) + "\r\n" + doc.Name + "\r\n"); });
        }

        public void Sent()
        {
            if (!enabled) return;
            SafeFile.Delete(inFlightFile);
        }

        public void End()
        {
            Interrupted = false;
            if (!enabled) return;
            SafeFile.Delete(markerFile);
        }

        private static void Try(string context, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error("Journal de lot (" + context + ")", ex);
            }
        }
    }
}
