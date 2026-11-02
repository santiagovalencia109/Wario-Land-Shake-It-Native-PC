using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace WarioLauncher.UI
{
    // Cabecera del Inicio: UserData/launcher_banner.(png|jpg) o la de local-assets (fan art de Dreaminerryday);
    // sin imagen, un fondo degradado (sin logos oficiales). Titulo y subtitulo abajo a la izquierda.
    public sealed class Banner : Grid
    {
        public Banner(string imagePath, string subtitle)
        {
            Height = 330;
            ClipToBounds = true;
            Bitmap bmp = null;
            try { if (imagePath != null && File.Exists(imagePath)) bmp = new Bitmap(imagePath); } catch { bmp = null; }
            if (bmp != null)
                Children.Add(new CoverImage(bmp));
            else
                Children.Add(new Border { Background = Gradient(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 1, RelativeUnit.Relative),
                    (Color.FromRgb(70, 26, 110), 0), (Color.FromRgb(20, 10, 32), 1)) });
            // Sombra a la izquierda y abajo para que el titulo se lea sobre la imagen.
            Children.Add(new Border { Background = Gradient(new RelativePoint(0, 0, RelativeUnit.Relative), new RelativePoint(1, 0, RelativeUnit.Relative),
                (Color.FromArgb(200, 24, 14, 34), 0), (Color.FromArgb(120, 24, 14, 34), 0.35), (Color.FromArgb(0, 24, 14, 34), 0.6)) });
            Children.Add(new Border { Background = Gradient(new RelativePoint(0, 0.5, RelativeUnit.Relative), new RelativePoint(0, 1, RelativeUnit.Relative),
                (Color.FromArgb(0, 24, 14, 34), 0), (Color.FromArgb(230, 24, 14, 34), 1)) });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(34, 0, 0, 18) };
            text.Children.Add(new TextBlock { Text = "WARIO LAND", FontSize = 44, FontWeight = FontWeight.Black, Foreground = WarioTheme.Gold });
            text.Children.Add(new TextBlock { Text = "SHAKE IT!", FontSize = 28, FontWeight = FontWeight.Black, Foreground = Brushes.White, Margin = new Thickness(4, -6, 0, 8) });
            text.Children.Add(new TextBlock { Text = subtitle, FontSize = 13, FontWeight = FontWeight.Bold, Foreground = WarioTheme.Gold, Margin = new Thickness(6, 0, 0, 0) });
            Children.Add(text);
        }

        // La imagen cubre todo el banner (como UniformToFill) pero encuadrada hacia arriba (FocusY), como el lanzador
        // anterior: se ve la cara del personaje y el credito de la artista de la esquina superior no se corta.
        private sealed class CoverImage : Control
        {
            private const double FocusY = 0.0;  // 0 = arriba de la imagen (no se corta el credito), 0.5 = centro
            private readonly Bitmap _bmp;

            public CoverImage(Bitmap bmp) { _bmp = bmp; ClipToBounds = true; }

            public override void Render(DrawingContext ctx)
            {
                Size img = _bmp.Size;
                Rect r = Bounds;
                if (img.Width <= 0 || img.Height <= 0 || r.Width <= 0 || r.Height <= 0) return;
                double scale = System.Math.Max(r.Width / img.Width, r.Height / img.Height);
                double w = img.Width * scale, h = img.Height * scale;
                ctx.DrawImage(_bmp, new Rect(0, 0, img.Width, img.Height), new Rect((r.Width - w) / 2, (r.Height - h) * FocusY, w, h));
            }
        }

        private static IBrush Gradient(RelativePoint start, RelativePoint end, params (Color color, double offset)[] stops)
        {
            var g = new LinearGradientBrush { StartPoint = start, EndPoint = end };
            var list = new GradientStops();
            foreach (var s in stops) list.Add(new GradientStop(s.color, s.offset));
            g.GradientStops = list;
            return g;
        }
    }
}
