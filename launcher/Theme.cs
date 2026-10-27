using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WarioLauncher
{
    // Colores "estilo Wario": morado oscuro con amarillo/dorado.
    public static class Theme
    {
        public static readonly Color Back = Color.FromArgb(24, 14, 34);
        public static readonly Color Side = Color.FromArgb(16, 9, 24);
        public static readonly Color SideHover = Color.FromArgb(38, 22, 54);
        public static readonly Color Panel = Color.FromArgb(34, 21, 48);
        public static readonly Color Field = Color.FromArgb(44, 28, 62);
        public static readonly Color FieldBorder = Color.FromArgb(84, 56, 112);
        public static readonly Color Gold = Color.FromArgb(246, 196, 38);
        public static readonly Color GoldDark = Color.FromArgb(196, 146, 18);
        public static readonly Color Purple = Color.FromArgb(118, 52, 170);
        public static readonly Color Text = Color.FromArgb(240, 234, 248);
        public static readonly Color Muted = Color.FromArgb(160, 146, 182);
        public static readonly Color Bad = Color.FromArgb(240, 96, 96);
        public static readonly Color Good = Color.FromArgb(120, 214, 120);

        public static Font UI(float size = 9.75f, FontStyle style = FontStyle.Regular) => new Font("Segoe UI", size, style);
        public static Font Title(float size) => new Font("Arial Black", size, FontStyle.Bold);
    }

    public static class Loc
    {
        public static bool English;
        public static string T(string es, string en) => English ? en : es;
    }

    // Boton del menu lateral.
    public sealed class SideButton : Control
    {
        public bool Selected;
        private bool _hover;

        public SideButton(string text)
        {
            Text = text;
            Height = Dpi.S(42);
            Dock = DockStyle.Top;
            Font = Theme.UI(10.5f);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = Enabled; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Selected || _hover ? Theme.SideHover : Theme.Side);
            if (Selected)
                using (var b = new SolidBrush(Theme.Gold)) e.Graphics.FillRectangle(b, 0, Dpi.S(6), Dpi.S(4), Height - Dpi.S(12));
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Dpi.S(18), 0, Width - Dpi.S(18), Height),
                !Enabled ? Color.FromArgb(90, 78, 108) : Selected ? Theme.Gold : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }
    }

    // Boton plano (dorado para JUGAR, morado para el resto).
    public sealed class FlatBtn : Button
    {
        public FlatBtn(string text, bool primary = false)
        {
            Text = text;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = primary ? 0 : 1;
            FlatAppearance.BorderColor = Theme.FieldBorder;
            BackColor = primary ? Theme.Gold : Theme.Field;
            ForeColor = primary ? Color.FromArgb(40, 16, 60) : Theme.Text;
            FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(255, 214, 80) : Theme.SideHover;
            FlatAppearance.MouseDownBackColor = primary ? Theme.GoldDark : Theme.Purple;
            Font = primary ? Theme.Title(15f) : Theme.UI(9.5f);
            Cursor = Cursors.Hand;
            AutoSize = !primary;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(8, 2, 8, 2);
            UseVisualStyleBackColor = false;
        }
    }

    // ComboBox oscuro (la lista desplegable tambien se pinta oscura).
    public sealed class DarkCombo : ComboBox
    {
        public DarkCombo(int width = 260)
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Field;
            ForeColor = Theme.Text;
            Font = Theme.UI(9.5f);
            Width = Dpi.S(width);
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = Dpi.S(20);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
            using (var b = new SolidBrush(sel ? Theme.Purple : Theme.Field)) e.Graphics.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font,
                new Rectangle(e.Bounds.X + 4, e.Bounds.Y, e.Bounds.Width - 4, e.Bounds.Height), (e.State & DrawItemState.ComboBoxEdit) != 0 ? ForeColor : Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    // Elemento de una lista: texto visible + valor guardado.
    public sealed class Choice
    {
        public readonly string Label;
        public readonly object Value;
        public Choice(string label, object value) { Label = label; Value = value; }
        public override string ToString() => Label;
    }

    // Casilla dibujada a mano: cuadro oscuro con borde; marcada, dorada con una palomita.
    public sealed class DarkCheck : CheckBox
    {
        private bool _hover;

        public DarkCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            AutoSize = false;
            Size = new Size(Dpi.S(22), Dpi.S(22));
            Cursor = Cursors.Hand;
            Text = "";
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int s = Math.Min(Width, Height) - 3;
            var box = new Rectangle(1, 1, s, s);
            using (var path = Rounded(box, Dpi.S(4)))
            {
                using (var fill = new SolidBrush(Checked ? Theme.Gold : Theme.Field)) g.FillPath(fill, path);
                using (var pen = new Pen(Checked ? Theme.Gold : _hover ? Theme.Gold : Theme.FieldBorder, 1.5f)) g.DrawPath(pen, path);
            }
            if (Checked)
            {
                using (var pen = new Pen(Color.FromArgb(40, 16, 60), Dpi.S(2) + 0.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[]
                    {
                        new PointF(box.X + s * 0.24f, box.Y + s * 0.52f),
                        new PointF(box.X + s * 0.43f, box.Y + s * 0.72f),
                        new PointF(box.X + s * 0.78f, box.Y + s * 0.30f),
                    });
            }
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // Home header: UserData\launcher_banner.(png|jpg) if present, else the port's local-assets image
    // (fan art by Dreaminerryday, credited in the image); with no image, a drawn background (no official logos).
    public sealed class Banner : Control
    {
        private readonly Image _image;
        public string Subtitle = "";
        public float FocusY = 0.18f;  // 0 = arriba de la imagen, 0.5 = centro

        public Banner(string imagePath)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            // launcher_banner.png or .jpg.
            string jpg = System.IO.Path.ChangeExtension(imagePath, ".jpg");
            string path = System.IO.File.Exists(imagePath) ? imagePath : System.IO.File.Exists(jpg) ? jpg : null;
            try { if (path != null) using (var tmp = Image.FromFile(path)) _image = new Bitmap(tmp); } catch { _image = null; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = ClientRectangle;
            if (_image != null)
            {
                float scale = Math.Max((float)r.Width / _image.Width, (float)r.Height / _image.Height);
                float w = _image.Width * scale, h = _image.Height * scale;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                // Encuadre hacia arriba (FocusY) para que se vea la cara del personaje y no solo el centro.
                g.DrawImage(_image, (r.Width - w) / 2, (r.Height - h) * FocusY, w, h);
                // Degradado oscuro abajo a la izquierda para que el titulo se lea sobre la imagen.
                using (var shade = new LinearGradientBrush(new Rectangle(0, 0, r.Width, r.Height),
                           Color.FromArgb(210, 24, 14, 34), Color.FromArgb(0, 24, 14, 34), 0f))
                {
                    var blend = new ColorBlend
                    {
                        Positions = new[] { 0f, 0.35f, 0.6f, 1f },
                        Colors = new[] { Color.FromArgb(200, 24, 14, 34), Color.FromArgb(120, 24, 14, 34), Color.FromArgb(0, 24, 14, 34), Color.FromArgb(0, 24, 14, 34) },
                    };
                    shade.InterpolationColors = blend;
                    g.FillRectangle(shade, r);
                }
                using (var bottom = new LinearGradientBrush(new Rectangle(0, r.Height / 2, r.Width, r.Height / 2 + 1),
                           Color.FromArgb(0, 24, 14, 34), Color.FromArgb(230, 24, 14, 34), 90f))
                    g.FillRectangle(bottom, 0, r.Height / 2, r.Width, r.Height - r.Height / 2);
            }
            else
            {
                using (var bg = new LinearGradientBrush(r, Color.FromArgb(70, 26, 110), Color.FromArgb(20, 10, 32), 25f))
                    g.FillRectangle(bg, r);
                // Rayas en zigzag doradas (como un rayo), al fondo a la derecha.
                using (var pen = new Pen(Color.FromArgb(60, Theme.Gold), 18f))
                {
                    for (int k = 0; k < 6; k++)
                    {
                        int x0 = r.Width / 2 + k * 70;
                        var pts = new[]
                        {
                            new Point(x0, -10), new Point(x0 + 40, r.Height / 3),
                            new Point(x0 - 10, r.Height / 2), new Point(x0 + 50, r.Height + 10),
                        };
                        g.DrawLines(pen, pts);
                    }
                }
                using (var glow = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    glow.AddEllipse(r.Width - 360, -120, 520, 420);
                    using (var pb = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(110, Theme.Gold), SurroundColors = new[] { Color.FromArgb(0, Theme.Gold) } })
                        g.FillPath(pb, glow);
                }
            }
            // Titulo con contorno.
            DrawOutlined(g, "WARIO LAND", Theme.Title(34f), Theme.Gold, Dpi.S(34), r.Height - Dpi.S(132));
            DrawOutlined(g, "SHAKE IT!", Theme.Title(22f), Color.White, Dpi.S(38), r.Height - Dpi.S(80));
            TextRenderer.DrawText(g, Subtitle, Theme.UI(10f, FontStyle.Bold), new Point(Dpi.S(40), r.Height - Dpi.S(36)), Theme.Gold);
        }

        private static void DrawOutlined(Graphics g, string text, Font font, Color fill, int x, int y)
        {
            using (var path = new GraphicsPath())
            {
                path.AddString(text, font.FontFamily, (int)font.Style, g.DpiY * font.SizeInPoints / 72f, new Point(x, y), StringFormat.GenericDefault);
                using (var sp = (GraphicsPath)path.Clone())
                {
                    using (var m = new Matrix()) { m.Translate(4, 5); sp.Transform(m); }
                    using (var sb = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillPath(sb, sp);
                }
                using (var pen = new Pen(Color.FromArgb(40, 16, 60), 7f) { LineJoin = LineJoin.Round }) g.DrawPath(pen, path);
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            }
            font.Dispose();
        }
    }
}
