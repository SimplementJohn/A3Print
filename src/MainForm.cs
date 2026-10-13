using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

namespace A3Print
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

        private const string AppName = "A3Print";
        private const int StateColumn = 3;
        private static readonly string[] VirtualPrinterHints = { "PDF", "OneNote", "XPS", "Fax" };

        private readonly bool dryRun;
        private readonly string folder;
        private readonly Settings settings = new Settings();
        private readonly Dictionary<DocItem, ListViewItem> rows = new Dictionary<DocItem, ListViewItem>();
        private readonly BackgroundWorker worker = new BackgroundWorker();

        private float scale = 1F;
        private Font headerFont;
        private Label questionLabel;
        private Label folderLabel;
        private BufferedListView list;
        private Label emptyLabel;
        private Button checkAllButton;
        private Button uncheckAllButton;
        private Button refreshButton;
        private ComboBox printerCombo;
        private NumericUpDown copiesInput;
        private ComboBox duplexCombo;
        private Button printButton;
        private Button cancelButton;
        private ThinProgress progress;
        private Label statusLabel;
        private LinkLabel updateLink;

        private bool filling;
        private bool fitting;
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

        // Taille en pixels pour le DPI de l'écran.
        private int S(int value)
        {
            return (int)Math.Round(value * scale);
        }

        private void BuildUi()
        {
            using (Graphics g = CreateGraphics())
            {
                scale = g.DpiX / 96F;
            }

            Text = AppName + (dryRun ? "  [SIMULATION - rien n'est imprimé]" : "");
            Font = new Font("Segoe UI", 9.75F);
            headerFont = new Font("Segoe UI", 8F, FontStyle.Bold);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(S(880), S(680));
            MinimumSize = new Size(S(720), S(560));
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(88)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildBody(), 0, 1);
            root.Controls.Add(BuildFooter(), 0, 2);

            AcceptButton = printButton;
        }

        // Bandeau ardoise : le logo a un texte blanc, illisible sur fond clair.
        private Control BuildHeader()
        {
            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.Margin = Padding.Empty;
            header.BackColor = Theme.Slate;
            header.ColumnCount = 2;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.RowCount = 2;
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 56F));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 44F));

            PictureBox logoBox = new PictureBox();
            logoBox.SizeMode = PictureBoxSizeMode.Zoom;
            logoBox.Size = new Size(S(108), S(72));
            logoBox.Margin = new Padding(S(20), S(8), S(14), S(8));
            System.IO.Stream logo = typeof(MainForm).Assembly.GetManifestResourceStream("A3Print.logo.png");
            if (logo != null) logoBox.Image = Image.FromStream(logo);
            header.Controls.Add(logoBox, 0, 0);
            header.SetRowSpan(logoBox, 2);

            Label title = new Label();
            title.Text = AppName;
            title.AutoSize = true;
            title.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
            title.Font = new Font("Segoe UI", 17F, FontStyle.Bold);
            title.ForeColor = Color.White;
            title.Margin = new Padding(0);
            header.Controls.Add(title, 1, 0);

            Label subtitle = new Label();
            subtitle.Text = "Impression en lot des PDF FA / AV du jour";
            subtitle.AutoSize = true;
            subtitle.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            subtitle.ForeColor = Theme.HeaderMuted;
            subtitle.Margin = new Padding(S(3), S(1), 0, 0);
            header.Controls.Add(subtitle, 1, 1);

            return header;
        }

        private Control BuildBody()
        {
            TableLayoutPanel body = new TableLayoutPanel();
            body.Dock = DockStyle.Fill;
            body.Margin = Padding.Empty;
            body.Padding = new Padding(S(20), S(16), S(20), S(8));
            body.ColumnCount = 1;
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.RowCount = 4;
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Question + actions sur la liste
            TableLayoutPanel top = new TableLayoutPanel();
            top.Dock = DockStyle.Fill;
            top.AutoSize = true;
            top.Margin = new Padding(0, 0, 0, S(10));
            top.ColumnCount = 2;
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.RowCount = 2;
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            top.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            questionLabel = new Label();
            questionLabel.AutoSize = true;
            questionLabel.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
            questionLabel.Margin = new Padding(0);
            top.Controls.Add(questionLabel, 0, 0);

            folderLabel = new Label();
            folderLabel.AutoSize = false;
            folderLabel.AutoEllipsis = true;
            folderLabel.Dock = DockStyle.Fill;
            folderLabel.Height = S(20);
            folderLabel.ForeColor = Theme.Muted;
            folderLabel.Margin = new Padding(S(2), S(2), 0, 0);
            top.Controls.Add(folderLabel, 0, 1);

            FlowLayoutPanel tools = new FlowLayoutPanel();
            tools.AutoSize = true;
            tools.WrapContents = false;
            tools.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            tools.Margin = Padding.Empty;
            checkAllButton = NewSecondaryButton("Tout cocher");
            checkAllButton.Click += delegate { SetAllChecked(true); };
            uncheckAllButton = NewSecondaryButton("Tout décocher");
            uncheckAllButton.Click += delegate { SetAllChecked(false); };
            refreshButton = NewSecondaryButton("Actualiser");
            refreshButton.Click += delegate { RefreshList(); };
            tools.Controls.Add(checkAllButton);
            tools.Controls.Add(uncheckAllButton);
            tools.Controls.Add(refreshButton);
            top.Controls.Add(tools, 1, 0);
            top.SetRowSpan(tools, 2);
            body.Controls.Add(top, 0, 0);

            body.Controls.Add(BuildListCard(), 0, 1);

            // Options d'impression
            FlowLayoutPanel options = new FlowLayoutPanel();
            options.Dock = DockStyle.Fill;
            options.AutoSize = true;
            options.WrapContents = true;
            options.BackColor = Theme.Card;
            options.Padding = new Padding(S(10), S(8), S(10), S(8));
            options.Margin = new Padding(0, S(12), 0, 0);
            Theme.AddBorder(options);

            options.Controls.Add(NewCaption("Imprimante"));
            printerCombo = new ComboBox();
            printerCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            printerCombo.FlatStyle = FlatStyle.Flat;
            printerCombo.Width = S(310);
            printerCombo.Margin = new Padding(S(3), S(3), S(18), S(3));
            options.Controls.Add(printerCombo);

            options.Controls.Add(NewCaption("Copies"));
            copiesInput = new NumericUpDown();
            copiesInput.Minimum = 1;
            copiesInput.Maximum = 99;
            copiesInput.Width = S(54);
            copiesInput.Margin = new Padding(S(3), S(3), S(18), S(3));
            options.Controls.Add(copiesInput);

            options.Controls.Add(NewCaption("Mode"));
            duplexCombo = new ComboBox();
            duplexCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            duplexCombo.FlatStyle = FlatStyle.Flat;
            duplexCombo.Width = S(200);
            duplexCombo.Items.AddRange(new object[]
            {
                "Réglage de l'imprimante", "Recto seul", "Recto-verso (bord long)", "Recto-verso (bord court)"
            });
            options.Controls.Add(duplexCombo);
            body.Controls.Add(options, 0, 2);

            // Progression + boutons
            TableLayoutPanel run = new TableLayoutPanel();
            run.Dock = DockStyle.Fill;
            run.AutoSize = true;
            run.Margin = new Padding(0, S(14), 0, 0);
            run.ColumnCount = 3;
            run.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            run.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            run.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            run.RowCount = 2;
            run.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            run.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            progress = new ThinProgress();
            progress.Height = S(6);
            progress.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            progress.Margin = new Padding(0, 0, S(16), S(4));
            run.Controls.Add(progress, 0, 0);

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.ForeColor = Theme.Muted;
            statusLabel.Margin = new Padding(0, 0, S(16), 0);
            run.Controls.Add(statusLabel, 0, 1);

            cancelButton = NewSecondaryButton("Annuler");
            cancelButton.AutoSize = false;
            cancelButton.Size = new Size(S(104), S(42));
            cancelButton.Margin = new Padding(0, 0, S(8), 0);
            cancelButton.Enabled = false;
            cancelButton.Click += delegate
            {
                cancelButton.Enabled = false;
                statusLabel.Text = "Annulation après le document en cours...";
                worker.CancelAsync();
            };
            run.Controls.Add(cancelButton, 1, 0);
            run.SetRowSpan(cancelButton, 2);

            printButton = new Button();
            printButton.Text = "Imprimer";
            printButton.FlatStyle = FlatStyle.Flat;
            printButton.FlatAppearance.BorderSize = 0;
            printButton.FlatAppearance.MouseOverBackColor = Theme.GreenDark;
            printButton.FlatAppearance.MouseDownBackColor = Theme.GreenDark;
            printButton.BackColor = Theme.Green;
            printButton.ForeColor = Color.White;
            printButton.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            printButton.Size = new Size(S(210), S(42));
            printButton.Margin = Padding.Empty;
            printButton.Cursor = Cursors.Hand;
            printButton.EnabledChanged += delegate
            {
                printButton.BackColor = printButton.Enabled ? Theme.Green : Theme.Disabled;
            };
            printButton.Click += PrintButton_Click;
            run.Controls.Add(printButton, 2, 0);
            run.SetRowSpan(printButton, 2);
            body.Controls.Add(run, 0, 3);

            return body;
        }

        private Control BuildListCard()
        {
            Panel card = new Panel();
            card.Dock = DockStyle.Fill;
            card.Margin = Padding.Empty;
            card.Padding = new Padding(1);
            card.BackColor = Theme.Border;

            // Hauteur de ligne : imposée par une liste d'images vide.
            ImageList rowHeight = new ImageList();
            rowHeight.ImageSize = new Size(1, S(30));

            list = new BufferedListView();
            list.Dock = DockStyle.Fill;
            list.BorderStyle = BorderStyle.None;
            list.View = View.Details;
            list.CheckBoxes = true;
            list.FullRowSelect = true;
            list.HideSelection = true;
            list.MultiSelect = false;
            list.OwnerDraw = true;
            list.SmallImageList = rowHeight;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.BackColor = Theme.Card;
            list.Columns.Add("Document", S(380));
            list.Columns.Add("Taille", S(90), HorizontalAlignment.Right);
            list.Columns.Add("Heure", S(80), HorizontalAlignment.Right);
            list.Columns.Add("État", S(250));
            list.DrawColumnHeader += List_DrawColumnHeader;
            list.DrawSubItem += List_DrawSubItem;
            list.ItemCheck += List_ItemCheck;
            list.ItemChecked += delegate { if (!filling) UpdateQuestion(); };
            list.MouseClick += List_MouseClick;
            list.ClientSizeChanged += delegate { FitColumns(); };
            list.ColumnWidthChanging += delegate(object sender, ColumnWidthChangingEventArgs e)
            {
                if (fitting) return;
                e.NewWidth = list.Columns[e.ColumnIndex].Width;
                e.Cancel = true;
            };

            emptyLabel = new Label();
            emptyLabel.Dock = DockStyle.Fill;
            emptyLabel.BackColor = Theme.Card;
            emptyLabel.ForeColor = Theme.Muted;
            emptyLabel.Font = new Font("Segoe UI", 11F);
            emptyLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            emptyLabel.Text = "Aucun PDF FA / AV téléchargé aujourd'hui.\n\n" +
                              "Téléchargez vos documents, puis cliquez sur Actualiser.";

            card.Controls.Add(list);
            card.Controls.Add(emptyLabel);
            return card;
        }

        private Control BuildFooter()
        {
            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.AutoSize = true;
            footer.Margin = Padding.Empty;
            footer.Padding = new Padding(S(20), S(4), S(20), S(10));
            footer.ColumnCount = 2;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label version = new Label();
            version.Text = AppName + " " + Updater.CurrentVersion;
            version.AutoSize = true;
            version.ForeColor = Theme.Muted;
            version.Margin = Padding.Empty;
            footer.Controls.Add(version, 0, 0);

            updateLink = new LinkLabel();
            updateLink.Text = "Rechercher une mise à jour";
            updateLink.AutoSize = true;
            updateLink.LinkColor = Theme.GreenDark;
            updateLink.ActiveLinkColor = Theme.Green;
            updateLink.LinkBehavior = LinkBehavior.HoverUnderline;
            updateLink.Margin = Padding.Empty;
            updateLink.LinkClicked += delegate { CheckForUpdate(true); };
            footer.Controls.Add(updateLink, 1, 0);

            return footer;
        }

        private Button NewSecondaryButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Theme.Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Theme.HeaderRow;
            button.BackColor = Theme.Card;
            button.ForeColor = Theme.Text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(S(8), S(3), S(8), S(3));
            button.Margin = new Padding(S(8), 0, 0, 0);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private Label NewCaption(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.ForeColor = Theme.Muted;
            label.Margin = new Padding(S(3), S(7), S(2), 0);
            return label;
        }

        private void List_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush back = new SolidBrush(Theme.HeaderRow))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
            }
            using (Pen line = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            Rectangle text = CellTextBounds(e.Bounds, e.ColumnIndex);
            TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpper(), headerFont, text, Theme.Muted,
                CellFlags(e.Header.TextAlign));
        }

        private void List_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            // Pour la colonne 0, Bounds couvre toute la ligne.
            Rectangle bounds = e.Bounds;
            if (e.ColumnIndex == 0) bounds.Width = list.Columns[0].Width;

            Color backColor = e.Item.Selected ? Theme.SelectedRow : (e.ItemIndex % 2 == 0 ? Theme.Card : Theme.AltRow);
            using (SolidBrush back = new SolidBrush(backColor))
            {
                e.Graphics.FillRectangle(back, bounds);
            }

            if (e.ColumnIndex == 0)
            {
                CheckBoxState state = e.Item.Checked ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal;
                Size glyph = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
                CheckBoxRenderer.DrawCheckBox(e.Graphics,
                    new Point(bounds.Left + S(10), bounds.Top + (bounds.Height - glyph.Height) / 2), state);
            }

            bool dimmed = e.Item.ForeColor == Theme.Muted;
            Color color = dimmed ? Theme.Muted : Theme.Text;
            string text = e.SubItem.Text;
            if (e.ColumnIndex == StateColumn)
            {
                color = e.Item.ForeColor;
                if (text.Length > 0) text = "●  " + text;
            }
            else if (e.ColumnIndex != 0)
            {
                color = Theme.Muted;
            }

            TextRenderer.DrawText(e.Graphics, text, list.Font, CellTextBounds(bounds, e.ColumnIndex), color,
                CellFlags(e.Header.TextAlign));
        }

        private Rectangle CellTextBounds(Rectangle bounds, int column)
        {
            int left = column == 0 ? S(36) : S(10);
            return new Rectangle(bounds.Left + left, bounds.Top, Math.Max(0, bounds.Width - left - S(10)), bounds.Height);
        }

        private static TextFormatFlags CellFlags(HorizontalAlignment align)
        {
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
            return flags | (align == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
        }

        // La colonne Document occupe la largeur restante.
        private void FitColumns()
        {
            if (fitting || list.Columns.Count == 0) return;
            fitting = true;
            int others = 0;
            for (int i = 1; i < list.Columns.Count; i++)
            {
                others += list.Columns[i].Width;
            }
            int width = Math.Max(S(180), list.ClientSize.Width - others);
            if (list.Columns[0].Width != width) list.Columns[0].Width = width;
            fitting = false;
        }

        // Pendant une impression les cases sont verrouillées.
        private void List_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (worker.IsBusy && !filling) e.NewValue = e.CurrentValue;
        }

        // Un clic n'importe où sur la ligne coche ou décoche ; la case elle-même est gérée par Windows.
        private void List_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || worker.IsBusy) return;
            ListViewHitTestInfo hit = list.HitTest(e.Location);
            if (hit.Item == null || hit.Location == ListViewHitTestLocations.StateImage) return;
            hit.Item.Checked = !hit.Item.Checked;
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
                statusLabel.Text = "Liste des imprimantes illisible : " + ex.Message;
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
            string error = null;
            try
            {
                docs = DocumentScanner.Scan(folder, DateTime.Today);
            }
            catch (Exception ex)
            {
                docs = new List<DocItem>();
                error = "Lecture du dossier impossible : " + ex.Message;
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
                row.ForeColor = already ? Theme.Muted : Theme.Text;
                list.Items.Add(row);
                rows[doc] = row;
            }
            list.EndUpdate();
            filling = false;

            list.Visible = docs.Count > 0;
            emptyLabel.Visible = docs.Count == 0;
            FitColumns();

            folderLabel.Text = folder + "  ·  " + DateTime.Today.ToString("dd/MM/yyyy");
            progress.Value = 0;
            statusLabel.Text = error ?? "";
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
                questionLabel.Text = "Rien à imprimer";
            }
            else if (count == 0)
            {
                questionLabel.Text = "Aucun document coché sur " + list.Items.Count;
            }
            else
            {
                questionLabel.Text = "Imprimer " + (count == 1 ? "ce document" : "ces " + count + " documents") + " ?";
            }
            printButton.Text = count > 0 ? "Imprimer (" + count + ")" : "Imprimer";
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
                MessageBox.Show(this, "Choisissez d'abord une imprimante.", AppName,
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
            if (MessageBox.Show(this, message, AppName, MessageBoxButtons.YesNo, icon) != DialogResult.Yes)
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
            SetBusy(true);
            worker.RunWorkerAsync(job);
        }

        private void SetBusy(bool busy)
        {
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
                row.SubItems[StateColumn].Text = dryRun ? "Simulé" : "Imprimé";
                row.ForeColor = Theme.Success;
                row.Checked = false;
                if (!dryRun) settings.MarkPrinted(result.Doc);
            }
            else
            {
                failCount++;
                row.SubItems[StateColumn].Text = "Échec : " + result.Error;
                row.ForeColor = Theme.Danger;
            }
            filling = false;

            row.EnsureVisible();
            progress.Value = e.ProgressPercentage;
            statusLabel.Text = e.ProgressPercentage + " / " + jobTotal + "  ·  " + result.Doc.Name;
        }

        private void Worker_Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            SetBusy(false);
            UpdateQuestion();

            string summary = okCount + " imprimé(s), " + failCount + " en échec";
            if (e.Error != null) summary += ". Erreur : " + e.Error.Message;
            else if (e.Cancelled) summary += ", annulé avant la fin (" + (jobTotal - okCount - failCount) + " non traité(s))";
            statusLabel.Text = "Terminé : " + summary + ".";

            string text = summary + ".";
            if (failCount > 0)
            {
                text += "\n\nLes documents en échec restent cochés : cliquez de nouveau sur Imprimer pour réessayer.";
            }
            MessageBox.Show(this, text, AppName, MessageBoxButtons.OK,
                failCount > 0 || e.Error != null ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
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
                    MessageBox.Show(this, "Vérification impossible : " + error.Message, AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(this, AppName + " " + Updater.CurrentVersion + " est à jour.", AppName,
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
                        MessageBox.Show(this, "Mise à jour impossible : " + failure.Message, AppName,
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
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
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
