using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace WarioLauncher.UI
{
    // Colores "estilo Wario": morado oscuro con amarillo/dorado (los mismos que el lanzador anterior).
    public static class WarioTheme
    {
        public static readonly Color BackC = Color.FromRgb(24, 14, 34);
        public static readonly Color SideC = Color.FromRgb(16, 9, 24);
        public static readonly Color SideHoverC = Color.FromRgb(38, 22, 54);
        public static readonly Color FieldC = Color.FromRgb(44, 28, 62);
        public static readonly Color FieldBorderC = Color.FromRgb(84, 56, 112);
        public static readonly Color GoldC = Color.FromRgb(246, 196, 38);
        public static readonly Color GoldDarkC = Color.FromRgb(196, 146, 18);
        public static readonly Color PurpleC = Color.FromRgb(118, 52, 170);
        public static readonly Color TextC = Color.FromRgb(240, 234, 248);
        public static readonly Color MutedC = Color.FromRgb(160, 146, 182);

        public static readonly IBrush Back = new SolidColorBrush(BackC);
        public static readonly IBrush Side = new SolidColorBrush(SideC);
        public static readonly IBrush SideHover = new SolidColorBrush(SideHoverC);
        public static readonly IBrush Field = new SolidColorBrush(FieldC);
        public static readonly IBrush FieldBorder = new SolidColorBrush(FieldBorderC);
        public static readonly IBrush Gold = new SolidColorBrush(GoldC);
        public static readonly IBrush Purple = new SolidColorBrush(PurpleC);
        public static readonly IBrush Text = new SolidColorBrush(TextC);
        public static readonly IBrush Muted = new SolidColorBrush(MutedC);
        public static readonly IBrush Bad = new SolidColorBrush(Color.FromRgb(240, 96, 96));
        public static readonly IBrush Good = new SolidColorBrush(Color.FromRgb(120, 214, 120));
        public static readonly IBrush DarkText = new SolidColorBrush(Color.FromRgb(40, 16, 60));
        public static readonly IBrush LogBack = new SolidColorBrush(Color.FromRgb(12, 8, 18));
        public static readonly IBrush Disabled = new SolidColorBrush(Color.FromRgb(90, 78, 108));

        public static readonly FontFamily Mono = new FontFamily("Consolas, DejaVu Sans Mono, Liberation Mono, monospace");

        // Recursos del tema Fluent (oscuro) con los colores del lanzador.
        public static void Apply(Application app)
        {
            app.RequestedThemeVariant = ThemeVariant.Dark;
            app.Styles.Add(new FluentTheme());
            var r = app.Resources;
            r["SystemAccentColor"] = GoldC;
            r["SystemAccentColorLight1"] = GoldC;
            r["SystemAccentColorDark1"] = GoldDarkC;
            r["ButtonBackground"] = Field;
            r["ButtonBackgroundPointerOver"] = SideHover;
            r["ButtonBackgroundPressed"] = Purple;
            r["ButtonForeground"] = Text;
            r["ButtonForegroundPointerOver"] = Text;
            r["ButtonForegroundPressed"] = Text;
            r["ButtonBorderBrush"] = FieldBorder;
            r["ButtonBorderBrushPointerOver"] = Gold;
            r["ComboBoxBackground"] = Field;
            r["ComboBoxBackgroundPointerOver"] = SideHover;
            r["ComboBoxBorderBrush"] = FieldBorder;
            r["ComboBoxDropDownBackground"] = Field;
            r["TextControlBackground"] = Field;
            r["TextControlBackgroundPointerOver"] = Field;
            r["TextControlBackgroundFocused"] = Field;
            r["TextControlBorderBrush"] = FieldBorder;
            r["TextControlForeground"] = Text;
            r["TextControlForegroundFocused"] = Text;
            r["TextControlForegroundPointerOver"] = Text;
        }
    }

    public sealed class App : Application
    {
        public override void Initialize() => WarioTheme.Apply(this);

        public override void OnFrameworkInitializationCompleted()
        {
            // Un error inesperado en la interfaz no cierra el lanzador: se anota en launcher-error.log (junto al
            // ejecutable) y se avisa.
            Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (s, e) =>
            {
                e.Handled = true;
                string log = System.IO.Path.Combine(System.AppContext.BaseDirectory, "launcher-error.log");
                try { System.IO.File.AppendAllText(log, System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + e.Exception + System.Environment.NewLine + System.Environment.NewLine); } catch { }
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d && d.MainWindow != null)
                    _ = Dialogs.Info(d.MainWindow, Core.Loc.T("Error inesperado (anotado en ", "Unexpected error (written to ") + log + "):\n\n" + e.Exception.Message);
            };
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();
            base.OnFrameworkInitializationCompleted();
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
}
