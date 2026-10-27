using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace WarioLauncher
{
    // Escala por DPI (las paginas se crean en tiempo de ejecucion, asi que se escalan a mano).
    public static class Dpi
    {
        public static float Factor = 1f;
        public static int S(int v) => (int)Math.Round(v * Factor);
    }

    // Pagina de ajustes: titulo, secciones y filas "etiqueta | control" con una descripcion debajo.
    public sealed class Page : Panel
    {
        public int Top0 = 24;
        private int _y = -1;
        private readonly int _x = Dpi.S(32);
        private readonly int _ctrlX = Dpi.S(360);

        public Page()
        {
            // Solo barra vertical: nunca desplazamiento horizontal.
            AutoScroll = false;
            HorizontalScroll.Maximum = 0;
            HorizontalScroll.Enabled = false;
            HorizontalScroll.Visible = false;
            AutoScroll = true;
            BackColor = Theme.Back;
            Padding = new Padding(0, 0, 0, Dpi.S(24));
        }

        private int Y { get { if (_y < 0) _y = Dpi.S(Top0); return _y; } set { _y = value; } }

        private int Width0 => Math.Max(Dpi.S(600), ClientSize.Width - Dpi.S(64));

        public void Header(string title, string subtitle)
        {
            Add(new Label { Text = title.ToUpperInvariant(), Font = Theme.Title(20f), ForeColor = Theme.Text, AutoSize = true, Location = new Point(_x, Y) });
            Y += Dpi.S(42);
            Add(new Label { Text = subtitle, ForeColor = Theme.Muted, AutoSize = true, MaximumSize = new Size(Dpi.S(820), 0), Location = new Point(_x, Y) });
            Y += Dpi.S(30);
        }

        public void Section(string name)
        {
            Y += Dpi.S(14);
            Add(new Label { Text = name.ToUpperInvariant(), Font = Theme.UI(12f, FontStyle.Bold), ForeColor = Theme.Gold, AutoSize = true, Location = new Point(_x, Y) });
            Y += Dpi.S(28);
            Add(new Panel { BackColor = Theme.FieldBorder, Location = new Point(_x, Y), Size = new Size(Dpi.S(820), 1) });
            Y += Dpi.S(12);
        }

        public void Row(string label, Control control, string desc = null)
        {
            Add(new Label { Text = label, AutoSize = true, ForeColor = Theme.Text, Location = new Point(_x, Y + Dpi.S(4)) });
            control.Location = new Point(_ctrlX, Y);
            Add(control);
            Y += Math.Max(Dpi.S(30), control.Height + Dpi.S(6));
            Desc(desc);
        }

        public void Check(string label, bool value, Action<bool> onChange, string desc = null)
        {
            var cb = new DarkCheck { Checked = value };
            cb.CheckedChanged += (s, e) => onChange(cb.Checked);
            Add(new Label { Text = label, AutoSize = true, ForeColor = Theme.Text, Location = new Point(_x, Y + Dpi.S(2)) });
            cb.Location = new Point(_ctrlX, Y + Dpi.S(2));
            Add(cb);
            Y += Dpi.S(28);
            Desc(desc);
        }

        public void Slider(string label, int value, Action<int> onChange)
        {
            Add(new Label { Text = label, AutoSize = true, ForeColor = Theme.Text, Location = new Point(_x, Y + Dpi.S(6)) });
            var tb = new TrackBar
            {
                Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, Value = value, Width = Dpi.S(300),
                BackColor = Theme.Back, Location = new Point(_ctrlX, Y), SmallChange = 5, LargeChange = 10,
            };
            var pct = new Label { Text = value + "%", AutoSize = true, ForeColor = Theme.Gold, Location = new Point(_ctrlX + Dpi.S(310), Y + Dpi.S(6)) };
            var timer = new Timer { Interval = 300 };
            timer.Tick += (s, e) => { timer.Stop(); onChange(tb.Value); };
            tb.ValueChanged += (s, e) => { pct.Text = tb.Value + "%"; timer.Stop(); timer.Start(); };
            Disposed += (s, e) => { if (timer.Enabled) { timer.Stop(); onChange(tb.Value); } timer.Dispose(); };
            Add(tb);
            Add(pct);
            Y += Dpi.S(40);
        }

        public void Folder(string label, string value, Action<string> onSet, string desc, bool allowEmpty = false)
        {
            Add(new Label { Text = label, AutoSize = true, ForeColor = Theme.Text, Location = new Point(_x, Y + Dpi.S(5)) });
            var box = new TextBox
            {
                Text = value, Width = Dpi.S(280), BackColor = Theme.Field, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(_ctrlX, Y + Dpi.S(2)),
            };
            box.Leave += (s, e) => { if (allowEmpty || box.Text.Trim().Length > 0) onSet(box.Text.Trim()); };
            var browse = new FlatBtn(Loc.T("Examinar…", "Browse…")) { Location = new Point(_ctrlX + Dpi.S(290), Y) };
            browse.Click += (s, e) =>
            {
                using (var dlg = new FolderPicker { SelectedPath = box.Text, ShowNewFolderButton = true, Description = label })
                    if (dlg.ShowDialog(FindForm()) == DialogResult.OK) { box.Text = dlg.SelectedPath; onSet(dlg.SelectedPath); }
            };
            var open = new FlatBtn(Loc.T("Abrir", "Open")) { Location = new Point(_ctrlX + Dpi.S(392), Y) };
            open.Click += (s, e) =>
            {
                if (System.IO.Directory.Exists(box.Text))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + box.Text + "\"") { UseShellExecute = true });
            };
            Add(box); Add(browse); Add(open);
            Y += Dpi.S(36);
            Desc(desc);
        }

        // Control propio (p. ej. un cuadro de registro) a lo ancho de la pagina.
        public void Embed(Control c)
        {
            c.Location = new Point(_x, Y);
            Add(c);
            Y += c.Height + Dpi.S(12);
        }

        public Label Info(string text, Color color)
        {
            var l = new Label { Text = text, AutoSize = true, MaximumSize = new Size(Dpi.S(820), 0), ForeColor = color, Location = new Point(_x, Y) };
            Add(l);
            Y += l.PreferredHeight + Dpi.S(10);
            return l;
        }

        public void Buttons(params KeyValuePair<string, Action>[] buttons)
        {
            int x = _x;
            int h = 0;
            foreach (var b in buttons)
            {
                var btn = new FlatBtn(b.Key) { Location = new Point(x, Y) };
                var act = b.Value;
                btn.Click += (s, e) => act();
                Add(btn);
                x += btn.PreferredSize.Width + Dpi.S(10);
                h = Math.Max(h, btn.PreferredSize.Height);
            }
            Y += h + Dpi.S(12);
        }

        // Tabla de controles: Accion | Teclado (2) | Mando (2)
        private int ColKb => _x + Dpi.S(260);
        private int ColPad => _x + Dpi.S(520);

        public void ControlsHeader(string action, string kb, string pad)
        {
            var f = Theme.UI(10f, FontStyle.Bold);
            Add(new Label { Text = action, Font = f, ForeColor = Theme.Gold, AutoSize = true, Location = new Point(_x, Y) });
            Add(new Label { Text = kb, Font = f, ForeColor = Theme.Gold, AutoSize = true, Location = new Point(ColKb, Y) });
            Add(new Label { Text = pad, Font = f, ForeColor = Theme.Gold, AutoSize = true, Location = new Point(ColPad, Y) });
            Y += Dpi.S(28);
        }

        public void ControlsRow(string label, Control k1, Control k2, Control p1, Control p2)
        {
            Add(new Label { Text = label, AutoSize = true, MaximumSize = new Size(Dpi.S(250), 0), ForeColor = Theme.Text, Location = new Point(_x, Y + Dpi.S(4)) });
            k1.Width = k2.Width = Dpi.S(120);
            p1.Width = p2.Width = Dpi.S(170);
            k1.Location = new Point(ColKb, Y);
            k2.Location = new Point(ColKb + Dpi.S(126), Y);
            p1.Location = new Point(ColPad, Y);
            p2.Location = new Point(ColPad + Dpi.S(176), Y);
            Add(k1); Add(k2); Add(p1); Add(p2);
            Y += Dpi.S(32);
        }

        private void Desc(string desc)
        {
            if (string.IsNullOrEmpty(desc)) { Y += Dpi.S(4); return; }
            var l = new Label
            {
                Text = desc, AutoSize = true, MaximumSize = new Size(Dpi.S(820), 0), ForeColor = Theme.Muted,
                Font = Theme.UI(8.75f), Location = new Point(_x, Y - Dpi.S(2)),
            };
            Add(l);
            Y += l.PreferredHeight + Dpi.S(10);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (HorizontalScroll.Visible) { HorizontalScroll.Visible = false; HorizontalScroll.Enabled = false; }
        }

        private void Add(Control c)
        {
            // Con AutoScroll, las posiciones son relativas al contenido desplazado.
            c.Location = new Point(c.Left + AutoScrollPosition.X, c.Top + AutoScrollPosition.Y);
            Controls.Add(c);
        }
    }

    // Casilla para capturar una tecla: clic y pulsar; clic derecho la borra.
    public sealed class KeyCapture : Label
    {
        public string Value { get; private set; }
        public event Action Changed;
        private bool _waiting;

        public KeyCapture(string value)
        {
            Value = value ?? "";
            AutoSize = false;
            Height = Dpi.S(26);
            BorderStyle = BorderStyle.FixedSingle;
            BackColor = Theme.Field;
            ForeColor = Theme.Text;
            TextAlign = ContentAlignment.MiddleCenter;
            Cursor = Cursors.Hand;
            Font = Theme.UI(9f);
            Refresh0();
        }

        private void Refresh0()
        {
            Text = _waiting ? Loc.T("Pulsa una tecla…", "Press a key…") : (Value.Length > 0 ? Value : "—");
            BackColor = _waiting ? Theme.Purple : Theme.Field;
        }

        public static KeyCapture Active;

        // Devuelve un aviso si la tecla ya esta asignada a otra casilla (entonces no se asigna), o null.
        public Func<string, string> Validate;
        public static event Action<string> Rejected;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetKeyState(int vk);
        private static bool Down(int vk) => (GetKeyState(vk) & 0x8000) != 0;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Active != null && Active != this) { Active._waiting = false; Active.Refresh0(); }
            if (e.Button == MouseButtons.Right) { Active = null; _waiting = false; Value = ""; Refresh0(); Changed?.Invoke(); return; }
            _waiting = true;
            Active = this;
            Refresh0();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (Active == this) Active = null;
            base.OnHandleDestroyed(e);
        }

        public void Deliver(Keys keyData)
        {
            Keys k = keyData & Keys.KeyCode;
            // Shift/Ctrl/Alt llegan sin lado: se mira cual esta pulsada.
            if (k == Keys.ShiftKey) k = Down(0xA1) ? Keys.RShiftKey : Keys.LShiftKey;
            else if (k == Keys.ControlKey) k = Down(0xA3) ? Keys.RControlKey : Keys.LControlKey;
            else if (k == Keys.Menu) k = Down(0xA5) ? Keys.RMenu : Keys.LMenu;
            Active = null;
            _waiting = false;
            string name = Bindings.SdlKeyName(k);
            if (name != null && !Bindings.Reserved(name))
            {
                string conflict = Validate?.Invoke(name);
                if (conflict != null) Rejected?.Invoke(conflict);
                else if (name != Value) { Value = name; Changed?.Invoke(); }
            }
            Refresh0();
        }
    }
}
