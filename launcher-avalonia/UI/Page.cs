using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WarioLauncher.Core;

namespace WarioLauncher.UI
{
    // Pagina de ajustes: titulo, secciones y filas "etiqueta | control" con una descripcion debajo.
    // (No hereda de ScrollViewer: contiene uno. Una subclase de un control con plantilla se queda sin la plantilla
    // del tema y entonces no desplaza.)
    public sealed class Page
    {
        private const double LabelWidth = 328;
        private const double MaxText = 820;
        private readonly StackPanel _panel = new StackPanel { Margin = new Thickness(32, 24, 32, 24), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Stretch };
        private readonly ScrollViewer _scroll;

        public Page()
        {
            _scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                // Barra siempre visible (el tema Fluent la esconde y solo aparece al pasar el raton por el borde).
                AllowAutoHide = false,
                Background = WarioTheme.Back,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Content = _panel,
            };
        }

        // Las paginas se devuelven como Control (el ScrollViewer).
        public static implicit operator Control(Page p) => p._scroll;

        public void Add(Control c) => _panel.Children.Add(c);

        public static TextBlock Label(string text, IBrush color = null, double size = 13.5, FontWeight weight = FontWeight.Normal) =>
            new TextBlock { Text = text, Foreground = color ?? WarioTheme.Text, FontSize = size, FontWeight = weight, TextWrapping = TextWrapping.Wrap, MaxWidth = MaxText };

        public void Header(string title, string subtitle)
        {
            Add(Label(title.ToUpperInvariant(), WarioTheme.Text, 26, FontWeight.Black));
            var sub = Label(subtitle, WarioTheme.Muted);
            sub.Margin = new Thickness(0, 6, 0, 10);
            Add(sub);
        }

        public void Section(string name)
        {
            var t = Label(name.ToUpperInvariant(), WarioTheme.Gold, 15, FontWeight.Bold);
            t.Margin = new Thickness(0, 18, 0, 6);
            Add(t);
            Add(new Border { Height = 1, Background = WarioTheme.FieldBorder, Margin = new Thickness(0, 0, 0, 10), MaxWidth = MaxText, HorizontalAlignment = HorizontalAlignment.Stretch });
        }

        private Grid RowGrid(string label, Control control)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(LabelWidth + ",*"), Margin = new Thickness(0, 3, 0, 3) };
            var l = Label(label);
            l.VerticalAlignment = VerticalAlignment.Center;
            l.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(l, 0);
            g.Children.Add(l);
            control.HorizontalAlignment = HorizontalAlignment.Left;
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        public void Row(string label, Control control, string desc = null)
        {
            Add(RowGrid(label, control));
            Desc(desc);
        }

        public CheckBox Check(string label, bool value, Action<bool> onChange, string desc = null)
        {
            var cb = new CheckBox { IsChecked = value };
            cb.PropertyChanged += (s, e) => { if (e.Property == ToggleButton.IsCheckedProperty) onChange(cb.IsChecked == true); };
            Add(RowGrid(label, cb));
            Desc(desc);
            return cb;
        }

        // Deslizador 0-100; el cambio se guarda 300 ms despues de soltarlo (y al salir de la pagina).
        public void Slider(string label, int value, Action<int> onChange)
        {
            var sl = new Slider { Minimum = 0, Maximum = 100, Value = value, Width = 300, SmallChange = 5, LargeChange = 10, IsSnapToTickEnabled = true, TickFrequency = 1 };
            var pct = Label(value + "%", WarioTheme.Gold);
            pct.VerticalAlignment = VerticalAlignment.Center;
            pct.Margin = new Thickness(12, 0, 0, 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Children = { sl, pct } };
            bool pending = false;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            timer.Tick += (s, e) => { timer.Stop(); pending = false; onChange((int)Math.Round(sl.Value)); };
            sl.PropertyChanged += (s, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                pct.Text = (int)Math.Round(sl.Value) + "%";
                pending = true;
                timer.Stop();
                timer.Start();
            };
            _scroll.DetachedFromVisualTree += (s, e) => { timer.Stop(); if (pending) { pending = false; onChange((int)Math.Round(sl.Value)); } };
            Add(RowGrid(label, row));
            Desc(null);
        }

        // Carpeta: cuadro de texto + Examinar + Abrir. pick: abre el selector y devuelve la ruta (o null).
        public void Folder(string label, string value, Action<string> onSet, Func<string, Task<string>> pick, string desc, bool allowEmpty = false)
        {
            var box = new TextBox { Text = value, Width = 300 };
            box.LostFocus += (s, e) => { string t = (box.Text ?? "").Trim(); if (allowEmpty || t.Length > 0) onSet(t); };
            var browse = new Button { Content = Loc.T("Examinar…", "Browse…"), Margin = new Thickness(8, 0, 0, 0) };
            browse.Click += async (s, e) =>
            {
                string path = await pick(box.Text);
                if (!string.IsNullOrEmpty(path)) { box.Text = path; onSet(path); }
            };
            var open = new Button { Content = Loc.T("Abrir", "Open"), Margin = new Thickness(8, 0, 0, 0) };
            open.Click += (s, e) => { if (System.IO.Directory.Exists(box.Text)) Platform.OpenFolder(box.Text); };
            Add(RowGrid(label, new StackPanel { Orientation = Orientation.Horizontal, Children = { box, browse, open } }));
            Desc(desc);
        }

        public TextBlock Info(string text, IBrush color)
        {
            var l = Label(text, color);
            l.Margin = new Thickness(0, 2, 0, 8);
            Add(l);
            return l;
        }

        public void Buttons(params (string text, Action action)[] buttons)
        {
            var row = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 10) };
            foreach (var b in buttons)
            {
                var btn = new Button { Content = b.text, Margin = new Thickness(0, 0, 10, 6) };
                var act = b.action;
                btn.Click += (s, e) => act();
                row.Children.Add(btn);
            }
            Add(row);
        }

        // Tabla de controles: Accion | Teclado (2) | Mando (2)
        private static Grid ControlsGrid() =>
            new Grid { ColumnDefinitions = new ColumnDefinitions("1.6*,*,*,1.3*,1.3*"), Margin = new Thickness(0, 2, 0, 2) };

        public void ControlsHeader(string action, string kb, string pad)
        {
            var g = ControlsGrid();
            void Put(string t, int col) { var l = Label(t, WarioTheme.Gold, 13, FontWeight.Bold); Grid.SetColumn(l, col); g.Children.Add(l); }
            Put(action, 0); Put(kb, 1); Put(pad, 3);
            g.Margin = new Thickness(0, 0, 0, 6);
            Add(g);
        }

        public void ControlsRow(string label, Control k1, Control k2, Control p1, Control p2)
        {
            var g = ControlsGrid();
            var l = Label(label);
            l.VerticalAlignment = VerticalAlignment.Center;
            l.Margin = new Thickness(0, 0, 8, 0);
            Grid.SetColumn(l, 0);
            g.Children.Add(l);
            int col = 1;
            foreach (var c in new[] { k1, k2, p1, p2 })
            {
                c.Margin = new Thickness(0, 0, 6, 0);
                c.Width = double.NaN;  // la columna decide el ancho
                c.MinWidth = 0;
                c.HorizontalAlignment = HorizontalAlignment.Stretch;
                Grid.SetColumn(c, col++);
                g.Children.Add(c);
            }
            Add(g);
        }

        private void Desc(string desc)
        {
            if (string.IsNullOrEmpty(desc)) { _panel.Children[^1].Margin = new Thickness(0, 3, 0, 6); return; }
            var l = Label(desc, WarioTheme.Muted, 12);
            l.Margin = new Thickness(0, 0, 0, 10);
            Add(l);
        }
    }
}
