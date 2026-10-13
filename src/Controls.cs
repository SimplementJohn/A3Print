using System;
using System.Drawing;
using System.Windows.Forms;

namespace A3Print
{
    // Couleurs reprises du logo : ardoise, vert, orange.
    internal static class Theme
    {
        public static readonly Color Slate = Color.FromArgb(47, 58, 69);
        public static readonly Color Green = Color.FromArgb(122, 170, 20);
        public static readonly Color GreenDark = Color.FromArgb(101, 145, 12);
        public static readonly Color Orange = Color.FromArgb(243, 146, 0);
        public static readonly Color Background = Color.FromArgb(243, 245, 247);
        public static readonly Color Card = Color.White;
        public static readonly Color AltRow = Color.FromArgb(249, 250, 251);
        public static readonly Color SelectedRow = Color.FromArgb(234, 242, 222);
        public static readonly Color HeaderRow = Color.FromArgb(246, 247, 249);
        public static readonly Color Border = Color.FromArgb(218, 223, 228);
        public static readonly Color Text = Color.FromArgb(31, 39, 48);
        public static readonly Color Muted = Color.FromArgb(108, 120, 132);
        public static readonly Color HeaderMuted = Color.FromArgb(190, 199, 207);
        public static readonly Color Success = Color.FromArgb(56, 142, 60);
        public static readonly Color Danger = Color.FromArgb(198, 40, 40);
        public static readonly Color Disabled = Color.FromArgb(205, 211, 217);

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

        protected override void OnPaint(PaintEventArgs e)
        {
            using (SolidBrush track = new SolidBrush(Theme.Border))
            {
                e.Graphics.FillRectangle(track, ClientRectangle);
            }
            int width = (int)((long)Width * current / maximum);
            if (width > 0)
            {
                using (SolidBrush fill = new SolidBrush(Theme.Green))
                {
                    e.Graphics.FillRectangle(fill, 0, 0, width, Height);
                }
            }
        }
    }
}
