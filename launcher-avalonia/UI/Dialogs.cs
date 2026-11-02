using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WarioLauncher.Core;

namespace WarioLauncher.UI
{
    // Mensajes, preguntas Si/No y ventanita de progreso (Avalonia no trae MessageBox).
    public static class Dialogs
    {
        public static Task Info(Window owner, string text) => Show(owner, text, false);

        public static Task<bool> Ask(Window owner, string text) => Show(owner, text, true);

        private static async Task<bool> Show(Window owner, string text, bool yesNo)
        {
            var w = NewWindow();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };
            if (yesNo)
            {
                var yes = new Button { Content = Loc.T("Sí", "Yes"), MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
                var no = new Button { Content = Loc.T("No", "No"), MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
                yes.Click += (s, e) => w.Close(true);
                no.Click += (s, e) => w.Close(false);
                buttons.Children.Add(yes);
                buttons.Children.Add(no);
            }
            else
            {
                var ok = new Button { Content = "OK", MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
                ok.Click += (s, e) => w.Close(true);
                buttons.Children.Add(ok);
            }
            w.Content = new StackPanel
            {
                Margin = new Thickness(22),
                Children =
                {
                    new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = WarioTheme.Text, FontSize = 13.5 },
                    buttons,
                },
            };
            if (owner == null || !owner.IsVisible) { w.Show(); return true; }
            return await w.ShowDialog<bool>(owner);
        }

        // Ejecuta work mostrando una ventanita con una barra de progreso; devuelve su resultado.
        public static async Task<T> Progress<T>(Window owner, string text, Func<Task<T>> work)
        {
            var w = NewWindow();
            w.Content = new StackPanel
            {
                Margin = new Thickness(22),
                Width = 420,
                Children =
                {
                    new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = WarioTheme.Text, FontSize = 13.5 },
                    new ProgressBar { IsIndeterminate = true, Margin = new Thickness(0, 16, 0, 0), Height = 8 },
                },
            };
            Task shown = owner != null && owner.IsVisible ? w.ShowDialog(owner) : Task.CompletedTask;
            try { return await work(); }
            finally { w.Close(); await shown; }
        }

        private static Window NewWindow() => new Window
        {
            Title = "WarioRecomp",
            SizeToContent = SizeToContent.WidthAndHeight,
            MaxWidth = 600,
            MinWidth = 320,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = WarioTheme.Back,
        };
    }
}
