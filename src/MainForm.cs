using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;
using System.Threading;
using System.Windows.Forms;

namespace ImprimLot
{
    internal sealed class MainForm : Form
    {
        private sealed class PrintJob
        {
            public List<DocItem> Docs;
            public string Printer;
            public int Copies;
            public DuplexMode Duplex;
        }

        private sealed class PrintResult
        {
            public DocItem Doc;
            public bool Ok;
            public string Error;
        }

        private static readonly string[] VirtualPrinterHints = { "PDF", "OneNote", "XPS", "Fax" };

        private readonly bool dryRun;
        private readonly string folder;
        private readonly Settings settings = new Settings();
        private readonly Dictionary<DocItem, ListViewItem> rows = new Dictionary<DocItem, ListViewItem>();
        private readonly BackgroundWorker worker = new BackgroundWorker();

        private Label questionLabel;
        private Label folderLabel;
        private ListView list;
        private Button checkAllButton;
        private Button uncheckAllButton;
        private Button refreshButton;
        private ComboBox printerCombo;
        private NumericUpDown copiesInput;
        private ComboBox duplexCombo;
        private Button printButton;
        private Button cancelButton;
        private ProgressBar progress;
        private Label statusLabel;
        private TextBox log;
        private LinkLabel updateLink;

        private bool filling;
        private int okCount;
        private int failCount;
        private int jobTotal;

        public MainForm(bool dryRun, string folder)
        {
            this.dryRun = dryRun;
            this.folder = string.IsNullOrEmpty(folder) ? DocumentScanner.GetDownloadsFolder() : folder;

            BuildUi();

            worker.WorkerReportsProgress = true;
            worker.WorkerSupportsCancellation = true;
            worker.DoWork += Worker_DoWork;
            worker.ProgressChanged += Worker_ProgressChanged;
            worker.RunWorkerCompleted += Worker_Completed;

            LoadPrinters();
            copiesInput.Value = settings.Copies;
            duplexCombo.SelectedIndex = (int)settings.Duplex;
            RefreshList();

            Shown += delegate { CheckForUpdate(false); };
        }

        private void BuildUi()
        {
            Text = "ImprimLot " + Updater.CurrentVersion + (dryRun ? "  [SIMULATION - rien n'est imprimé]" : "");
            Font = new Font("Segoe UI", 9F);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 640);
            MinimumSize = new Size(620, 520);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowCount = 7;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 110F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // Bandeau sombre : le logo a un texte blanc, illisible sur fond clair.
            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.AutoSize = true;
            header.BackColor = Color.FromArgb(47, 58, 69);
            header.Padding = new Padding(8);
            header.Margin = new Padding(3, 0, 3, 8);
            header.ColumnCount = 2;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.RowCount = 2;
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));

            PictureBox logoBox = new PictureBox();
            logoBox.SizeMode = PictureBoxSizeMode.Zoom;
            logoBox.Size = new Size(144, 96);
            logoBox.Margin = new Padding(0, 0, 12, 0);
            System.IO.Stream logo = typeof(MainForm).Assembly.GetManifestResourceStream("ImprimLot.logo.png");
            if (logo != null) logoBox.Image = Image.FromStream(logo);
            header.Controls.Add(logoBox, 0, 0);
            header.SetRowSpan(logoBox, 2);

            questionLabel = new Label();
            questionLabel.AutoSize = true;
            questionLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            questionLabel.Font = new Font(Font.FontFamily, 13F, FontStyle.Bold);
            questionLabel.ForeColor = Color.White;
            questionLabel.Margin = new Padding(3, 0, 3, 2);
            header.Controls.Add(questionLabel, 1, 0);

            folderLabel = new Label();
            folderLabel.AutoSize = true;
            folderLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            folderLabel.ForeColor = Color.FromArgb(200, 208, 215);
            folderLabel.Margin = new Padding(3, 2, 3, 0);
            header.Controls.Add(folderLabel, 1, 1);
            root.Controls.Add(header, 0, 0);

            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            list = new ListView();
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.CheckBoxes = true;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Columns.Add("Document", 380);
            list.Columns.Add("Taille", 80, HorizontalAlignment.Right);
            list.Columns.Add("Heure", 70);
            list.Columns.Add("État", 180);
            list.ItemChecked += delegate { if (!filling) UpdateQuestion(); };
            root.Controls.Add(list, 0, 1);

            FlowLayoutPanel listButtons = NewRow();
            checkAllButton = NewButton("Tout cocher");
            checkAllButton.Click += delegate { SetAllChecked(true); };
            uncheckAllButton = NewButton("Tout décocher");
            uncheckAllButton.Click += delegate { SetAllChecked(false); };
            refreshButton = NewButton("Actualiser");
            refreshButton.Click += delegate { RefreshList(); };
            listButtons.Controls.Add(checkAllButton);
            listButtons.Controls.Add(uncheckAllButton);
            listButtons.Controls.Add(refreshButton);
            root.Controls.Add(listButtons, 0, 2);

            FlowLayoutPanel options = NewRow();
            options.Controls.Add(NewCaption("Imprimante :"));
            printerCombo = new ComboBox();
            printerCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            printerCombo.Width = 300;
            options.Controls.Add(printerCombo);
            options.Controls.Add(NewCaption("Copies :"));
            copiesInput = new NumericUpDown();
            copiesInput.Minimum = 1;
            copiesInput.Maximum = 99;
            copiesInput.Width = 50;
            options.Controls.Add(copiesInput);
            duplexCombo = new ComboBox();
            duplexCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            duplexCombo.Width = 190;
            duplexCombo.Items.AddRange(new object[]
            {
                "Réglage de l'imprimante", "Recto seul", "Recto-verso (bord long)", "Recto-verso (bord court)"
            });
            options.Controls.Add(duplexCombo);
            root.Controls.Add(options, 0, 3);

            TableLayoutPanel run = new TableLayoutPanel();
            run.Dock = DockStyle.Fill;
            run.AutoSize = true;
            run.ColumnCount = 3;
            run.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            run.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            run.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            run.RowCount = 2;
            run.Margin = new Padding(0, 6, 0, 0);
            printButton = NewButton("Imprimer");
            printButton.Font = new Font(Font, FontStyle.Bold);
            printButton.Click += PrintButton_Click;
            cancelButton = NewButton("Annuler");
            cancelButton.Enabled = false;
            cancelButton.Click += delegate
            {
                cancelButton.Enabled = false;
                statusLabel.Text = "Annulation après le document en cours...";
                worker.CancelAsync();
            };
            progress = new ProgressBar();
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(8, 6, 3, 6);
            statusLabel = new Label();
            statusLabel.AutoSize = true;
            statusLabel.Margin = new Padding(3, 0, 3, 4);
            run.Controls.Add(printButton, 0, 0);
            run.Controls.Add(cancelButton, 1, 0);
            run.Controls.Add(progress, 2, 0);
            run.Controls.Add(statusLabel, 0, 1);
            run.SetColumnSpan(statusLabel, 3);
            root.Controls.Add(run, 0, 4);

            log = new TextBox();
            log.Dock = DockStyle.Fill;
            log.Multiline = true;
            log.ReadOnly = true;
            log.ScrollBars = ScrollBars.Vertical;
            log.BackColor = SystemColors.Window;
            root.Controls.Add(log, 0, 5);

            updateLink = new LinkLabel();
            updateLink.AutoSize = true;
            updateLink.Text = "Rechercher une mise à jour";
            updateLink.Margin = new Padding(3, 6, 3, 0);
            updateLink.LinkClicked += delegate { CheckForUpdate(true); };
            root.Controls.Add(updateLink, 0, 6);

            AcceptButton = printButton;
        }

        private static FlowLayoutPanel NewRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.Dock = DockStyle.Fill;
            row.WrapContents = true;
            row.Margin = new Padding(0, 6, 0, 0);
            return row;
        }

        private static Button NewButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(8, 2, 8, 2);
            return button;
        }

        private static Label NewCaption(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Margin = new Padding(3, 7, 0, 0);
            return label;
        }

        private void LoadPrinters()
        {
            printerCombo.Items.Clear();
            string defaultPrinter = null;
            try
            {
                foreach (string name in PrinterSettings.InstalledPrinters)
                {
                    printerCombo.Items.Add(name);
                }
                defaultPrinter = new PrinterSettings().PrinterName;
            }
            catch (Exception ex)
            {
                AppendLog("Liste des imprimantes illisible : " + ex.Message);
            }

            // Jamais d'imprimante virtuelle présélectionnée : 300 fenêtres « Enregistrer sous » sinon.
            int index = printerCombo.Items.IndexOf(settings.Printer);
            if (index < 0 && defaultPrinter != null && !IsVirtualPrinter(defaultPrinter))
            {
                index = printerCombo.Items.IndexOf(defaultPrinter);
            }
            printerCombo.SelectedIndex = index;
        }

        private static bool IsVirtualPrinter(string name)
        {
            foreach (string hint in VirtualPrinterHints)
            {
                if (name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private void RefreshList()
        {
            List<DocItem> docs;
            try
            {
                docs = DocumentScanner.Scan(folder, DateTime.Today);
            }
            catch (Exception ex)
            {
                docs = new List<DocItem>();
                AppendLog("Lecture du dossier impossible : " + ex.Message);
            }

            filling = true;
            list.BeginUpdate();
            list.Items.Clear();
            rows.Clear();
            foreach (DocItem doc in docs)
            {
                bool already = settings.IsPrinted(doc);
                ListViewItem row = new ListViewItem(doc.Name);
                row.SubItems.Add(FormatSize(doc.Size));
                row.SubItems.Add(doc.Time.ToString("HH:mm"));
                row.SubItems.Add(already ? "Déjà imprimé" : "");
                row.Checked = !already;
                row.Tag = doc;
                if (already) row.ForeColor = SystemColors.GrayText;
                list.Items.Add(row);
                rows[doc] = row;
            }
            list.EndUpdate();
            filling = false;

            folderLabel.Text = "PDF commençant par FA ou AV, du " + DateTime.Today.ToString("dd/MM/yyyy") + ", dans " + folder;
            progress.Value = 0;
            statusLabel.Text = "";
            UpdateQuestion();
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " Mo";
            return Math.Max(1, bytes / 1024) + " Ko";
        }

        private void SetAllChecked(bool value)
        {
            filling = true;
            foreach (ListViewItem row in list.Items)
            {
                row.Checked = value;
            }
            filling = false;
            UpdateQuestion();
        }

        private void UpdateQuestion()
        {
            int count = list.CheckedItems.Count;
            if (list.Items.Count == 0)
            {
                questionLabel.Text = "Aucun PDF FA / AV téléchargé aujourd'hui.";
            }
            else if (count == 0)
            {
                questionLabel.Text = "Aucun document coché (" + list.Items.Count + " trouvés).";
            }
            else
            {
                questionLabel.Text = "Imprimer " + (count == 1 ? "ce document" : "ces " + count + " documents") + " ?";
            }
            printButton.Enabled = count > 0 && !worker.IsBusy;
        }

        private void PrintButton_Click(object sender, EventArgs e)
        {
            if (worker.IsBusy) return;

            PrintJob job = new PrintJob();
            job.Docs = new List<DocItem>();
            foreach (ListViewItem row in list.CheckedItems)
            {
                job.Docs.Add((DocItem)row.Tag);
            }
            if (job.Docs.Count == 0) return;

            if (printerCombo.SelectedItem == null)
            {
                MessageBox.Show(this, "Choisissez d'abord une imprimante.", "ImprimLot",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                printerCombo.DroppedDown = true;
                return;
            }
            job.Printer = (string)printerCombo.SelectedItem;
            job.Copies = (int)copiesInput.Value;
            job.Duplex = (DuplexMode)duplexCombo.SelectedIndex;

            string message = "Envoyer " + job.Docs.Count + " document(s) vers « " + job.Printer + " » ?";
            if (job.Copies > 1) message += "\n" + job.Copies + " copies de chaque.";
            MessageBoxIcon icon = MessageBoxIcon.Question;
            if (IsVirtualPrinter(job.Printer))
            {
                message += "\n\nAttention : imprimante virtuelle, elle risque de demander un nom de fichier pour chaque document.";
                icon = MessageBoxIcon.Warning;
            }
            if (dryRun) message += "\n\n(Simulation : rien ne sera imprimé.)";
            if (MessageBox.Show(this, message, "ImprimLot", MessageBoxButtons.YesNo, icon) != DialogResult.Yes)
            {
                return;
            }

            settings.Printer = job.Printer;
            settings.Copies = job.Copies;
            settings.Duplex = job.Duplex;
            settings.Save();

            okCount = 0;
            failCount = 0;
            jobTotal = job.Docs.Count;
            progress.Maximum = jobTotal;
            progress.Value = 0;
            statusLabel.Text = "Impression en cours...";
            AppendLog("--- " + DateTime.Now.ToString("HH:mm:ss") + " : " + jobTotal + " document(s) vers " + job.Printer + " ---");
            SetBusy(true);
            worker.RunWorkerAsync(job);
        }

        private void SetBusy(bool busy)
        {
            list.Enabled = !busy;
            checkAllButton.Enabled = !busy;
            uncheckAllButton.Enabled = !busy;
            refreshButton.Enabled = !busy;
            printerCombo.Enabled = !busy;
            copiesInput.Enabled = !busy;
            duplexCombo.Enabled = !busy;
            updateLink.Enabled = !busy;
            cancelButton.Enabled = busy;
            printButton.Enabled = !busy && list.CheckedItems.Count > 0;
        }

        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            PrintJob job = (PrintJob)e.Argument;
            for (int i = 0; i < job.Docs.Count; i++)
            {
                if (worker.CancellationPending)
                {
                    e.Cancel = true;
                    return;
                }

                PrintResult result = new PrintResult();
                result.Doc = job.Docs[i];
                result.Ok = PdfPrinter.Print(result.Doc.Path, job.Printer, job.Copies, job.Duplex, dryRun, out result.Error);
                worker.ReportProgress(i + 1, result);
            }
        }

        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            PrintResult result = (PrintResult)e.UserState;
            ListViewItem row = rows[result.Doc];

            filling = true;
            if (result.Ok)
            {
                okCount++;
                row.SubItems[3].Text = dryRun ? "Simulé" : "Imprimé";
                row.ForeColor = Color.ForestGreen;
                row.Checked = false;
                if (!dryRun) settings.MarkPrinted(result.Doc);
            }
            else
            {
                failCount++;
                row.SubItems[3].Text = "Échec : " + result.Error;
                row.ForeColor = Color.Firebrick;
                AppendLog("ÉCHEC  " + result.Doc.Name + " : " + result.Error);
            }
            filling = false;

            row.EnsureVisible();
            progress.Value = Math.Min(progress.Maximum, e.ProgressPercentage);
            statusLabel.Text = e.ProgressPercentage + " / " + jobTotal + "  -  " + result.Doc.Name;
        }

        private void Worker_Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            SetBusy(false);
            UpdateQuestion();

            string summary = okCount + " imprimé(s), " + failCount + " en échec";
            if (e.Cancelled) summary += ", annulé avant la fin (" + (jobTotal - okCount - failCount) + " non traité(s))";
            if (e.Error != null) summary += ". Erreur : " + e.Error.Message;
            statusLabel.Text = "Terminé : " + summary + ".";
            AppendLog("Terminé : " + summary + ".");

            string text = summary + ".";
            if (failCount > 0)
            {
                text += "\n\nLes documents en échec restent cochés : cliquez de nouveau sur Imprimer pour réessayer.";
            }
            MessageBox.Show(this, text, "ImprimLot", MessageBoxButtons.OK,
                failCount > 0 || e.Error != null ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private void AppendLog(string line)
        {
            log.AppendText(line + Environment.NewLine);
        }

        private void CheckForUpdate(bool manual)
        {
            updateLink.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = null;
                Exception error = null;
                try
                {
                    info = Updater.Check();
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                try
                {
                    BeginInvoke((MethodInvoker)delegate { OnUpdateChecked(info, error, manual); });
                }
                catch (InvalidOperationException)
                {
                    // Fenêtre fermée entre-temps.
                }
            });
        }

        private void OnUpdateChecked(UpdateInfo info, Exception error, bool manual)
        {
            updateLink.Enabled = !worker.IsBusy;

            if (info == null)
            {
                if (!manual) return;
                if (error != null)
                {
                    MessageBox.Show(this, "Vérification impossible : " + error.Message, "ImprimLot",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(this, "ImprimLot " + Updater.CurrentVersion + " est à jour.", "ImprimLot",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }

            if (worker.IsBusy) return;
            DialogResult answer = MessageBox.Show(this,
                "La version " + info.Version + " est disponible (version actuelle : " + Updater.CurrentVersion + ").\n\nInstaller maintenant ?",
                "Mise à jour", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;

            Cursor = Cursors.WaitCursor;
            Enabled = false;
            statusLabel.Text = "Téléchargement de la mise à jour...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception failure = null;
                try
                {
                    Updater.DownloadAndRun(info);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (failure == null)
                        {
                            Application.Exit();
                            return;
                        }
                        Enabled = true;
                        Cursor = Cursors.Default;
                        statusLabel.Text = "";
                        MessageBox.Show(this, "Mise à jour impossible : " + failure.Message, "ImprimLot",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    });
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (worker.IsBusy && e.CloseReason == CloseReason.UserClosing)
            {
                DialogResult answer = MessageBox.Show(this,
                    "Une impression est en cours. Quitter quand même ?\nLes documents déjà envoyés seront imprimés.",
                    "ImprimLot", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                worker.CancelAsync();
            }
            base.OnFormClosing(e);
        }
    }
}
