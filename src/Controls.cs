using System;
using System.Drawing;
using System.Windows.Forms;

namespace A3Print
{
    // Couleurs reprises du logo : ardoise, vert, orange. Les couples texte/fond respectent un contraste de 4,5:1.
    internal static class Theme
    {
        public static readonly Color Slate = Color.FromArgb(47, 58, 69);
        public static readonly Color Green = Color.FromArgb(84, 128, 4);
        public static readonly Color GreenDark = Color.FromArgb(66, 104, 0);
        public static readonly Color GreenBright = Color.FromArgb(132, 180, 24);
        public static readonly Color Background = Color.FromArgb(243, 245, 247);
        public static readonly Color Card = Color.White;
        public static readonly Color AltRow = Color.FromArgb(249, 250, 251);
        public static readonly Color SelectedRow = Color.FromArgb(234, 242, 222);
        public static readonly Color HeaderRow = Color.FromArgb(246, 247, 249);
        public static readonly Color Border = Color.FromArgb(218, 223, 228);
        public static readonly Color Text = Color.FromArgb(31, 39, 48);
        public static readonly Color Muted = Color.FromArgb(98, 110, 122);
        public static readonly Color HeaderMuted = Color.FromArgb(190, 199, 207);
        public static readonly Color Success = Color.FromArgb(46, 125, 50);
        public static readonly Color Warning = Color.FromArgb(176, 92, 0);
        public static readonly Color Danger = Color.FromArgb(198, 40, 40);
        public static readonly Color Disabled = Color.FromArgb(205, 211, 217);

        public static readonly Color InfoBack = Color.FromArgb(233, 238, 243);
        public static readonly Color SuccessBack = Color.FromArgb(236, 245, 225);
        public static readonly Color WarningBack = Color.FromArgb(255, 243, 224);
        public static readonly Color DangerBack = Color.FromArgb(253, 236, 234);

        // Bordure fine autour d'un panneau.
        public static void AddBorder(Control control)
        {
            control.Paint += delegate(object sender, PaintEventArgs e)
            {
                using (Pen pen = new Pen(Border))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
                }
            };
            control.Resize += delegate { control.Invalidate(); };
        }
    }

    internal sealed class BufferedListView : ListView
    {
        public BufferedListView()
        {
            DoubleBuffered = true;
        }
    }

    internal sealed class ThinProgress : Control
    {
        private int maximum = 1;
        private int current;
        private Color barColor = Theme.GreenBright;

        public ThinProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public int Maximum
        {
            get { return maximum; }
            set { maximum = Math.Max(1, value); Invalidate(); }
        }

        public int Value
        {
            get { return current; }
            set { current = Math.Max(0, Math.Min(maximum, value)); Invalidate(); }
        }

        public Color BarColor
        {
            get { return barColor; }
            set { barColor = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush track = new SolidBrush(Theme.Border))
            {
                e.Graphics.FillRectangle(track, ClientRectangle);
            }
            int width = (int)((long)Width * current / maximum);
            if (width > 0)
            {
                using (SolidBrush fill = new SolidBrush(barColor))
                {
                    e.Graphics.FillRectangle(fill, 0, 0, width, Height);
                }
            }
        }
    }

    internal enum NoticeKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    // Bandeau de message dans la fenêtre : titre, explication, et l'action qui permet de s'en sortir.
    internal sealed class NoticeBar : Panel
    {
        private readonly Label title = new Label();
        private readonly Label body = new Label();
        private readonly Button action = new Button();
        private readonly Button close = new Button();
        private readonly ToolTip tip = new ToolTip();
        private readonly int stripeWidth;
        private Color stripe = Theme.Slate;
        private EventHandler handler;

        public NoticeBar(float scale)
        {
            stripeWidth = Scale(4, scale);
            Height = Scale(70, scale);
            Visible = false;
            Padding = new Padding(stripeWidth, 0, 0, 0);
            SetStyle(ControlStyles.ResizeRedraw, true);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.BackColor = Color.Transparent;
            layout.Padding = new Padding(Scale(16, scale), Scale(8, scale), Scale(10, scale), Scale(8, scale));
            layout.ColumnCount = 3;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            title.AutoSize = true;
            title.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold);
            title.Margin = new Padding(0, 0, 0, Scale(2, scale));
            layout.Controls.Add(title, 0, 0);

            body.AutoSize = false;
            body.AutoEllipsis = true;
            body.Dock = DockStyle.Fill;
            body.ForeColor = Theme.Text;
            body.Margin = Padding.Empty;
            layout.Controls.Add(body, 0, 1);

            StyleButton(action, scale);
            action.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold);
            action.Click += delegate { if (handler != null) handler(this, EventArgs.Empty); };
            layout.Controls.Add(action, 1, 0);
            layout.SetRowSpan(action, 2);

            StyleButton(close, scale);
            close.Text = "Fermer";
            close.Click += delegate { HideNotice(); };
            layout.Controls.Add(close, 2, 0);
            layout.SetRowSpan(close, 2);

            Controls.Add(layout);
        }

        private static int Scale(int value, float scale)
        {
            return (int)Math.Round(value * scale);
        }

        private static void StyleButton(Button button, float scale)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Theme.Border;
            button.FlatAppearance.BorderSize = 1;
            button.BackColor = Theme.Card;
            button.ForeColor = Theme.Text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.Padding = new Padding(Scale(8, scale), Scale(3, scale), Scale(8, scale), Scale(3, scale));
            button.Margin = new Padding(Scale(8, scale), 0, 0, 0);
            button.Anchor = AnchorStyles.Right;
            button.Cursor = Cursors.Hand;
        }

        // actionText null : pas de bouton d'action. closable false : le bandeau reste tant que la situation dure.
        public void ShowNotice(NoticeKind kind, string titleText, string bodyText, string actionText,
            EventHandler onAction, bool closable)
        {
            switch (kind)
            {
                case NoticeKind.Success: stripe = Theme.Success; BackColor = Theme.SuccessBack; break;
                case NoticeKind.Warning: stripe = Theme.Warning; BackColor = Theme.WarningBack; break;
                case NoticeKind.Error: stripe = Theme.Danger; BackColor = Theme.DangerBack; break;
                default: stripe = Theme.Slate; BackColor = Theme.InfoBack; break;
            }
            title.ForeColor = stripe;
            title.Text = titleText;
            body.Text = bodyText;
            tip.SetToolTip(body, bodyText);
            handler = onAction;
            action.Text = actionText ?? "";
            action.Visible = actionText != null;
            close.Visible = closable;
            Visible = true;
            Invalidate();
        }

        public void HideNotice()
        {
            handler = null;
            Visible = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (SolidBrush brush = new SolidBrush(stripe))
            {
                e.Graphics.FillRectangle(brush, 0, 0, stripeWidth, Height);
            }
        }
    }
}
