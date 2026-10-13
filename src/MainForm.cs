using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
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

        private sealed class PauseInfo
        {
            public bool Paused;
            public string Reason;
            public bool AutoResume;
        }

        private const string AppName = "A3Print";
        private const int StateColumn = 3;
        private const int PrinterCheckEvery = 5;
        private const int MaxConsecutiveFailures = 3;
        private const int PauseRecheckMs = 5000;
        private static readonly string[] VirtualPrinterHints = { "PDF", "OneNote", "XPS", "Fax" };
        private static readonly CultureInfo French = new CultureInfo("fr-FR");

        private readonly bool dryRun;
        private readonly string folder;
        private readonly Settings settings = new Settings();
        private readonly BatchJournal journal;
        private readonly Dictionary<DocItem, ListViewItem> rows = new Dictionary<DocItem, ListViewItem>();
        private readonly BackgroundWorker worker = new BackgroundWorker();
        private readonly ManualResetEvent resumeSignal = new ManualResetEvent(false);

        private float scale = 1F;
        private Font headerFont;
        private Label questionLabel;
        private Label folderLabel;
        private Button previousDayButton;
        private Button dayButton;
        private Button nextDayButton;
        private Button todayButton;
        private ToolStripDropDown calendarPopup;
        private MonthCalendar calendar;
        private DateTime selectedDay = DateTime.Today;
        private BufferedListView list;
        private Label emptyLabel;
        private Button checkAllButton;
        private Button uncheckAllButton;
        private Button refreshButton;
        private ComboBox printerCombo;
        private NumericUpDown copiesInput;
        private ComboBox duplexCombo;
        private NoticeBar notice;
        private Button printButton;
        private Button cancelButton;
        private ThinProgress progress;
        private Label statusLabel;
        private LinkLabel updateLink;

        private bool filling;
        private bool fitting;
        private bool followToday = true;
        private bool paused;
        private bool closeWhenDone;
        private int okCount;
        private int failCount;
        private int jobTotal;
        private string jobPrinter = "";

        private UpdateInfo pendingUpdate;
        private bool updating;
        private volatile bool updateCancelled;

        public MainForm(bool dryRun, string folder)
        {
            this.dryRun = dryRun;
            this.folder = string.IsNullOrEmpty(folder) ? DocumentScanner.GetDownloadsFolder() : folder;
            journal = new BatchJournal(!dryRun);

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
            ShowInterruptedBatch();

            Shown += delegate { CheckForUpdate(false); };
            Activated += delegate
            {
                // Fenêtre restée ouverte depuis la veille : on repasse sur le jour courant.
                if (followToday && !IsBusy && selectedDay != DateTime.Today) RefreshList();
            };
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
            ClientSize = new Size(S(900), S(720));
            MinimumSize = new Size(S(760), S(600));
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
            logoBox.AccessibleName = "Logo A3 PRO";
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
            subtitle.Text = "Impression en lot des PDF FA / AV";
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
            body.RowCount = 5;
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            body.Controls.Add(BuildTopBar(), 0, 0);
            body.Controls.Add(BuildListCard(), 0, 1);
            body.Controls.Add(BuildOptions(), 0, 2);

            notice = new NoticeBar(scale);
            notice.Dock = DockStyle.Fill;
            notice.Margin = new Padding(0, S(12), 0, 0);
            body.Controls.Add(notice, 0, 3);

            body.Controls.Add(BuildRunBar(), 0, 4);
            return body;
        }

        // Question, choix du jour et actions sur la liste.
        private Control BuildTopBar()
        {
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
            folderLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            folderLabel.ForeColor = Theme.Muted;
            folderLabel.Margin = new Padding(S(2), S(2), 0, 0);
            top.Controls.Add(folderLabel, 0, 1);

            FlowLayoutPanel tools = NewToolRow();
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

            top.Controls.Add(BuildDayBar(), 1, 1);
            return top;
        }

        // Choix du jour : jour précédent, date (ouvre un calendrier), jour suivant, retour à aujourd'hui.
        private Control BuildDayBar()
        {
            FlowLayoutPanel day = NewToolRow();
            day.Margin = new Padding(0, S(8), 0, 0);

            Label caption = NewCaption("Documents du");
            caption.Margin = new Padding(0, S(8), S(8), 0);
            day.Controls.Add(caption);

            previousDayButton = NewDayButton("\u2039", S(34), "Jour précédent");
            previousDayButton.Font = new Font("Segoe UI", 12F);
            previousDayButton.Click += delegate { SetDay(selectedDay.AddDays(-1)); };
            day.Controls.Add(previousDayButton);

            dayButton = NewDayButton("", S(230), "Jour des documents, ouvre le calendrier");
            dayButton.Margin = new Padding(S(4), 0, 0, 0);
            dayButton.Click += delegate { ShowCalendar(); };
            day.Controls.Add(dayButton);

            nextDayButton = NewDayButton("\u203A", S(34), "Jour suivant");
            nextDayButton.Font = new Font("Segoe UI", 12F);
            nextDayButton.Margin = new Padding(S(4), 0, 0, 0);
            nextDayButton.Click += delegate { SetDay(selectedDay.AddDays(1)); };
            day.Controls.Add(nextDayButton);

            todayButton = NewDayButton("Aujourd'hui", S(112), "Revenir à aujourd'hui");
            todayButton.Margin = new Padding(S(8), 0, 0, 0);
            todayButton.Click += delegate { SetDay(DateTime.Today); };
            day.Controls.Add(todayButton);

            calendar = new MonthCalendar();
            calendar.MaxSelectionCount = 1;
            calendar.DateSelected += delegate(object sender, DateRangeEventArgs e)
            {
                calendarPopup.Close();
                SetDay(e.Start);
            };
            ToolStripControlHost host = new ToolStripControlHost(calendar);
            host.Margin = Padding.Empty;
            host.Padding = Padding.Empty;
            host.AutoSize = false;
            host.Size = calendar.Size;
            calendarPopup = new ToolStripDropDown();
            calendarPopup.Padding = Padding.Empty;
            calendarPopup.Items.Add(host);

            return day;
        }

        private Button NewDayButton(string text, int width, string accessibleName)
        {
            Button button = NewSecondaryButton(text);
            button.AutoSize = false;
            button.Size = new Size(width, S(32));
            button.Padding = Padding.Empty;
            button.Margin = Padding.Empty;
            button.AccessibleName = accessibleName;
            return button;
        }

        private void ShowCalendar()
        {
            calendar.MaxDate = DateTime.Today;
            calendar.SetDate(selectedDay);
            calendarPopup.Show(dayButton, new Point(0, dayButton.Height + S(2)));
        }

        private void SetDay(DateTime day)
        {
            if (IsBusy) return;
            if (day.Date > DateTime.Today) day = DateTime.Today;
            selectedDay = day.Date;
            followToday = selectedDay == DateTime.Today;
            RefreshList();
        }

        private FlowLayoutPanel NewToolRow()
        {
            FlowLayoutPanel row = new FlowLayoutPanel();
            row.AutoSize = true;
            row.WrapContents = false;
            row.Anchor = AnchorStyles.Right;
            row.Margin = Padding.Empty;
            return row;
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
            list.AccessibleName = "Documents à imprimer";
            list.Columns.Add("Document", S(360));
            list.Columns.Add("Taille", S(90), HorizontalAlignment.Right);
            list.Columns.Add("Heure", S(80), HorizontalAlignment.Right);
            list.Columns.Add("État", S(290));
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

            card.Controls.Add(list);
            card.Controls.Add(emptyLabel);
            return card;
        }

        private Control BuildOptions()
        {
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
            printerCombo.AccessibleName = "Imprimante";
            options.Controls.Add(printerCombo);

            options.Controls.Add(NewCaption("Copies"));
            copiesInput = new NumericUpDown();
            copiesInput.Minimum = 1;
            copiesInput.Maximum = 99;
            copiesInput.Width = S(54);
            copiesInput.Margin = new Padding(S(3), S(3), S(18), S(3));
            copiesInput.AccessibleName = "Nombre de copies";
            options.Controls.Add(copiesInput);

            options.Controls.Add(NewCaption("Mode"));
            duplexCombo = new ComboBox();
            duplexCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            duplexCombo.FlatStyle = FlatStyle.Flat;
            duplexCombo.Width = S(200);
            duplexCombo.AccessibleName = "Recto ou recto-verso";
            duplexCombo.Items.AddRange(new object[]
            {
                "Réglage de l'imprimante", "Recto seul", "Recto-verso (bord long)", "Recto-verso (bord court)"
            });
            options.Controls.Add(duplexCombo);
            return options;
        }

        // Progression, statut et boutons d'action.
        private Control BuildRunBar()
        {
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
            progress.AccessibleName = "Progression";
            run.Controls.Add(progress, 0, 0);

            statusLabel = new Label();
            statusLabel.AutoSize = false;
            statusLabel.AutoEllipsis = true;
            statusLabel.Dock = DockStyle.Fill;
            statusLabel.ForeColor = Theme.Muted;
            statusLabel.Margin = new Padding(0, 0, S(16), 0);
            run.Controls.Add(statusLabel, 0, 1);

            cancelButton = NewSecondaryButton("Arrêter");
            cancelButton.AutoSize = false;
            cancelButton.Size = new Size(S(104), S(42));
            cancelButton.Margin = new Padding(0, 0, S(8), 0);
            cancelButton.Enabled = false;
            cancelButton.Click += CancelButton_Click;
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
            return run;
        }

        private Control BuildFooter()
        {
            TableLayoutPanel footer = new TableLayoutPanel();
            footer.Dock = DockStyle.Fill;
            footer.AutoSize = true;
            footer.Margin = Padding.Empty;
            footer.Padding = new Padding(S(20), S(4), S(20), S(10));
            footer.ColumnCount = 3;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            Label version = new Label();
            version.Text = AppName + " " + Updater.CurrentVersion;
            version.AutoSize = true;
            version.ForeColor = Theme.Muted;
            version.Margin = Padding.Empty;
            footer.Controls.Add(version, 0, 0);

            LinkLabel logLink = NewLink("Journal");
            logLink.Margin = new Padding(0, 0, S(16), 0);
            logLink.LinkClicked += delegate { OpenLog(); };
            footer.Controls.Add(logLink, 1, 0);

            updateLink = NewLink("Rechercher une mise à jour");
            updateLink.LinkClicked += delegate
            {
                if (pendingUpdate != null) StartUpdate();
                else CheckForUpdate(true);
            };
            footer.Controls.Add(updateLink, 2, 0);

            return footer;
        }

        private static LinkLabel NewLink(string text)
        {
            LinkLabel link = new LinkLabel();
            link.Text = text;
            link.AutoSize = true;
            link.LinkColor = Theme.GreenDark;
            link.ActiveLinkColor = Theme.Green;
            link.LinkBehavior = LinkBehavior.HoverUnderline;
            link.Margin = Padding.Empty;
            return link;
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
            button.MinimumSize = new Size(0, S(32));
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

        private void OpenLog()
        {
            try
            {
                Log.Write("Ouverture du journal");
                Process.Start("notepad.exe", "\"" + Log.FilePath + "\"");
            }
            catch (Exception ex)
            {
                notice.ShowNotice(NoticeKind.Error, "Journal inaccessible", ex.Message + "  (" + Log.FilePath + ")", null, null, true);
            }
        }

        // ---- Liste --------------------------------------------------------------------------------

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

        // Pendant une impression ou une mise à jour les cases sont verrouillées.
        private void List_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (IsBusy && !filling) e.NewValue = e.CurrentValue;
        }

        // Un clic n'importe où sur la ligne coche ou décoche ; la case elle-même est gérée par Windows.
        private void List_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || IsBusy) return;
            ListViewHitTestInfo hit = list.HitTest(e.Location);
            if (hit.Item == null || hit.Location == ListViewHitTestLocations.StateImage) return;
            hit.Item.Checked = !hit.Item.Checked;
        }

        private bool IsBusy
        {
            get { return worker.IsBusy || updating; }
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
                Log.Error("Liste des imprimantes", ex);
                notice.ShowNotice(NoticeKind.Error, "Imprimantes illisibles",
                    "Windows n'a pas renvoyé la liste des imprimantes (" + ex.Message + "). Vérifiez le service Spouleur d'impression, puis relancez A3Print.",
                    null, null, true);
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
            if (followToday) selectedDay = DateTime.Today;
            DateTime day = selectedDay;
            bool isToday = day == DateTime.Today;
            dayButton.Text = day.ToString("dddd d MMMM yyyy", French) + "  \u25BE";
            string dayText = isToday ? "aujourd'hui" : "le " + day.ToString("dd/MM/yyyy");

            List<DocItem> docs;
            string error = null;
            try
            {
                docs = DocumentScanner.Scan(folder, day);
            }
            catch (Exception ex)
            {
                docs = new List<DocItem>();
                error = "Lecture du dossier impossible : " + ex.Message;
                Log.Error("Lecture du dossier " + folder, ex);
            }

            filling = true;
            list.BeginUpdate();
            list.Items.Clear();
            rows.Clear();
            foreach (DocItem doc in docs)
            {
                bool already = settings.IsPrinted(doc);
                bool uncertain = !already && journal.IsUncertain(doc);
                ListViewItem row = new ListViewItem(doc.Name);
                row.SubItems.Add(FormatSize(doc.Size));
                row.SubItems.Add(doc.Time.ToString("HH:mm"));
                row.SubItems.Add(already ? "Déjà envoyé" : (uncertain ? "À vérifier : envoi coupé" : ""));
                row.Checked = !already && !uncertain;
                row.Tag = doc;
                row.ForeColor = already ? Theme.Muted : (uncertain ? Theme.Warning : Theme.Text);
                list.Items.Add(row);
                rows[doc] = row;
            }
            list.EndUpdate();
            filling = false;

            list.Visible = docs.Count > 0;
            emptyLabel.Visible = docs.Count == 0;
            emptyLabel.Text = error != null
                ? error
                : "Aucun PDF FA / AV téléchargé " + dayText + ".\n\nTéléchargez vos documents, puis cliquez sur Actualiser.";
            FitColumns();

            // Aujourd'hui reste cliquable : en vert quand c'est le jour affiché, plutôt que grisé.
            todayButton.ForeColor = isToday ? Theme.GreenDark : Theme.Text;
            todayButton.FlatAppearance.BorderColor = isToday ? Theme.Green : Theme.Border;
            nextDayButton.Enabled = !isToday && !IsBusy;
            folderLabel.Text = folder;
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
            printButton.Enabled = count > 0 && !IsBusy;
        }

        // Un lot précédent ne s'est pas terminé proprement : on dit où on en est.
        private void ShowInterruptedBatch()
        {
            if (!journal.Interrupted) return;

            string when = journal.InterruptedAt == DateTime.MinValue ? "" : " du " + journal.InterruptedAt.ToString("dd/MM à HH:mm");
            string text = "Le lot" + when + " vers « " + journal.InterruptedPrinter + " » a été coupé avant la fin. " +
                          "Les documents déjà envoyés sont décochés, les autres restent cochés.";
            if (journal.InterruptedDocName != null)
            {
                text += " « " + journal.InterruptedDocName + " » partait au moment de la coupure : vérifiez s'il est sorti avant de le recocher.";
            }
            notice.ShowNotice(NoticeKind.Warning, "Impression interrompue la dernière fois", text, "Compris",
                delegate
                {
                    journal.End();
                    notice.HideNotice();
                }, false);
        }

        // ---- Impression ---------------------------------------------------------------------------

        private void PrintButton_Click(object sender, EventArgs e)
        {
            if (IsBusy) return;

            PrintJob job = new PrintJob();
            job.Docs = new List<DocItem>();
            foreach (ListViewItem row in list.CheckedItems)
            {
                job.Docs.Add((DocItem)row.Tag);
            }
            if (job.Docs.Count == 0) return;

            if (printerCombo.SelectedItem == null)
            {
                notice.ShowNotice(NoticeKind.Warning, "Aucune imprimante choisie",
                    "Sélectionnez l'imprimante dans la liste ci-dessus, puis cliquez de nouveau sur Imprimer.", null, null, true);
                printerCombo.Focus();
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
            if (!dryRun)
            {
                PrinterState state = PrinterMonitor.Check(job.Printer);
                if (!state.Ready)
                {
                    message += "\n\nAttention : l'imprimante semble indisponible (" + state.Problem +
                               "). L'impression se mettra en pause jusqu'à son retour.";
                    icon = MessageBoxIcon.Warning;
                }
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
            paused = false;
            jobTotal = job.Docs.Count;
            jobPrinter = job.Printer;
            progress.Maximum = jobTotal;
            progress.Value = 0;
            progress.BarColor = Theme.GreenBright;
            notice.HideNotice();
            statusLabel.Text = "Impression en cours...";
            Native.SetTaskbar(Handle, TaskbarState.Normal, 0, jobTotal);
            Log.Write("Lot : " + jobTotal + " document(s) vers " + job.Printer + (dryRun ? " (simulation)" : ""));
            journal.Begin(job.Printer);
            resumeSignal.Reset();
            SetBusy(true);
            worker.RunWorkerAsync(job);
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            cancelButton.Enabled = false;
            if (updating)
            {
                updateCancelled = true;
                statusLabel.Text = "Annulation du téléchargement...";
                return;
            }
            statusLabel.Text = "Arrêt après le document en cours...";
            worker.CancelAsync();
            resumeSignal.Set();
        }

        private void SetBusy(bool busy)
        {
            checkAllButton.Enabled = !busy;
            uncheckAllButton.Enabled = !busy;
            refreshButton.Enabled = !busy;
            previousDayButton.Enabled = !busy;
            dayButton.Enabled = !busy;
            nextDayButton.Enabled = !busy && selectedDay != DateTime.Today;
            todayButton.Enabled = !busy;
            printerCombo.Enabled = !busy;
            copiesInput.Enabled = !busy;
            duplexCombo.Enabled = !busy;
            updateLink.Enabled = !busy;
            cancelButton.Text = updating ? "Annuler" : "Arrêter";
            cancelButton.Enabled = busy;
            printButton.Enabled = !busy && list.CheckedItems.Count > 0;
        }

        private void Worker_DoWork(object sender, DoWorkEventArgs e)
        {
            PrintJob job = (PrintJob)e.Argument;
            Native.KeepAwake(true);
            try
            {
                int consecutiveFailures = 0;
                for (int i = 0; i < job.Docs.Count; i++)
                {
                    if (worker.CancellationPending)
                    {
                        e.Cancel = true;
                        return;
                    }

                    // Contrôle régulier : inutile d'empiler 300 travaux sur une imprimante hors ligne.
                    if (!dryRun && i % PrinterCheckEvery == 0 && !WaitUntilPrinterReady(job.Printer))
                    {
                        e.Cancel = true;
                        return;
                    }

                    PrintResult result = new PrintResult();
                    result.Doc = job.Docs[i];
                    bool stopped;

                    // Ordre voulu pour une coupure : « en cours » écrit avant l'envoi, effacé après l'historique.
                    journal.Sending(result.Doc);
                    result.Ok = PrintWithRetry(result.Doc, job, out result.Error, out stopped);
                    if (result.Ok && !dryRun) settings.MarkPrinted(result.Doc);
                    journal.Sent();

                    if (stopped)
                    {
                        e.Cancel = true;
                        return;
                    }
                    worker.ReportProgress(i + 1, result);

                    if (result.Ok)
                    {
                        consecutiveFailures = 0;
                    }
                    else if (++consecutiveFailures >= MaxConsecutiveFailures && i + 1 < job.Docs.Count)
                    {
                        consecutiveFailures = 0;
                        string reason = MaxConsecutiveFailures + " documents de suite ont échoué (dernier : " + result.Error + ").";
                        if (!Pause(reason, null))
                        {
                            e.Cancel = true;
                            return;
                        }
                    }
                }
            }
            finally
            {
                Native.KeepAwake(false);
            }
        }

        // Un refus net de l'imprimante est retenté : après son retour si elle est en panne, une fois sinon.
        private bool PrintWithRetry(DocItem doc, PrintJob job, out string error, out bool stopped)
        {
            stopped = false;
            for (int attempt = 1; ; attempt++)
            {
                bool retryable;
                if (PdfPrinter.Print(doc.Path, job.Printer, job.Copies, job.Duplex, dryRun, out error, out retryable))
                {
                    return true;
                }
                Log.Write("Échec " + doc.Name + " (essai " + attempt + ") : " + error);
                if (!retryable || dryRun) return false;

                if (!PrinterMonitor.Check(job.Printer).Ready)
                {
                    if (!WaitUntilPrinterReady(job.Printer))
                    {
                        stopped = true;
                        return false;
                    }
                    attempt = 0;
                    continue;
                }
                if (attempt >= 2) return false;
                Thread.Sleep(1500);
            }
        }

        private bool WaitUntilPrinterReady(string printer)
        {
            PrinterState state = PrinterMonitor.Check(printer);
            if (state.Ready) return true;
            return Pause("Imprimante indisponible : " + state.Problem + ".", printer);
        }

        // Met le lot en pause. Reprise : bouton Reprendre, ou retour de l'imprimante si printer est fourni.
        // Renvoie false si l'utilisateur arrête le lot.
        private bool Pause(string reason, string printer)
        {
            Log.Write("Pause : " + reason);
            resumeSignal.Reset();
            PauseInfo info = new PauseInfo();
            info.Paused = true;
            info.Reason = reason;
            info.AutoResume = printer != null;
            worker.ReportProgress(-1, info);

            while (true)
            {
                bool resumed = resumeSignal.WaitOne(PauseRecheckMs);
                if (worker.CancellationPending) return false;
                if (resumed || (printer != null && PrinterMonitor.Check(printer).Ready)) break;
            }

            Log.Write("Reprise");
            worker.ReportProgress(-1, new PauseInfo());
            return true;
        }

        private void Worker_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            PauseInfo pause = e.UserState as PauseInfo;
            if (pause != null)
            {
                OnPauseChanged(pause);
                return;
            }

            PrintResult result = (PrintResult)e.UserState;
            ListViewItem row = rows[result.Doc];

            filling = true;
            if (result.Ok)
            {
                okCount++;
                row.SubItems[StateColumn].Text = dryRun ? "Simulé" : "Envoyé à l'imprimante";
                row.ForeColor = Theme.Success;
                row.Checked = false;
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
            Native.SetTaskbar(Handle, failCount > 0 ? TaskbarState.Error : TaskbarState.Normal, e.ProgressPercentage, jobTotal);
            statusLabel.Text = e.ProgressPercentage + " / " + jobTotal + "  ·  " + result.Doc.Name +
                               (failCount > 0 ? "  ·  " + failCount + " échec(s)" : "");
        }

        private void OnPauseChanged(PauseInfo pause)
        {
            paused = pause.Paused;
            if (!pause.Paused)
            {
                notice.HideNotice();
                progress.BarColor = Theme.GreenBright;
                statusLabel.Text = "Reprise de l'impression...";
                Native.SetTaskbar(Handle, TaskbarState.Normal, progress.Value, jobTotal);
                return;
            }

            progress.BarColor = Theme.Warning;
            statusLabel.Text = "En pause  ·  " + progress.Value + " / " + jobTotal + " envoyés";
            Native.SetTaskbar(Handle, TaskbarState.Paused, progress.Value, jobTotal);
            Native.Flash(Handle);
            string text = pause.Reason + (pause.AutoResume
                ? " L'impression reprendra toute seule dès que l'imprimante répondra. Rien n'est perdu."
                : " Vérifiez l'imprimante puis reprenez, ou arrêtez : les documents restants resteront cochés.");
            notice.ShowNotice(NoticeKind.Warning, "Impression en pause", text, "Reprendre",
                delegate { resumeSignal.Set(); }, false);
        }

        private void Worker_Completed(object sender, RunWorkerCompletedEventArgs e)
        {
            journal.End();
            paused = false;
            SetBusy(false);
            UpdateQuestion();
            progress.BarColor = Theme.GreenBright;
            Native.SetTaskbar(Handle, TaskbarState.None, 0, 1);

            int untreated = jobTotal - okCount - failCount;
            string sent = okCount + " document(s) " + (dryRun ? "simulé(s)" : "envoyé(s) à « " + jobPrinter + " »");
            Log.Write("Fin du lot : " + okCount + " envoyés, " + failCount + " échecs, " + untreated + " non traités" +
                      (e.Error != null ? ", erreur : " + e.Error.Message : ""));

            if (e.Error != null)
            {
                Log.Error("Lot d'impression", e.Error);
                statusLabel.Text = "Interrompu par une erreur.";
                notice.ShowNotice(NoticeKind.Error, "Impression interrompue par une erreur",
                    e.Error.Message + "  " + sent + ", " + untreated + " non traité(s) restent cochés.",
                    "Reprendre les restants", delegate { PrintButton_Click(this, EventArgs.Empty); }, true);
            }
            else if (e.Cancelled)
            {
                statusLabel.Text = "Arrêté : " + okCount + " / " + jobTotal + ".";
                notice.ShowNotice(NoticeKind.Warning, "Impression arrêtée",
                    sent + ", " + failCount + " en échec. Les " + untreated + " documents non traités restent cochés.",
                    "Reprendre les restants", delegate { PrintButton_Click(this, EventArgs.Empty); }, true);
            }
            else if (failCount > 0)
            {
                statusLabel.Text = "Terminé avec " + failCount + " échec(s).";
                notice.ShowNotice(NoticeKind.Warning, "Terminé avec " + failCount + " échec(s)",
                    sent + ". Les documents en échec restent cochés, la raison est indiquée dans la colonne État.",
                    "Réessayer les échecs", delegate { PrintButton_Click(this, EventArgs.Empty); }, true);
            }
            else
            {
                statusLabel.Text = "Terminé.";
                notice.ShowNotice(NoticeKind.Success, "Impression terminée", sent + ".", null, null, true);
            }

            if (!ContainsFocus) Native.Flash(Handle);
            if (closeWhenDone) Close();
        }

        // ---- Mise à jour --------------------------------------------------------------------------

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
                    Log.Error("Recherche de mise à jour", ex);
                }
                Post(delegate { OnUpdateChecked(info, error, manual); });
            });
        }

        // Exécute sur le fil de l'interface, sans erreur si la fenêtre a été fermée entre-temps.
        private void Post(MethodInvoker action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void OnUpdateChecked(UpdateInfo info, Exception error, bool manual)
        {
            updateLink.Enabled = !IsBusy;

            if (info != null)
            {
                pendingUpdate = info;
                updateLink.Text = "Installer la version " + info.Version;
                // Un message plus important (pause, lot interrompu) n'est pas remplacé.
                if (manual || (!notice.Visible && !IsBusy))
                {
                    notice.ShowNotice(NoticeKind.Info, "Mise à jour disponible",
                        "La version " + info.Version + " est prête (version actuelle : " + Updater.CurrentVersion +
                        "). L'installation prend quelques secondes et relance A3Print.",
                        "Installer", delegate { StartUpdate(); }, true);
                }
                return;
            }

            if (!manual) return;
            if (error != null)
            {
                notice.ShowNotice(NoticeKind.Error, "Vérification impossible", Updater.Describe(error),
                    "Réessayer", delegate { CheckForUpdate(true); }, true);
            }
            else
            {
                notice.ShowNotice(NoticeKind.Success, "A3Print est à jour",
                    "Version " + Updater.CurrentVersion + ", aucune version plus récente.", null, null, true);
            }
        }

        private void StartUpdate()
        {
            if (IsBusy || pendingUpdate == null) return;

            UpdateInfo info = pendingUpdate;
            updating = true;
            updateCancelled = false;
            notice.HideNotice();
            progress.Maximum = 100;
            progress.Value = 0;
            progress.BarColor = Theme.GreenBright;
            statusLabel.Text = "Téléchargement de la version " + info.Version + "...";
            Native.SetTaskbar(Handle, TaskbarState.Normal, 0, 100);
            SetBusy(true);
            Log.Write("Mise à jour vers " + info.Version);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string setup = null;
                Exception failure = null;
                try
                {
                    setup = Updater.Download(info,
                        delegate(int percent)
                        {
                            Post(delegate
                            {
                                if (!updating) return;
                                progress.Value = percent;
                                statusLabel.Text = "Téléchargement de la version " + info.Version + "  ·  " + percent + " %";
                                Native.SetTaskbar(Handle, TaskbarState.Normal, percent, 100);
                            });
                        },
                        delegate { return updateCancelled; });
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                Post(delegate { OnUpdateDownloaded(info, setup, failure); });
            });
        }

        private void OnUpdateDownloaded(UpdateInfo info, string setup, Exception failure)
        {
            if (failure == null)
            {
                try
                {
                    statusLabel.Text = "Installation de la version " + info.Version + "...";
                    cancelButton.Enabled = false;
                    Updater.Launch(setup);
                    Log.Write("Installeur lancé : " + setup);
                    Application.Exit();
                    return;
                }
                catch (Exception ex)
                {
                    failure = new InvalidOperationException("L'installeur n'a pas pu démarrer (" + ex.Message + ").", ex);
                }
            }

            updating = false;
            SetBusy(false);
            UpdateQuestion();
            progress.Value = 0;
            Native.SetTaskbar(Handle, TaskbarState.None, 0, 1);

            if (failure is OperationCanceledException)
            {
                statusLabel.Text = "Mise à jour annulée.";
                return;
            }

            Log.Error("Mise à jour", failure);
            statusLabel.Text = "Mise à jour non installée.";
            notice.ShowNotice(NoticeKind.Error, "Mise à jour non installée",
                Updater.Describe(failure) + " Rien n'a été modifié : A3Print reste en version " + Updater.CurrentVersion + ".",
                "Réessayer", delegate { StartUpdate(); }, true);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (updating)
            {
                updateCancelled = true;
            }
            else if (worker.IsBusy && e.CloseReason == CloseReason.UserClosing && !closeWhenDone)
            {
                DialogResult answer = MessageBox.Show(this,
                    "Une impression est en cours (" + progress.Value + " / " + jobTotal + ").\n\n" +
                    "Arrêter après le document en cours et quitter ?\nLes documents non envoyés resteront cochés à la réouverture.",
                    AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                e.Cancel = true;
                if (answer == DialogResult.Yes && worker.IsBusy)
                {
                    // On laisse le document en cours partir en entier, puis Worker_Completed ferme la fenêtre.
                    closeWhenDone = true;
                    cancelButton.Enabled = false;
                    statusLabel.Text = "Fermeture après le document en cours...";
                    worker.CancelAsync();
                    resumeSignal.Set();
                }
                return;
            }
            else if (worker.IsBusy && closeWhenDone && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }
    }
}
