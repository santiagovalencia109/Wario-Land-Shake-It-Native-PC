using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using WarioLauncher.Core;

namespace WarioLauncher.UI
{
    public sealed partial class MainWindow : Window
    {
        private readonly Port _port = new Port(AppContext.BaseDirectory);
        private TomlFile Cfg => _port.Config;
        private LauncherSettings Ls => _port.Settings;

        private readonly Border _content = new Border();
        private readonly TextBlock _status = new TextBlock { Foreground = WarioTheme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly List<Button> _nav = new List<Button>();
        private Button _play;
        private int _page;
        private Process _running;

        private const int HomePage = 0, DisplayPage = 1, GraphicsPage = 2, AudioPage = 3, GamePage = 4, ControlsPage = 5, AdvancedPage = 6, InstallPage = 7, LogPage = 8;

        public MainWindow()
        {
            Loc.English = Ls.Get("lang", "en") == "en";
            Title = "Wario Land: Shake It! — WarioRecomp v" + _port.CodeVersion + " by Santiago109";
            Width = 1100;
            Height = 720;
            MinWidth = 900;
            MinHeight = 600;
            Background = WarioTheme.Back;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try
            {
                string ico = _port.Root == null ? null : Path.Combine(_port.Root, "local-assets", "wario.ico");
                if (ico != null && File.Exists(ico)) Icon = new WindowIcon(ico);
            }
            catch { }
            BuildShell();
            ShowPage(_port.IsInstalled() ? HomePage : InstallPage);
            Opened += (s, e) => BackgroundUpdateCheck();
            // Captura de teclas para la pagina Controles, antes de que las flechas muevan el foco.
            AddHandler(KeyDownEvent, (s, e) =>
            {
                if (KeyCapture.Active == null) return;
                KeyCapture.Active.Deliver(e.Key);
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
        }

        // ================================================================== estructura

        private void BuildShell()
        {
            _nav.Clear();
            string[] pages =
            {
                Loc.T("Inicio", "Home"), Loc.T("Pantalla", "Display"), Loc.T("Gráficos", "Graphics"),
                Loc.T("Audio", "Audio"), Loc.T("Juego", "Game"), Loc.T("Controles", "Controls"),
                Loc.T("Avanzado", "Advanced"), Loc.T("Instalación", "Install"), Loc.T("Registro", "Log"),
            };
            var navPanel = new StackPanel();
            for (int i = 0; i < pages.Length; i++)
            {
                int idx = i;
                var b = new Button
                {
                    Content = pages[i], Height = 42, FontSize = 14.5, Padding = new Thickness(18, 0, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center, Background = Brushes.Transparent,
                    BorderThickness = new Thickness(4, 0, 0, 0), CornerRadius = new CornerRadius(0),
                };
                b.Click += (s, e) => ShowPage(idx);
                navPanel.Children.Add(b);
                _nav.Add(b);
            }

            var sideBottom = new StackPanel { Spacing = 0 };
            // El codigo es mas nuevo que el programa compilado (una actualizacion): boton dorado a Instalacion.
            if (_port.Root != null && _port.NeedsRebuild)
            {
                var rebuild = new Button
                {
                    Content = "⚠  " + Loc.T("Recompilar ahora", "Rebuild now"), Height = 44, FontWeight = FontWeight.Bold,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center, Background = WarioTheme.Gold, Foreground = WarioTheme.DarkText, CornerRadius = new CornerRadius(0),
                };
                rebuild.Click += (s, e) => ShowPage(InstallPage);
                sideBottom.Children.Add(rebuild);
            }
            sideBottom.Children.Add(new TextBlock
            {
                Text = "v" + _port.CodeVersion + "  ·  " + Loc.T("lanzador ", "launcher ") + Updates.OwnVersion,
                Foreground = WarioTheme.Muted, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6),
            });
            // Idioma del lanzador, siempre disponible (tambien durante la instalacion).
            var lang = new Button
            {
                Content = Loc.English ? "🌐  English  →  Español" : "🌐  Español  →  English", Height = 34, FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(0),
            };
            lang.Click += (s, e) =>
            {
                Loc.English = !Loc.English;
                Ls.Set("lang", Loc.English ? "en" : "es");
                int page = _page;
                Dispatcher.UIThread.Post(() => { BuildShell(); ShowPage(page); });
            };
            sideBottom.Children.Add(lang);

            var logo = new TextBlock
            {
                Text = "WARIO LAND\nSHAKE IT!", FontSize = 20, FontWeight = FontWeight.Black, Foreground = WarioTheme.Gold,
                Margin = new Thickness(16, 20, 0, 20),
            };
            var side = new DockPanel { Width = 220, Background = WarioTheme.Side };
            DockPanel.SetDock(logo, Dock.Top);
            DockPanel.SetDock(sideBottom, Dock.Bottom);
            side.Children.Add(logo);
            side.Children.Add(sideBottom);
            side.Children.Add(navPanel);

            var hint = new TextBlock
            {
                Width = 220, Foreground = WarioTheme.Muted, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0),
                Text = Loc.T("En el juego: F10 abre el menú del port,\nF11 cambia ventana / pantalla completa.",
                             "In game: F10 opens the port menu,\nF11 toggles window / fullscreen."),
            };
            _play = new Button
            {
                Content = Loc.T("JUGAR", "PLAY"), Width = 230, FontSize = 22, FontWeight = FontWeight.Black,
                Background = WarioTheme.Gold, Foreground = WarioTheme.DarkText, CornerRadius = new CornerRadius(0),
                VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = !Building,
            };
            _play.Click += (s, e) => Play();
            var bottom = new DockPanel { Height = 70, Background = WarioTheme.Side };
            DockPanel.SetDock(hint, Dock.Left);
            DockPanel.SetDock(_play, Dock.Right);
            bottom.Children.Add(hint);
            bottom.Children.Add(_play);
            Detach(_status);  // se reutiliza al reconstruir (cambio de idioma): hay que quitarlo de la barra anterior
            bottom.Children.Add(_status);

            var root = new DockPanel();
            DockPanel.SetDock(bottom, Dock.Bottom);
            DockPanel.SetDock(side, Dock.Left);
            root.Children.Add(bottom);
            root.Children.Add(side);
            Detach(_content);
            root.Children.Add(_content);
            Content = root;
        }

        // Un control solo puede tener un padre: los que se reutilizan al reconstruir la ventana se sueltan antes.
        private static void Detach(Control c)
        {
            if (c.Parent is Panel panel) panel.Children.Remove(c);
            else if (c.Parent is Decorator deco && deco.Child == c) deco.Child = null;
        }

        private void ShowPage(int index)
        {
            // Sin juego instalado solo se puede usar la pagina Instalacion (y ver el registro).
            bool installed = _port.IsInstalled();
            if (!installed && index != InstallPage && index != LogPage) index = InstallPage;
            _page = index;
            KeyCapture.Active = null;
            for (int i = 0; i < _nav.Count; i++)
            {
                bool sel = i == index;
                _nav[i].IsEnabled = installed || i == InstallPage || i == LogPage;
                _nav[i].Foreground = sel ? WarioTheme.Gold : WarioTheme.Text;
                _nav[i].Background = sel ? WarioTheme.SideHover : Brushes.Transparent;
                _nav[i].BorderBrush = sel ? WarioTheme.Gold : Brushes.Transparent;
            }
            if (_play != null && !Building && (_running == null || _running.HasExited))
                _play.Content = installed ? Loc.T("JUGAR", "PLAY") : Loc.T("INSTALAR", "INSTALL");
            Cfg.Reload();
            Control page = index switch
            {
                HomePage => BuildHome(),
                DisplayPage => BuildDisplay(),
                GraphicsPage => BuildGraphics(),
                AudioPage => BuildAudio(),
                GamePage => BuildGame(),
                ControlsPage => BuildControls(),
                AdvancedPage => BuildAdvanced(),
                InstallPage => BuildInstall(),
                _ => BuildLog(),
            };
            _content.Child = page;
            UpdateStatus();
        }

        private void Reshow(int index) => Dispatcher.UIThread.Post(() => ShowPage(index));

        // Guarda una clave en Config.toml (recargando antes: el juego tambien escribe en el archivo).
        private void Write(Action<TomlFile> change)
        {
            try
            {
                Cfg.Reload();
                change(Cfg);
                Cfg.Save();
            }
            catch (Exception ex)
            {
                _ = Dialogs.Info(this, Loc.T("No se pudo guardar Config.toml:\n", "Could not save Config.toml:\n") + ex.Message);
            }
            UpdateStatus();
        }

        // ---- selectores de archivos ----

        private async Task<string> PickFolder(string title, string start)
        {
            var opts = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
            try { if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) opts.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(start); } catch { }
            var res = await StorageProvider.OpenFolderPickerAsync(opts);
            return res.Count > 0 ? res[0].TryGetLocalPath() : null;
        }

        private async Task<string> PickDiscImage()
        {
            var res = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.T("Elige la imagen del disco de Wario Land: Shake It!", "Pick the Wario Land: Shake It! disc image"),
                AllowMultiple = false,
                FileTypeFilter = new List<FilePickerFileType>
                {
                    new FilePickerFileType(Loc.T("Discos de Wii", "Wii discs")) { Patterns = DiscTool.Patterns },
                    FilePickerFileTypes.All,
                },
            });
            return res.Count > 0 ? res[0].TryGetLocalPath() : null;
        }

        // ================================================================== opciones compartidas

        private static readonly double[] Scales = { 1.0, 1.5, 2.25, 3.0 };  // 480p, 720p, 1080p, 1440p

        private static string ScaleLabel(double s)
        {
            if (s <= 0) return Loc.T("Automática (según la ventana)", "Automatic (follows the window)");
            int lines = (int)Math.Round(480 * s);
            return lines == 480 ? Loc.T("480p (original)", "480p (original)")
                : lines == 1080 ? "1080p (Full HD)" : lines == 1440 ? "1440p (2K)" : lines == 2160 ? "2160p (4K)" : lines + "p";
        }

        private static ComboBox Combo(Choice[] items, Choice selected, Action<object> onChange, double width = 300)
        {
            var c = new ComboBox { ItemsSource = items, SelectedItem = selected ?? items[0], Width = width };
            if (onChange != null) c.SelectionChanged += (s, e) => { if (c.SelectedItem is Choice ch) onChange(ch.Value); };
            return c;
        }

        private ComboBox ScaleCombo()
        {
            double cur = Cfg.GetDouble("video", "resolution_multiplier", 2.25);
            var items = Scales.Select(s => new Choice(ScaleLabel(s), s)).ToArray();
            // Valores fuera de la lista: se muestra el mas cercano.
            return Combo(items, items.OrderBy(c => Math.Abs((double)c.Value - cur)).First(),
                v => Write(t => t.SetDouble("video", "resolution_multiplier", (double)v)));
        }

        private ComboBox DisplayModeCombo()
        {
            string cur = Cfg.GetString("video", "display_mode", "borderless");
            var items = new[]
            {
                new Choice(Loc.T("Ventana", "Windowed"), "windowed"),
                new Choice(Loc.T("Pantalla completa sin bordes", "Borderless fullscreen"), "borderless"),
                new Choice(Loc.T("Pantalla completa exclusiva", "Exclusive fullscreen"), "exclusive"),
            };
            return Combo(items, items.FirstOrDefault(c => (string)c.Value == cur), v => Write(t => t.SetString("video", "display_mode", (string)v)));
        }

        private ComboBox AspectCombo()
        {
            int cur = Cfg.GetInt("video", "aspect_mode", Cfg.GetBool("video", "widescreen", true) ? 2 : 0);
            var items = new[]
            {
                new Choice(Loc.T("4:3 (original)", "4:3 (original)"), 0),
                new Choice("16:9", 1),
                new Choice(Loc.T("Llenar la ventana", "Fill the window"), 2),
                new Choice(Loc.T("Cinemático 2.39:1", "Cinematic 2.39:1"), 3),
            };
            return Combo(items, items.FirstOrDefault(c => (int)c.Value == cur), v => Write(t =>
            {
                t.SetInt("video", "aspect_mode", (int)v);
                t.SetBool("video", "widescreen", (int)v != 0);  // el juego tambien dibuja en panoramico
            }));
        }

        // La novedad del port: el juego original nunca dejo que los niveles llenaran una pantalla 16:9.
        private void FullWidescreenCheck(Page p)
        {
            p.Check(Loc.T("★ 16:9 real en los niveles (experimental)", "★ True 16:9 in levels (experimental)"), Cfg.GetBool("video", "full_widescreen", false),
                v => Write(t => t.SetBool("video", "full_widescreen", v)),
                Loc.T("Novedad del port: quita los paneles laterales y el nivel llena toda la pantalla, algo que el juego original nunca permitió. Desactivada: los paneles originales. Solo con 16:9, Llenar o Cinemático; se aplica al iniciar el juego.",
                      "New in this port: removes the side panels so the level fills the whole screen, something the original game never allowed. Off: the original panels. Only with 16:9, Fill or Cinematic; applies when the game starts."));
        }

        private ComboBox IntroCombo()
        {
            var items = new[]
            {
                new Choice(Loc.T("Completa (se salta con A, 2 o +)", "Full (skip with A, 2 or +)"), false),
                new Choice(Loc.T("Corta (1 cuadro)", "Short (1 frame)"), true),
            };
            return Combo(items, items[File.Exists(_port.ShortIntroFlag) ? 1 : 0], v =>
            {
                try
                {
                    if ((bool)v) File.WriteAllText(_port.ShortIntroFlag, "");
                    else if (File.Exists(_port.ShortIntroFlag)) File.Delete(_port.ShortIntroFlag);
                }
                catch { }
                UpdateStatus();
            });
        }

        // Cabecera: UserData/launcher_banner.(png|jpg), si no la de local-assets del port.
        private string BannerPath()
        {
            foreach (string dir in new[] { _port.UserData, _port.Root == null ? null : Path.Combine(_port.Root, "local-assets") })
            {
                if (dir == null) continue;
                foreach (string ext in new[] { ".png", ".jpg" })
                {
                    string f = Path.Combine(dir, "launcher_banner" + ext);
                    if (File.Exists(f)) return f;
                }
            }
            return null;
        }

        // ================================================================== paginas

        private Control BuildHome()
        {
            var p = new Page();
            var banner = new Banner(BannerPath(), "RWLE01 · " + Loc.T("recompilación nativa para PC", "native PC recompilation") + "  ·  by Santiago109  ·  " + Loc.T("en pruebas", "in testing"))
            {
                Margin = new Thickness(-32, -24, -32, 12),
            };
            p.Add(banner);
            p.Section(Loc.T("Ajustes rápidos", "Quick settings"));
            p.Row(Loc.T("Modo de pantalla", "Display mode"), DisplayModeCombo());
            p.Row(Loc.T("Resolución interna", "Render resolution"), ScaleCombo());
            p.Row(Loc.T("Relación de aspecto", "Aspect ratio"), AspectCombo());
            p.Row(Loc.T("Intro", "Intro"), IntroCombo());
            FullWidescreenCheck(p);
            p.Section(Loc.T("Estado", "Status"));
            string dvd = Cfg.GetString("paths", "dvd_root", "");
            bool okGame = Port.GameFolderOk(dvd, out string why);
            p.Info(okGame ? "✔ " + Loc.T("Juego encontrado: ", "Game found: ") + dvd : "✖ " + why, okGame ? WarioTheme.Good : WarioTheme.Bad);
            p.Info(_port.GameBuilt ? "✔ " + Platform.GameExeName : "✖ " + Loc.T("Todavía no está compilado: ve a Instalación (o pulsa JUGAR).",
                "Not built yet: go to Install (or press PLAY)."), _port.GameBuilt ? WarioTheme.Good : WarioTheme.Bad);
            return p;
        }

        private Control BuildDisplay()
        {
            var p = new Page();
            p.Header(Loc.T("Pantalla", "Display"), Loc.T("Cómo se ve el juego en tu pantalla.", "How the game appears on your screen."));
            p.Section(Loc.T("Ventana", "Window"));
            p.Row(Loc.T("Modo de pantalla", "Display mode"), DisplayModeCombo(),
                Loc.T("Sin bordes cubre la pantalla sin cambiar la resolución del escritorio. F11 cambia en el juego.",
                      "Borderless covers the screen without changing the display mode. F11 switches in game."));
            int w = Cfg.GetInt("video", "window_width", 854), h = Cfg.GetInt("video", "window_height", 480);
            var sizes = new List<Choice>();
            foreach (var s in new[] { "640x480", "854x480", "1280x720", "1600x900", "1920x1080", "2560x1440", "3840x2160" })
                sizes.Add(new Choice(s.Replace("x", " × "), s));
            string cur = w + "x" + h;
            if (!sizes.Any(c => (string)c.Value == cur)) sizes.Insert(0, new Choice(cur.Replace("x", " × ") + Loc.T(" (actual)", " (current)"), cur));
            p.Row(Loc.T("Tamaño de la ventana", "Window size"), Combo(sizes.ToArray(), sizes.First(c => (string)c.Value == cur), v =>
            {
                string[] parts = ((string)v).Split('x');
                Write(t => { t.SetInt("video", "window_width", int.Parse(parts[0])); t.SetInt("video", "window_height", int.Parse(parts[1])); });
            }), Loc.T("Al abrir en ventana. Si cambias el tamaño con el ratón, el juego lo recuerda.",
                      "When opening windowed. If you resize it with the mouse, the game remembers it."));
            p.Row(Loc.T("Relación de aspecto", "Aspect ratio"), AspectCombo(),
                Loc.T("16:9 y Llenar hacen que el juego dibuje en panorámico (más escenario a los lados).",
                      "16:9 and Fill make the game draw widescreen (more of the level at the sides)."));
            FullWidescreenCheck(p);
            p.Check(Loc.T("Sincronización vertical (VSync)", "Vertical sync (VSync)"), Cfg.GetBool("video", "vsync", true),
                v => Write(t => t.SetBool("video", "vsync", v)),
                Loc.T("Desactivada puede haber cortes en la imagen (tearing).", "Off can show tearing."));
            p.Section(Loc.T("Superposición", "Overlay"));
            p.Check(Loc.T("Mostrar FPS", "Show FPS"), Cfg.GetBool("video", "show_fps", false), v => Write(t => t.SetBool("video", "show_fps", v)));
            p.Check(Loc.T("Gráfica de tiempo por cuadro", "Frame time graph"), Cfg.GetBool("video", "frame_graph", false), v => Write(t => t.SetBool("video", "frame_graph", v)),
                Loc.T("Útil para ver tirones.", "Useful to spot stutter."));
            return p;
        }

        private Control BuildGraphics()
        {
            var p = new Page();
            p.Header(Loc.T("Gráficos", "Graphics"), Loc.T("Resolución y renderizador.", "Resolution and renderer."));
            p.Section(Loc.T("Resolución", "Resolution"));
            p.Row(Loc.T("Resolución interna", "Render resolution"), ScaleCombo(),
                Loc.T("Se nota más en pantalla completa o en una ventana grande.", "Shows most in fullscreen or a large window."));
            var fi = new[] { new Choice(Loc.T("Desactivada", "Off"), 0), new Choice("120 FPS", 120), new Choice("180 FPS", 180), new Choice("240 FPS", 240) };
            int fcur = Cfg.GetInt("video", "frame_interpolation_fps", 0);
            p.Row(Loc.T("Interpolación de cuadros", "Frame interpolation"),
                Combo(fi, fi.FirstOrDefault(c => (int)c.Value == fcur) ?? fi[0], v => Write(t => t.SetInt("video", "frame_interpolation_fps", (int)v))),
                Loc.T("Experimental. El juego sigue a 60; se añaden cuadros intermedios. Va mejor si el monitor funciona a esa frecuencia.",
                      "Experimental. The game still runs at 60; frames in between are added. Best when the monitor runs at that rate."));
            p.Section(Loc.T("Renderizador", "Renderer"));
            var apis = Platform.IsWindows
                ? new[] { new Choice(Loc.T("Automático", "Automatic"), "auto"), new Choice("Direct3D 12", "d3d12"), new Choice("Vulkan", "vulkan") }
                : new[] { new Choice(Loc.T("Automático", "Automatic"), "auto"), new Choice("Vulkan", "vulkan") };
            string api = Cfg.GetString("video", "graphics_api", "auto");
            p.Row(Loc.T("API gráfica", "Graphics API"), Combo(apis, apis.FirstOrDefault(c => (string)c.Value == api) ?? apis[0],
                v => Write(t => t.SetString("video", "graphics_api", (string)v))));
            var compat = new[] { new Choice(Loc.T("Automático", "Automatic"), 0), new Choice(Loc.T("Activado", "On"), 1), new Choice(Loc.T("Desactivado", "Off"), 2) };
            int cc = Cfg.GetInt("video", "gpu_compat", 0);
            p.Row(Loc.T("Compatibilidad de GPU", "GPU compatibility"), Combo(compat, compat.FirstOrDefault(c => (int)c.Value == cc) ?? compat[0],
                v => Write(t => t.SetInt("video", "gpu_compat", (int)v))), Loc.T("Solo si ves gráficos rotos.", "Only if you see broken graphics."));
            p.Check(Loc.T("No esperar a los shaders nuevos", "Don't wait for new shaders"), Cfg.GetBool("video", "skip_unready_pipelines", true),
                v => Write(t => t.SetBool("video", "skip_unready_pipelines", v)), Loc.T("Menos tirones.", "Fewer stutters."));
            p.Check(Loc.T("Filtro de copia original", "Original copy filter"), !Cfg.GetBool("video", "disable_copy_filter", true),
                v => Write(t => t.SetBool("video", "disable_copy_filter", !v)), Loc.T("Imagen más suave, como en la Wii.", "Softer picture, like on the Wii."));
            p.Section(Loc.T("Texturas", "Textures"));
            p.Check(Loc.T("Usar texturas de reemplazo", "Use replacement textures"), Cfg.GetBool("video", "texture_replacements", true),
                v => Write(t => t.SetBool("video", "texture_replacements", v)));
            p.Check(Loc.T("Volcar texturas del juego", "Dump game textures"), Cfg.GetBool("video", "texture_dumps", false),
                v => Write(t => t.SetBool("video", "texture_dumps", v)));
            p.Buttons(
                (Loc.T("Carpeta de texturas", "Textures folder"), () => Platform.OpenFolder(Path.Combine(_port.UserData, "texture_replacements"))),
                (Loc.T("Carpeta de volcados", "Dumps folder"), () => Platform.OpenFolder(Path.Combine(_port.UserData, "Cache", "texture_dumps"))));
            return p;
        }

        private Control BuildAudio()
        {
            var p = new Page();
            p.Header(Loc.T("Audio", "Audio"), Loc.T("Volumen y mezcla.", "Volume and mixing."));
            p.Section(Loc.T("Volumen", "Volume"));
            int vol = (int)Math.Round(Cfg.GetDouble("audio", "volume", 1.0) * 100);
            p.Slider(Loc.T("Volumen general", "Master volume"), Math.Clamp(vol, 0, 100), v => Write(t => t.SetDouble("audio", "volume", v / 100.0)));
            p.Check(Loc.T("Silenciar", "Mute"), Cfg.GetBool("audio", "muted", false), v => Write(t => t.SetBool("audio", "muted", v)));
            p.Section(Loc.T("Rendimiento", "Performance"));
            p.Check(Loc.T("Mezclar el audio en su propio hilo", "Mix audio on its own thread"), Cfg.GetBool("audio", "mix_worker", true),
                v => Write(t => t.SetBool("audio", "mix_worker", v)),
                Loc.T("Aprovecha otro núcleo del procesador; apágalo solo si el sonido falla.", "Uses another CPU core; turn off only if sound breaks."));
            return p;
        }

        private Control BuildGame()
        {
            var p = new Page();
            p.Header(Loc.T("Juego", "Game"), Loc.T("Tu copia del juego, partidas e idioma.", "Your game dump, saves and language."));
            p.Section(Loc.T("Carpetas", "Folders"));
            string dvd = Cfg.GetString("paths", "dvd_root", "");
            TextBlock dvdState = null;
            p.Folder(Loc.T("Carpeta del juego", "Game folder"), dvd, path =>
            {
                path = Port.NormalizeGameFolder(path);
                Write(t => t.SetString("paths", "dvd_root", path));
                bool ok = Port.GameFolderOk(path, out string why);
                dvdState.Text = ok ? "✔ " + Loc.T("Juego correcto (RWLE01).", "Game OK (RWLE01).") : "✖ " + why;
                dvdState.Foreground = ok ? WarioTheme.Good : WarioTheme.Bad;
            }, start => PickFolder(Loc.T("Carpeta del juego extraído (la que tiene sys y files)", "Extracted game folder (the one with sys and files)"), start), null);
            bool okNow = Port.GameFolderOk(dvd, out string whyNow);
            dvdState = p.Info(okNow ? "✔ " + Loc.T("Juego correcto (RWLE01).", "Game OK (RWLE01).") : "✖ " + whyNow, okNow ? WarioTheme.Good : WarioTheme.Bad);

            string nand = Cfg.GetString("paths", "nand_root", "");
            p.Folder(Loc.T("Carpeta de partidas", "Saves folder"), nand, path =>
            {
                Write(t => { if (string.IsNullOrWhiteSpace(path)) t.Remove("paths", "nand_root"); else t.SetString("paths", "nand_root", path); });
            }, start => PickFolder(Loc.T("Carpeta de partidas", "Saves folder"), start), Loc.T("Vacío = la carpeta del port.", "Empty = the port folder."), true);

            p.Buttons((Loc.T("Importar disco (.iso / .ciso / .wbfs / .rvz)…", "Import disc (.iso / .ciso / .wbfs / .rvz)…"), async () =>
            {
                if (await ImportDiscInteractive()) Reshow(GamePage);
            }));

            p.Section(Loc.T("Idioma", "Language"));
            var langs = new[] { new Choice("English", "en"), new Choice("Español", "es"), new Choice("Français", "fr") };
            string lang = Cfg.GetString("game", "language", "en");
            p.Row(Loc.T("Idioma del juego", "Game language"), Combo(langs, langs.FirstOrDefault(c => (string)c.Value == lang) ?? langs[0],
                v => Write(t => t.SetString("game", "language", (string)v))));

            p.Section(Loc.T("Intro", "Intro"));
            p.Row(Loc.T("Video de introducción", "Opening movie"), IntroCombo());

            p.Section(Loc.T("Copias de seguridad de partidas", "Save backups"));
            p.Check(Loc.T("Copiar las partidas antes de cada inicio", "Back up saves before every start"), Ls.GetBool("backup_on_start", true),
                v => Ls.SetBool("backup_on_start", v), Loc.T("Se guardan las últimas 10.", "The last 10 are kept."));
            var list = _port.ListBackups().Select(d => new Choice(Port.BackupLabel(d), d)).ToList();
            if (list.Count == 0) list.Add(new Choice(Loc.T("(no hay copias todavía)", "(no backups yet)"), null));
            var bcombo = Combo(list.ToArray(), list[0], null);
            p.Row(Loc.T("Copias", "Backups"), bcombo);
            p.Buttons(
                (Loc.T("Restaurar", "Restore"), async () =>
                {
                    if (!(bcombo.SelectedItem is Choice c) || c.Value == null) return;
                    if (!await Dialogs.Ask(this, Loc.T("¿Restaurar esta copia? Antes se hace una copia de las partidas actuales.",
                            "Restore this backup? The current saves are backed up first."))) return;
                    try { _port.RestoreBackup((string)c.Value); await Dialogs.Info(this, Loc.T("Partidas restauradas.", "Saves restored.")); }
                    catch (Exception ex) { await Dialogs.Info(this, ex.Message); }
                    Reshow(GamePage);
                }),
                (Loc.T("Hacer copia ahora", "Back up now"), () => { _port.BackupSaves(); Reshow(GamePage); }),
                (Loc.T("Abrir partidas", "Open saves"), () => Platform.OpenFolder(_port.SaveDir())));
            return p;
        }

        private Control BuildAdvanced()
        {
            var p = new Page();
            p.Header(Loc.T("Avanzado", "Advanced"), Loc.T("Opciones del lanzador, archivos y diagnóstico.", "Launcher options, files and diagnostics."));
            p.Section(Loc.T("Lanzador", "Launcher"));
            var langs = new[] { new Choice("Español", "es"), new Choice("English", "en") };
            p.Row(Loc.T("Idioma del lanzador", "Launcher language"), Combo(langs, langs[Loc.English ? 1 : 0], v =>
            {
                Ls.Set("lang", (string)v);
                Loc.English = (string)v == "en";
                Dispatcher.UIThread.Post(() => { BuildShell(); ShowPage(AdvancedPage); });
            }));
            p.Check(Loc.T("Cerrar el lanzador al iniciar el juego", "Close the launcher when the game starts"), Ls.GetBool("close_on_start", false),
                v => Ls.SetBool("close_on_start", v));
            p.Check(Loc.T("Buscar actualizaciones al abrir", "Check for updates on start"), Ls.GetBool("auto_update", true),
                v => Ls.SetBool("auto_update", v));
            p.Buttons((Platform.IsWindows ? Loc.T("Acceso directo en el escritorio", "Desktop shortcut") : Loc.T("Añadir al menú de aplicaciones", "Add to the applications menu"), async () =>
            {
                string icon = _port.Root == null ? "" : Path.Combine(_port.Root, "local-assets", Platform.IsWindows ? "wario.ico" : "wario.png");
                if (Platform.IsWindows && !File.Exists(icon)) icon = Updates.OwnExe;
                string err = Platform.CreateShortcut(Updates.OwnExe, _port.PlayDir, icon);
                await Dialogs.Info(this, err == null ? Loc.T("Acceso directo creado.", "Shortcut created.") : err);
            }));
            p.Section(Loc.T("Archivos", "Files"));
            p.Buttons(
                (Loc.T("Carpeta del port", "Port folder"), () => Platform.OpenFolder(_port.PlayDir)),
                ("Config.toml", () => Platform.OpenFile(_port.ConfigPath)),
                (Loc.T("Carpeta de registros", "Logs folder"), () => Platform.OpenFolder(_port.LogsDir)));
            p.Buttons((Loc.T("Borrar la caché de shaders", "Clear the shader cache"), async () =>
            {
                string cache = Path.Combine(_port.UserData, "Cache");
                if (!await Dialogs.Ask(this, Loc.T("¿Borrar la caché de shaders? La próxima partida tendrá algunos tirones mientras se vuelve a crear.",
                        "Clear the shader cache? The next session will stutter a bit while it is rebuilt."))) return;
                try
                {
                    int n = 0;
                    if (Directory.Exists(cache))
                        foreach (string f in Directory.GetFiles(cache, "*.db*", SearchOption.TopDirectoryOnly)) { File.Delete(f); n++; }
                    await Dialogs.Info(this, Loc.T("Archivos borrados: ", "Files deleted: ") + n);
                }
                catch (Exception ex) { await Dialogs.Info(this, ex.Message); }
            }));
            p.Info(Loc.T("Si el juego solo muestra una pantalla negra después de actualizar, borrar la caché suele ayudar.",
                         "If the game only shows a black screen after an update, clearing the cache usually helps."), WarioTheme.Muted);
            p.Section(Loc.T("Créditos", "Credits"));
            p.Info("WarioRecomp — " + Loc.T("port nativo para PC de Wario Land: Shake It! (RWLE01), por ", "native PC port of Wario Land: Shake It! (RWLE01), by ") + "Santiago109", WarioTheme.Gold);
            p.Info(Loc.T("Versión en pruebas: puede haber fallos, cuelgues o partes que no funcionen.", "Testing build: expect bugs, crashes or parts that don't work yet."), WarioTheme.Muted);
            foreach (string line in new[]
            {
                "WiiCompiled — patchzyy " + Loc.T("y colaboradores: recompilador y runtime (GPL-3.0)", "and contributors: recompiler and runtime (GPL-3.0)"),
                "Aurora — Luke Street: " + Loc.T("gráficos GX de Wii sobre WebGPU (MIT)", "Wii GX graphics on WebGPU (MIT)"),
                "Dawn, SDL3, LLVM, CMake, Ninja, .NET, Avalonia — " + Loc.T("compilación, ejecución y este lanzador", "build, runtime and this launcher"),
                "nod / nodtool — Luke Street: " + Loc.T("lectura de imágenes de disco (MIT)", "disc image reading (MIT)"),
                "Dolphin Emulator — " + Loc.T("base de firmas de funciones del SDK (GPL-2.0+)", "SDK function signature database (GPL-2.0+)"),
                "Dreaminerryday — " + Loc.T("fan art de Wario del encabezado y el icono", "Wario fan art for the header and the icon"),
                "Capstone — " + Loc.T("desensamblador para las herramientas de análisis (BSD)", "disassembler for the analysis tools (BSD)"),
                Loc.T("Proyecto de fans no oficial; no incluye el juego.", "Unofficial fan project; the game is not included."),
            })
                p.Info("•  " + line, WarioTheme.Muted);
            p.Buttons((Loc.T("Licencias de terceros", "Third-party notices"), () =>
            {
                string notices = _port.Root == null ? null : Path.Combine(_port.Root, "fw", "THIRD-PARTY-NOTICES.md");
                if (notices != null && File.Exists(notices)) Platform.OpenFile(notices);
            }));
            return p;
        }

        private Control BuildLog()
        {
            var box = new TextBox
            {
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = WarioTheme.Mono, FontSize = 12,
                Background = WarioTheme.LogBack, Foreground = WarioTheme.Text, BorderThickness = new Thickness(0),
            };
            var source = new TextBlock { Foreground = WarioTheme.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            void Load()
            {
                string file = _port.LatestLog();
                source.Text = file ?? Loc.T("Todavía no hay registros: se crean al jugar.", "No logs yet: they are made when you play.");
                box.Text = file == null ? "" : Port.ReadShared(file, 400000);
                box.CaretIndex = box.Text?.Length ?? 0;
            }
            var refresh = new Button { Content = Loc.T("Actualizar", "Refresh"), Margin = new Thickness(0, 0, 8, 0) };
            var copy = new Button { Content = Loc.T("Copiar", "Copy"), Margin = new Thickness(0, 0, 8, 0) };
            var open = new Button { Content = Loc.T("Abrir carpeta", "Open folder") };
            refresh.Click += (s, e) => Load();
            copy.Click += async (s, e) => { if (!string.IsNullOrEmpty(box.Text) && Clipboard != null) await Clipboard.SetTextAsync(box.Text); };
            open.Click += (s, e) => Platform.OpenFolder(_port.LogsDir);
            var title = new TextBlock { Text = Loc.T("Registro", "Log").ToUpperInvariant(), FontSize = 24, FontWeight = FontWeight.Black, Foreground = WarioTheme.Text, Margin = new Thickness(0, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center };
            var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12), Children = { title, refresh, copy, open, source } };
            var host = new DockPanel { Margin = new Thickness(24, 16, 24, 16) };
            DockPanel.SetDock(top, Dock.Top);
            host.Children.Add(top);
            host.Children.Add(box);
            Load();
            return host;
        }

        // ================================================================== acciones

        private async void Play()
        {
            if (_running != null && !_running.HasExited) { await Dialogs.Info(this, Loc.T("El juego ya está abierto.", "The game is already running.")); return; }
            if (_port.Root != null && !_port.IsInstalled()) { if (_page != InstallPage) ShowPage(InstallPage); InstallFlow(); return; }
            if (!_port.GameBuilt)
            {
                await Dialogs.Info(this, Loc.T("No se encuentra " + Platform.GameExeName + " junto al lanzador.", Platform.GameExeName + " was not found next to the launcher."));
                return;
            }
            Cfg.Reload();
            if (!Port.GameFolderOk(Cfg.GetString("paths", "dvd_root", ""), out string why))
            {
                await Dialogs.Info(this, why + "\n\n" + Loc.T("Elige la carpeta del juego en la página Juego.", "Pick the game folder on the Game page."));
                ShowPage(GamePage);
                return;
            }
            try { File.Delete(Path.Combine(_port.PlayDir, "autoexit.txt")); } catch { }
            if (Ls.GetBool("backup_on_start", true)) _port.BackupSaves();
            try
            {
                _running = Process.Start(new ProcessStartInfo(_port.GameExe) { WorkingDirectory = _port.PlayDir, UseShellExecute = false });
                if (_running == null) return;
                _running.EnableRaisingEvents = true;
                _running.Exited += (s, e) => Dispatcher.UIThread.Post(() =>
                {
                    _play.IsEnabled = true;
                    _play.Content = Loc.T("JUGAR", "PLAY");
                    if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                    if (_page == LogPage) ShowPage(LogPage);
                });
                if (Ls.GetBool("close_on_start", false)) { Close(); return; }
                _play.IsEnabled = false;
                _play.Content = Loc.T("EN JUEGO…", "PLAYING…");
                WindowState = WindowState.Minimized;
            }
            catch (Exception ex) { await Dialogs.Info(this, ex.Message); }
        }

        private void UpdateStatus()
        {
            if (_port.Root != null && !_port.GameBuilt) { _status.Text = ""; return; }
            try
            {
                string mode = Cfg.GetString("video", "display_mode", "borderless");
                string modeText = mode == "windowed" ? Loc.T("Ventana", "Windowed") : mode == "exclusive" ? Loc.T("Pantalla completa", "Fullscreen") : Loc.T("Sin bordes", "Borderless");
                double s = Cfg.GetDouble("video", "resolution_multiplier", 2.25);
                string res = s <= 0 ? Loc.T("resolución automática", "auto resolution") : (int)Math.Round(480 * s) + "p";
                int asp = Cfg.GetInt("video", "aspect_mode", 2);
                string aspText = asp == 0 ? "4:3" : asp == 1 ? "16:9" : asp == 3 ? "2.39:1" : Loc.T("llenar", "fill");
                string intro = File.Exists(_port.ShortIntroFlag) ? Loc.T("intro corta", "short intro") : Loc.T("intro saltable", "skippable intro");
                _status.Text = res + "  ·  " + aspText + "  ·  " + modeText + "  ·  " + intro;
            }
            catch { _status.Text = ""; }
        }
    }
}
