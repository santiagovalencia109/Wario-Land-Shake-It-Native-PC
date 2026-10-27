using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WarioLauncher
{
    public sealed partial class MainForm : Form
    {
        // ---- rutas ----
        // El lanzador puede estar en la raiz del port (junto a tools\) o en Play\, junto a WarioRecomp.exe.
        private static readonly string BaseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        private readonly string _root = FindRoot(BaseDir);
        private string _playDir => _root != null ? Path.Combine(_root, "Play") : BaseDir;
        private string GameExe => Path.Combine(_playDir, "WarioRecomp.exe");
        private string UserData => Path.Combine(_playDir, "UserData");
        private string ConfigPath => Path.Combine(UserData, "Config.toml");
        private string ShortIntroFlag => Path.Combine(_playDir, "short_intro.txt");
        private string BackupDir => Path.Combine(UserData, "SaveBackups");
        private const string SaveSubPath = @"title\00010000\52574c45\data";

        private readonly TomlFile _cfg;
        private readonly LauncherSettings _ls;

        private readonly Panel _side = new Panel();
        private readonly Panel _content = new Panel();
        private readonly Panel _bottom = new Panel();
        private readonly Label _status = new Label();
        private FlatBtn _play;
        private readonly List<SideButton> _nav = new List<SideButton>();
        private int _page;
        private Process _running;

        public MainForm()
        {
            _cfg = new TomlFile(ConfigPath);
            _ls = new LauncherSettings(Path.Combine(UserData, "launcher.ini"));
            Loc.English = _ls.Get("lang", "en") == "en";

            Text = "Wario Land: Shake It! — WarioRecomp v" + CodeVersion + " by Santiago109";
            BackColor = Theme.Back;
            ForeColor = Theme.Text;
            Font = Theme.UI();
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Dpi.S(1100), Dpi.S(720));
            MinimumSize = new Size(Dpi.S(900), Dpi.S(600));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BuildShell();
            ShowPage(IsInstalled() ? 0 : 7);
            Shown += (s, e) => BackgroundUpdateCheck();
        }

        // ================================================================== estructura

        private void BuildShell()
        {
            Controls.Clear();
            _nav.Clear();

            _side.Controls.Clear();
            _side.Dock = DockStyle.Left;
            _side.Width = Dpi.S(220);
            _side.BackColor = Theme.Side;

            _bottom.Controls.Clear();
            _bottom.Dock = DockStyle.Bottom;
            _bottom.Height = Dpi.S(70);
            _bottom.BackColor = Theme.Side;

            _content.Dock = DockStyle.Fill;
            _content.BackColor = Theme.Back;

            string[] pages =
            {
                Loc.T("Inicio", "Home"), Loc.T("Pantalla", "Display"), Loc.T("Gráficos", "Graphics"),
                Loc.T("Audio", "Audio"), Loc.T("Juego", "Game"), Loc.T("Controles", "Controls"),
                Loc.T("Avanzado", "Advanced"), Loc.T("Instalación", "Install"), Loc.T("Registro", "Log"),
            };
            // Dock=Top apila al reves: se agregan del ultimo al primero.
            for (int i = pages.Length - 1; i >= 0; i--)
            {
                var b = new SideButton(pages[i]);
                int idx = i;
                b.Click += (s, e) => ShowPage(idx);
                _side.Controls.Add(b);
                _nav.Insert(0, b);
            }
            var logo = new Label
            {
                Dock = DockStyle.Top, Height = Dpi.S(92), Text = "WARIO LAND\nSHAKE IT!",
                Font = Theme.Title(15f), ForeColor = Theme.Gold, BackColor = Theme.Side,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(Dpi.S(16), 0, 0, 0),
            };
            _side.Controls.Add(logo);
            // Idioma del lanzador, siempre disponible (tambien durante la instalacion).
            var lang = new FlatBtn(Loc.English ? "🌐  English  →  Español" : "🌐  Español  →  English")
            {
                AutoSize = false, Dock = DockStyle.Bottom, Height = Dpi.S(34), Font = Theme.UI(8.75f),
            };
            lang.Click += (s, e) =>
            {
                Loc.English = !Loc.English;
                _ls.Set("lang", Loc.English ? "en" : "es");
                int page = _page;
                BeginInvoke(new Action(() => { BuildShell(); ShowPage(page); }));
            };
            _side.Controls.Add(lang);
            _side.Controls.Add(new Label
            {
                Dock = DockStyle.Bottom, Height = Dpi.S(26), Text = "v" + CodeVersion,
                ForeColor = Theme.Muted, BackColor = Theme.Side, Font = Theme.UI(8.5f),
                TextAlign = ContentAlignment.MiddleCenter,
            });
            // The code is newer than the built program (an update or a new release): a gold button that
            // can't be missed, taking the player straight to Install.
            if (_root != null && NeedsRebuild)
            {
                var rebuild = new FlatBtn("⚠  " + Loc.T("Recompilar ahora", "Rebuild now"), true)
                {
                    AutoSize = false, Dock = DockStyle.Bottom, Height = Dpi.S(44), Font = Theme.UI(10.5f, FontStyle.Bold),
                };
                rebuild.Click += (s, e) => ShowPage(InstallPageIndex);
                _side.Controls.Add(rebuild);
            }

            var hint = new Label
            {
                AutoSize = false, Width = Dpi.S(220), Height = Dpi.S(70), Dock = DockStyle.Left,
                Text = Loc.T("En el juego: F10 abre el menú del port,\nF11 cambia ventana / pantalla completa.",
                             "In game: F10 opens the port menu,\nF11 toggles window / fullscreen."),
                ForeColor = Theme.Muted, Font = Theme.UI(8.5f), Padding = new Padding(14, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            _play = new FlatBtn(Loc.T("JUGAR", "PLAY"), true) { Dock = DockStyle.Right, Width = Dpi.S(230) };
            _play.Click += (s, e) => Play();
            _play.Enabled = !Building;
            _status.Dock = DockStyle.Fill;
            _status.ForeColor = Theme.Muted;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(16, 0, 0, 0);
            _bottom.Controls.Add(_status);
            _bottom.Controls.Add(_play);
            _bottom.Controls.Add(hint);

            Controls.Add(_content);
            Controls.Add(_side);
            Controls.Add(_bottom);
        }

        private void ShowPage(int index)
        {
            // Sin juego instalado solo se puede usar la pagina Instalacion (y ver el registro).
            bool installed = IsInstalled();
            if (!installed && index != 7 && index != 8) index = 7;
            _page = index;
            for (int i = 0; i < _nav.Count; i++)
            {
                _nav[i].Enabled = installed || i == 7 || i == 8;
                _nav[i].Selected = i == index;
                _nav[i].Invalidate();
            }
            if (_play != null) _play.Text = installed ? Loc.T("JUGAR", "PLAY") : Loc.T("INSTALAR", "INSTALL");
            _cfg.Reload();
            _content.SuspendLayout();
            foreach (Control c in _content.Controls.Cast<Control>().ToArray()) c.Dispose();
            _content.Controls.Clear();
            Control page;
            switch (index)
            {
                case 0: page = BuildHome(); break;
                case 1: page = BuildDisplay(); break;
                case 2: page = BuildGraphics(); break;
                case 3: page = BuildAudio(); break;
                case 4: page = BuildGame(); break;
                case 5: page = BuildControls(); break;
                case 6: page = BuildAdvanced(); break;
                case 7: page = BuildInstall(); break;
                default: page = BuildLog(); break;
            }
            page.Dock = DockStyle.Fill;
            _content.Controls.Add(page);
            _content.ResumeLayout();
            UpdateStatus();
        }

        // Elige la imagen del disco y la carpeta de destino, extrae el juego y lo guarda como paths.dvd_root.
        private bool ImportDiscInteractive()
        {
                string image;
                using (var dlg = new OpenFileDialog { Filter = DiscImport.Filter, Title = Loc.T("Elige la imagen del disco de Wario Land: Shake It!", "Pick the Wario Land: Shake It! disc image") })
                {
                    if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                    image = dlg.FileName;
                }
                string dest;
                using (var fb = new FolderPicker
                {
                    Description = Loc.T("¿Dónde extraer el juego? (hacen falta unos 4,5 GB libres; se crea una carpeta DATA dentro)",
                                        "Where to extract the game? (about 4.5 GB free needed; a DATA folder is made inside)"),
                    SelectedPath = Path.Combine(UserData, "Game"), ShowNewFolderButton = true,
                })
                {
                    Directory.CreateDirectory(Path.Combine(UserData, "Game"));
                    if (fb.ShowDialog(this) != DialogResult.OK) return false;
                    dest = fb.SelectedPath;
                }
                string data = DiscImport.Run(this, _playDir, image, dest);
                if (data == null) return false;
                Write(t => t.SetString("paths", "dvd_root", data));
                MessageBox.Show(this, Loc.T("Juego listo en " + data + ". La imagen original ya no hace falta.",
                    "Game ready in " + data + ". The original image is no longer needed."), Text);
            return true;
        }

        // Cabecera: UserData\launcher_banner.(png|jpg), si no la de local-assets del port.
        private string BannerPath()
        {
            foreach (string dir in new[] { UserData, _root == null ? null : Path.Combine(_root, "local-assets") })
            {
                if (dir == null) continue;
                foreach (string ext in new[] { ".png", ".jpg" })
                {
                    string f = Path.Combine(dir, "launcher_banner" + ext);
                    if (File.Exists(f)) return f;
                }
            }
            return Path.Combine(UserData, "launcher_banner.png");
        }

        private void Reshow(int index) => BeginInvoke(new Action(() => ShowPage(index)));

        // Guarda una clave en Config.toml (recargando antes: el juego tambien escribe en el archivo).
        private void Write(Action<TomlFile> change)
        {
            try
            {
                _cfg.Reload();
                change(_cfg);
                _cfg.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, Loc.T("No se pudo guardar Config.toml:\n", "Could not save Config.toml:\n") + ex.Message,
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            UpdateStatus();
        }

        // ================================================================== opciones compartidas

        private static readonly double[] Scales = { 1.0, 1.5, 2.25, 3.0 };  // 480p, 720p, 1080p, 1440p

        private static string ScaleLabel(double s)
        {
            if (s <= 0) return Loc.T("Automática (según la ventana)", "Automatic (follows the window)");
            int lines = (int)Math.Round(480 * s);
            string name = lines == 480 ? Loc.T("480p (original)", "480p (original)")
                : lines == 1080 ? "1080p (Full HD)" : lines == 1440 ? "1440p (2K)" : lines == 2160 ? "2160p (4K)"
                : lines == 2880 ? "2880p (5K)" : lines == 3840 ? "3840p (8K)" : lines + "p";
            return name;
        }

        private DarkCombo ScaleCombo()
        {
            double cur = _cfg.GetDouble("video", "resolution_multiplier", 2.25);
            var items = Scales.Select(s => new Choice(ScaleLabel(s), s)).ToArray();
            // Valores fuera de la lista (p. ej. 4K de una version anterior): se muestra el mas cercano.
            return Combo(items, items.OrderBy(c => Math.Abs((double)c.Value - cur)).First(),
                v => Write(t => t.SetDouble("video", "resolution_multiplier", (double)v)));
        }

        private DarkCombo DisplayModeCombo()
        {
            string cur = _cfg.GetString("video", "display_mode", "borderless");
            var items = new[]
            {
                new Choice(Loc.T("Ventana", "Windowed"), "windowed"),
                new Choice(Loc.T("Pantalla completa sin bordes", "Borderless fullscreen"), "borderless"),
                new Choice(Loc.T("Pantalla completa exclusiva", "Exclusive fullscreen"), "exclusive"),
            };
            return Combo(items, items.FirstOrDefault(c => (string)c.Value == cur), v => Write(t => t.SetString("video", "display_mode", (string)v)));
        }

        private DarkCombo AspectCombo()
        {
            int cur = _cfg.GetInt("video", "aspect_mode", _cfg.GetBool("video", "widescreen", true) ? 2 : 0);
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
                t.SetBool("video", "widescreen", (int)v != 0); // el juego tambien dibuja en panoramico
            }));
        }

        private const int InstallPageIndex = 7;  // ShowPage: case 7 = BuildInstall

        // The port's headline feature: the original game never let levels fill a 16:9 screen.
        private void FullWidescreenCheck(Page p)
        {
            p.Check(Loc.T("★ 16:9 real en los niveles (experimental)", "★ True 16:9 in levels (experimental)"), _cfg.GetBool("video", "full_widescreen", false),
                v => Write(t => t.SetBool("video", "full_widescreen", v)),
                Loc.T("Novedad del port: quita los paneles laterales y el nivel llena toda la pantalla, algo que el juego original nunca permitió. Desactivada: los paneles originales. Solo con 16:9, Llenar o Cinemático; se aplica al iniciar el juego.",
                      "New in this port: removes the side panels so the level fills the whole screen, something the original game never allowed. Off: the original panels. Only with 16:9, Fill or Cinematic; applies when the game starts."));
        }

        private DarkCombo IntroCombo()
        {
            var items = new[]
            {
                new Choice(Loc.T("Completa (se salta con A, 2 o +)", "Full (skip with A, 2 or +)"), false),
                new Choice(Loc.T("Corta (1 cuadro)", "Short (1 frame)"), true),
            };
            return Combo(items, items[File.Exists(ShortIntroFlag) ? 1 : 0], v =>
            {
                try
                {
                    if ((bool)v) File.WriteAllText(ShortIntroFlag, "");
                    else if (File.Exists(ShortIntroFlag)) File.Delete(ShortIntroFlag);
                }
                catch { }
                UpdateStatus();
            });
        }

        // ================================================================== paginas

        private Control BuildHome()
        {
            var p = new Page();
            var banner = new Banner(BannerPath())
            {
                Height = Dpi.S(330), Dock = DockStyle.Top,
                Subtitle = "RWLE01 · " + Loc.T("recompilación nativa para PC", "native PC recompilation") + "  ·  by Santiago109  ·  " + Loc.T("en pruebas", "in testing"),
            };
            p.Controls.Add(banner);
            p.Top0 = 350;
            p.Section(Loc.T("Ajustes rápidos", "Quick settings"));
            p.Row(Loc.T("Modo de pantalla", "Display mode"), DisplayModeCombo());
            p.Row(Loc.T("Resolución interna", "Render resolution"), ScaleCombo());
            p.Row(Loc.T("Relación de aspecto", "Aspect ratio"), AspectCombo());
            p.Row(Loc.T("Intro", "Intro"), IntroCombo());
            FullWidescreenCheck(p);
            p.Section(Loc.T("Estado", "Status"));
            string dvd = _cfg.GetString("paths", "dvd_root", "");
            bool okGame = GameFolderOk(dvd, out string why);
            p.Info(okGame ? "✔ " + Loc.T("Juego encontrado: ", "Game found: ") + dvd : "✖ " + why, okGame ? Theme.Good : Theme.Bad);
            p.Info(File.Exists(GameExe) ? "✔ WarioRecomp.exe" : "✖ " + Loc.T("Todavía no está compilado: ve a Instalación (o pulsa JUGAR).",
                "Not built yet: go to Install (or press PLAY)."), File.Exists(GameExe) ? Theme.Good : Theme.Bad);
            return p;
        }

        private Control BuildDisplay()
        {
            var p = new Page();
            p.Header(Loc.T("Pantalla", "Display"), Loc.T("Cómo se ve el juego en tu pantalla.", "How the game appears on your screen."));
            p.Section(Loc.T("Ventana", "Window"));
            p.Row(Loc.T("Modo de pantalla", "Display mode"), DisplayModeCombo(),
                Loc.T("Sin bordes cubre la pantalla sin cambiar la resolución de Windows. F11 cambia en el juego.",
                      "Borderless covers the screen without changing the display mode. F11 switches in game."));

            int w = _cfg.GetInt("video", "window_width", 854), h = _cfg.GetInt("video", "window_height", 480);
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
            p.Check(Loc.T("Sincronización vertical (VSync)", "Vertical sync (VSync)"), _cfg.GetBool("video", "vsync", true),
                v => Write(t => t.SetBool("video", "vsync", v)),
                Loc.T("Desactivada puede haber cortes en la imagen (tearing).", "Off can show tearing."));
            p.Section(Loc.T("Superposición", "Overlay"));
            p.Check(Loc.T("Mostrar FPS", "Show FPS"), _cfg.GetBool("video", "show_fps", false), v => Write(t => t.SetBool("video", "show_fps", v)));
            p.Check(Loc.T("Gráfica de tiempo por cuadro", "Frame time graph"), _cfg.GetBool("video", "frame_graph", false), v => Write(t => t.SetBool("video", "frame_graph", v)),
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
            var fi = new[] { new Choice(Loc.T("Desactivada", "Off"), 0), new Choice("120 FPS", 120) };
            int fcur = _cfg.GetInt("video", "frame_interpolation_fps", 0);
            p.Row(Loc.T("Interpolación de cuadros", "Frame interpolation"),
                Combo(fi, fi.FirstOrDefault(c => (int)c.Value == fcur) ?? fi[0], v => Write(t => t.SetInt("video", "frame_interpolation_fps", (int)v))),
                Loc.T("Experimental.", "Experimental."));
            p.Section(Loc.T("Renderizador", "Renderer"));
            var apis = new[] { new Choice(Loc.T("Automático", "Automatic"), "auto"), new Choice("Direct3D 12", "d3d12"), new Choice("Vulkan", "vulkan") };
            string api = _cfg.GetString("video", "graphics_api", "auto");
            p.Row(Loc.T("API gráfica", "Graphics API"), Combo(apis, apis.FirstOrDefault(c => (string)c.Value == api) ?? apis[0],
                v => Write(t => t.SetString("video", "graphics_api", (string)v))));
            var compat = new[] { new Choice(Loc.T("Automático", "Automatic"), 0), new Choice(Loc.T("Activado", "On"), 1), new Choice(Loc.T("Desactivado", "Off"), 2) };
            int cc = _cfg.GetInt("video", "gpu_compat", 0);
            p.Row(Loc.T("Compatibilidad de GPU", "GPU compatibility"), Combo(compat, compat.FirstOrDefault(c => (int)c.Value == cc) ?? compat[0],
                v => Write(t => t.SetInt("video", "gpu_compat", (int)v))), Loc.T("Solo si ves gráficos rotos.", "Only if you see broken graphics."));
            p.Check(Loc.T("No esperar a los shaders nuevos", "Don't wait for new shaders"), _cfg.GetBool("video", "skip_unready_pipelines", true),
                v => Write(t => t.SetBool("video", "skip_unready_pipelines", v)), Loc.T("Menos tirones.", "Fewer stutters."));
            p.Check(Loc.T("Filtro de copia original", "Original copy filter"), !_cfg.GetBool("video", "disable_copy_filter", true),
                v => Write(t => t.SetBool("video", "disable_copy_filter", !v)), Loc.T("Imagen más suave, como en la Wii.", "Softer picture, like on the Wii."));
            p.Section(Loc.T("Texturas", "Textures"));
            p.Check(Loc.T("Usar texturas de reemplazo", "Use replacement textures"), _cfg.GetBool("video", "texture_replacements", true),
                v => Write(t => t.SetBool("video", "texture_replacements", v)));
            p.Check(Loc.T("Volcar texturas del juego", "Dump game textures"), _cfg.GetBool("video", "texture_dumps", false),
                v => Write(t => t.SetBool("video", "texture_dumps", v)));
            p.Buttons(
                new KeyValuePair<string, Action>(Loc.T("Carpeta de texturas", "Textures folder"), () => OpenFolder(Path.Combine(UserData, "texture_replacements"))),
                new KeyValuePair<string, Action>(Loc.T("Carpeta de volcados", "Dumps folder"), () => OpenFolder(Path.Combine(UserData, "Cache", "texture_dumps"))));
            return p;
        }

        private Control BuildAudio()
        {
            var p = new Page();
            p.Header(Loc.T("Audio", "Audio"), Loc.T("Volumen y mezcla.", "Volume and mixing."));
            p.Section(Loc.T("Volumen", "Volume"));
            int vol = (int)Math.Round(_cfg.GetDouble("audio", "volume", 1.0) * 100);
            p.Slider(Loc.T("Volumen general", "Master volume"), Math.Max(0, Math.Min(100, vol)),
                v => Write(t => t.SetDouble("audio", "volume", v / 100.0)));
            p.Check(Loc.T("Silenciar", "Mute"), _cfg.GetBool("audio", "muted", false), v => Write(t => t.SetBool("audio", "muted", v)));
            p.Section(Loc.T("Rendimiento", "Performance"));
            p.Check(Loc.T("Mezclar el audio en su propio hilo", "Mix audio on its own thread"), _cfg.GetBool("audio", "mix_worker", true),
                v => Write(t => t.SetBool("audio", "mix_worker", v)),
                Loc.T("Aprovecha otro núcleo del procesador; apágalo solo si el sonido falla.", "Uses another CPU core; turn off only if sound breaks."));
            return p;
        }

        private Control BuildGame()
        {
            var p = new Page();
            p.Header(Loc.T("Juego", "Game"), Loc.T("Tu copia del juego, partidas e idioma.", "Your game dump, saves and language."));
            p.Section(Loc.T("Carpetas", "Folders"));
            string dvd = _cfg.GetString("paths", "dvd_root", "");
            Label dvdState = null;
            p.Folder(Loc.T("Carpeta del juego", "Game folder"), dvd, path =>
            {
                Write(t => t.SetString("paths", "dvd_root", path));
                bool ok = GameFolderOk(path, out string why);
                dvdState.Text = ok ? "✔ " + Loc.T("Juego correcto (RWLE01).", "Game OK (RWLE01).") : "✖ " + why;
                dvdState.ForeColor = ok ? Theme.Good : Theme.Bad;
            }, null);
            bool okNow = GameFolderOk(dvd, out string whyNow);
            dvdState = p.Info(okNow ? "✔ " + Loc.T("Juego correcto (RWLE01).", "Game OK (RWLE01).") : "✖ " + whyNow, okNow ? Theme.Good : Theme.Bad);

            string nand = _cfg.GetString("paths", "nand_root", "");
            p.Folder(Loc.T("Carpeta de partidas", "Saves folder"), nand, path =>
            {
                Write(t => { if (string.IsNullOrWhiteSpace(path)) t.Remove("paths", "nand_root"); else t.SetString("paths", "nand_root", path); });
            }, Loc.T("Vacío = la carpeta del port.", "Empty = the port folder."), true);

            p.Buttons(new KeyValuePair<string, Action>(Loc.T("Importar disco (.iso / .ciso / .wbfs / .rvz)…", "Import disc (.iso / .ciso / .wbfs / .rvz)…"), () =>
            {
                if (ImportDiscInteractive()) Reshow(4);
            }));

            p.Section(Loc.T("Idioma", "Language"));
            var langs = new[] { new Choice("English", "en"), new Choice("Español", "es"), new Choice("Français", "fr") };
            string lang = _cfg.GetString("game", "language", "en");
            p.Row(Loc.T("Idioma del juego", "Game language"), Combo(langs, langs.FirstOrDefault(c => (string)c.Value == lang) ?? langs[0],
                v => Write(t => t.SetString("game", "language", (string)v))));

            p.Section(Loc.T("Intro", "Intro"));
            p.Row(Loc.T("Video de introducción", "Opening movie"), IntroCombo());

            p.Section(Loc.T("Copias de seguridad de partidas", "Save backups"));
            p.Check(Loc.T("Copiar las partidas antes de cada inicio", "Back up saves before every start"), _ls.GetBool("backup_on_start", true),
                v => _ls.SetBool("backup_on_start", v), Loc.T("Se guardan las últimas 10.", "The last 10 are kept."));
            var list = ListBackups().Select(d => new Choice(BackupLabel(d), d)).ToList();
            if (list.Count == 0) list.Add(new Choice(Loc.T("(no hay copias todavía)", "(no backups yet)"), null));
            var bcombo = Combo(list.ToArray(), list[0], null, 300);
            p.Row(Loc.T("Copias", "Backups"), bcombo);
            p.Buttons(
                new KeyValuePair<string, Action>(Loc.T("Restaurar", "Restore"), () =>
                {
                    if (!(bcombo.SelectedItem is Choice c) || c.Value == null) return;
                    if (MessageBox.Show(this, Loc.T("¿Restaurar esta copia? Antes se hace una copia de las partidas actuales.",
                            "Restore this backup? The current saves are backed up first."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    RestoreBackup((string)c.Value);
                    Reshow(4);
                }),
                new KeyValuePair<string, Action>(Loc.T("Hacer copia ahora", "Back up now"), () => { BackupSaves(); Reshow(4); }),
                new KeyValuePair<string, Action>(Loc.T("Abrir partidas", "Open saves"), () => OpenFolder(SaveDir())));
            return p;
        }

        private Control BuildControls()
        {
            var p = new Page();
            p.Header(Loc.T("Controles", "Controls"), Loc.T("Qué tecla o botón del mando hace cada acción. El juego usa un Wii Remote de lado.",
                "Which key or gamepad button does each action. The game uses a sideways Wii Remote."));
            int style = Math.Max(0, Array.IndexOf(Bindings.Styles, _ls.Get("pad_style", "playstation")));
            var styles = new[] { new Choice("Xbox", 0), new Choice("PlayStation (DualSense / DualShock)", 1), new Choice("Nintendo (Pro Controller)", 2) };
            p.Row(Loc.T("Tipo de mando a editar", "Gamepad type to edit"), Combo(styles, styles[style], v =>
            {
                _ls.Set("pad_style", Bindings.Styles[(int)v]);
                Reshow(5);
            }), Loc.T("El port detecta el tipo de mando solo; aquí eliges cuál configurar.", "The port detects the pad type itself; here you pick which one to set up."));
            p.Check(Loc.T("Vibración", "Rumble"), _cfg.GetBool("controller", "rumble", true), v => Write(t => t.SetBool("controller", "rumble", v)));
            int rumble;
            if (!int.TryParse(_cfg.GetString("gamepad", "wario_rumble_strength", "100"), out rumble)) rumble = 100;
            p.Slider(Loc.T("Intensidad de la vibración", "Rumble strength"), Math.Max(0, Math.Min(100, rumble)),
                v => Write(t => { if (v == 100) t.Remove("gamepad", "wario_rumble_strength"); else t.SetString("gamepad", "wario_rumble_strength", v.ToString(CultureInfo.InvariantCulture)); }));

            p.Section(Loc.T("Movimiento (Wii Remote de lado)", "Motion (sideways Wii Remote)"));
            var stickModes = new[]
            {
                new Choice(Loc.T("Apuntar / inclinar según la dirección", "Aim / tilt by direction"), "aim"),
                new Choice(Loc.T("Inclinar con el eje horizontal", "Tilt with the horizontal axis"), "tilt"),
                new Choice(Loc.T("Mover (cruceta)", "Move (D-pad)"), "dpad"),
            };
            string sm = _cfg.GetString("gamepad", "wario_left_stick", "aim");
            p.Row(Loc.T("Stick izquierdo", "Left stick"), Combo(stickModes, stickModes.FirstOrDefault(c => (string)c.Value == sm) ?? stickModes[0],
                v => Write(t => { if ((string)v == "aim") t.Remove("gamepad", "wario_left_stick"); else t.SetString("gamepad", "wario_left_stick", (string)v); })),
                Loc.T("Como en el port anterior: el stick inclina el mando (hasta 85°, para apuntar) y Wario se mueve con la cruceta. «Mover» lo usa como cruceta.",
                      "Like the previous port: the stick tilts the remote (up to 85°, for aiming) and Wario moves with the D-pad. “Move” uses it as the D-pad."));
            p.Check(Loc.T("Sensores de movimiento del mando al iniciar", "Gamepad motion sensors at start"), _cfg.GetString("gamepad", "wario_motion", "on") != "off",
                v => Write(t => t.SetString("gamepad", "wario_motion", v ? "on" : "off")),
                Loc.T("DualSense / DualShock 4 / Pro Controller: inclina y sacude el mando como un Wii Remote de lado. En el juego, el panel táctil (o Captura) lo activa y desactiva.",
                      "DualSense / DualShock 4 / Pro Controller: tilt and shake the pad like a sideways Wii Remote. In game, the touchpad (or Capture) toggles it."));
            p.Check(Loc.T("Invertir inclinación de los sensores", "Invert sensor tilt"), _cfg.GetString("gamepad", "wario_motion_invert_roll", "") == "1",
                v => Write(t => { if (v) t.SetString("gamepad", "wario_motion_invert_roll", "1"); else t.Remove("gamepad", "wario_motion_invert_roll"); }));
            p.Check(Loc.T("Invertir cabeceo de los sensores", "Invert sensor pitch"), _cfg.GetString("gamepad", "wario_motion_invert_pitch", "") == "1",
                v => Write(t => { if (v) t.SetString("gamepad", "wario_motion_invert_pitch", "1"); else t.Remove("gamepad", "wario_motion_invert_pitch"); }));

            p.Section(Loc.T("Asignación", "Mapping"));
            p.ControlsHeader(Loc.T("ACCIÓN", "ACTION"), Loc.T("TECLADO", "KEYBOARD"), Loc.T("MANDO", "GAMEPAD"));
            string styleName = Bindings.Styles[style];
            // Una tecla o un boton solo puede tener una accion: se rechaza asignar uno que ya este en uso.
            var keyBoxes = new List<KeyValuePair<KeyCapture, string>>();
            var padBoxes = new List<KeyValuePair<DarkCombo, string>>();
            var padExtras = new List<KeyValuePair<string, string>>();
            Label conflictInfo = null;
            Action<string> warn = msg => { if (conflictInfo != null) { conflictInfo.Text = msg; conflictInfo.ForeColor = Color.FromArgb(255, 110, 110); } };
            string PadValue(DarkCombo c) => c.SelectedItem is Choice ch ? (string)ch.Value : "";
            string KeyOwner(KeyCapture self, string name)
            {
                foreach (var kb in keyBoxes)
                    if (kb.Key != self && string.Equals(kb.Key.Value, name, StringComparison.OrdinalIgnoreCase)) return kb.Value;
                return null;
            }
            string PadOwner(DarkCombo self, string value)
            {
                if (value.Length == 0) return null;
                foreach (var pb in padBoxes) if (pb.Key != self && PadValue(pb.Key) == value) return pb.Value;
                string selfLabel = padBoxes.FirstOrDefault(pb => pb.Key == self).Value;
                foreach (var pe in padExtras) if (pe.Key == value && pe.Value != selfLabel) return pe.Value;
                return null;
            }
            void MarkConflicts()
            {
                var dup = new List<string>();
                foreach (var kb in keyBoxes)
                {
                    string other = kb.Key.Value.Length > 0 ? KeyOwner(kb.Key, kb.Key.Value) : null;
                    kb.Key.ForeColor = other != null ? Color.FromArgb(255, 110, 110) : Theme.Text;
                    if (other != null && !dup.Contains(kb.Key.Value)) dup.Add(kb.Key.Value);
                }
                foreach (var pb in padBoxes)
                {
                    string v = PadValue(pb.Key);
                    string other = PadOwner(pb.Key, v);
                    pb.Key.ForeColor = other != null ? Color.FromArgb(255, 110, 110) : Theme.Text;
                    pb.Key.Invalidate();
                    string shown = Bindings.PadLabel(v, style);
                    if (other != null && !dup.Contains(shown)) dup.Add(shown);
                }
                if (conflictInfo == null) return;
                if (dup.Count > 0)
                    warn(Loc.T("Asignadas a más de una acción (en rojo): ", "Assigned to more than one action (in red): ") + string.Join(", ", dup.ToArray()) +
                         Loc.T(". Cambia o borra una de ellas.", ". Change or clear one of them."));
                else { conflictInfo.Text = ""; }
            }
            KeyCapture.Rejected -= _keyRejected;
            _keyRejected = msg => warn(msg);
            KeyCapture.Rejected += _keyRejected;
            foreach (var a in Bindings.Actions)
            {
                var act = a;
                List<string> keys = Bindings.Split(_cfg.GetRaw("keyboard", a.Key) != null ? _cfg.GetString("keyboard", a.Key) : a.KeyboardDefault);
                string padKey = styleName + "_" + a.Key;
                List<string> pads = Bindings.Split(_cfg.GetRaw("gamepad", padKey) != null ? _cfg.GetString("gamepad", padKey) : a.PadDefault[style]);

                var k1 = new KeyCapture(keys.Count > 0 ? keys[0] : "");
                var k2 = new KeyCapture(keys.Count > 1 ? keys[1] : "");
                Action saveKeys = () =>
                {
                    var l = new List<string>();
                    if (k1.Value.Length > 0) l.Add(k1.Value);
                    if (k2.Value.Length > 0 && k2.Value != k1.Value) l.Add(k2.Value);
                    string joined = string.Join(",", l.ToArray());
                    Write(t => { if (joined == act.KeyboardDefault) t.Remove("keyboard", act.Key); else t.SetString("keyboard", act.Key, joined); });
                };
                k1.Changed += saveKeys; k2.Changed += saveKeys;
                string actLabel = Loc.English ? a.En : a.Es;
                foreach (var kc in new[] { k1, k2 })
                {
                    var self = kc;
                    self.Validate = name =>
                    {
                        string owner = KeyOwner(self, name);
                        return owner == null ? null : Loc.T("La tecla " + name + " ya está asignada a «" + owner + "». Bórrala allí primero (clic derecho).",
                                                            "The " + name + " key is already assigned to “" + owner + "”. Clear it there first (right-click).");
                    };
                    self.Changed += () => MarkConflicts();
                    keyBoxes.Add(new KeyValuePair<KeyCapture, string>(self, actLabel));
                }

                var options = Bindings.PadInputs.Select(i => new Choice(Bindings.PadLabel(i, style), i)).ToArray();
                // Entradas que no estan en la lista (escritas a mano) se conservan como una opcion mas.
                DarkCombo PadCombo(string value)
                {
                    var opts = options.ToList();
                    if (!opts.Any(o => (string)o.Value == value)) opts.Add(new Choice(value, value));
                    return Combo(opts.ToArray(), opts.First(o => (string)o.Value == value), null, 170);
                }
                var p1 = PadCombo(pads.Count > 0 ? pads[0] : "");
                var p2 = PadCombo(pads.Count > 1 ? pads[1] : "");
                var extra = pads.Skip(2).ToList(); // mas de dos entradas (p. ej. "back,touchpad,leftshoulder,lefttrigger")
                EventHandler savePad = (s, e) =>
                {
                    var l = new List<string>();
                    string v1 = (string)((Choice)p1.SelectedItem).Value, v2 = (string)((Choice)p2.SelectedItem).Value;
                    if (v1.Length > 0) l.Add(v1);
                    if (v2.Length > 0 && v2 != v1) l.Add(v2);
                    foreach (string x in extra) if (!l.Contains(x)) l.Add(x);
                    string joined = string.Join(",", l.ToArray());
                    Write(t => { if (joined == act.PadDefault[style]) t.Remove("gamepad", padKey); else t.SetString("gamepad", padKey, joined); });
                };
                foreach (var pc in new[] { p1, p2 })
                {
                    var self = pc;
                    self.Tag = PadValue(self);
                    bool reverting = false;
                    self.SelectedIndexChanged += (s2, e2) =>
                    {
                        if (reverting) return;
                        string v = PadValue(self);
                        string owner = PadOwner(self, v);
                        if (owner != null)
                        {
                            reverting = true;
                            string prev = (string)self.Tag;
                            foreach (var it in self.Items) if ((string)((Choice)it).Value == prev) { self.SelectedItem = it; break; }
                            reverting = false;
                            warn(Loc.T(Bindings.PadLabel(v, style) + " ya está asignado a «" + owner + "». Quítalo allí primero.",
                                       Bindings.PadLabel(v, style) + " is already assigned to “" + owner + "”. Remove it there first."));
                            return;
                        }
                        self.Tag = v;
                        savePad(s2, e2);
                        MarkConflicts();
                    };
                    padBoxes.Add(new KeyValuePair<DarkCombo, string>(self, actLabel));
                }
                foreach (string x in extra) padExtras.Add(new KeyValuePair<string, string>(x, actLabel));
                string label = Loc.English ? a.En : a.Es;
                if (extra.Count > 0) label += "  (+" + string.Join(", ", extra.Select(x => Bindings.PadLabel(x, style)).ToArray()) + ")";
                p.ControlsRow(label, k1, k2, p1, p2);
            }
            p.Info(Loc.T("Clic en una casilla del teclado y pulsa la tecla; clic derecho la borra. Una tecla o botón ya asignado no se puede repetir. Los cambios se aplican al abrir el juego.",
                         "Click a keyboard box and press the key; right-click clears it. A key or button already in use can't be assigned again. Changes apply when the game starts."), Theme.Muted);
            conflictInfo = p.Info(" ", Theme.Muted);
            MarkConflicts();
            p.Buttons(
                new KeyValuePair<string, Action>(Loc.T("Restaurar teclado", "Reset keyboard"), () =>
                {
                    Write(t => { foreach (var a in Bindings.Actions) t.Remove("keyboard", a.Key); });
                    Reshow(5);
                }),
                new KeyValuePair<string, Action>(Loc.T("Restaurar mando", "Reset gamepad"), () =>
                {
                    Write(t => { foreach (var a in Bindings.Actions) t.Remove("gamepad", styleName + "_" + a.Key); });
                    Reshow(5);
                }));
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
                _ls.Set("lang", (string)v);
                Loc.English = (string)v == "en";
                BeginInvoke(new Action(() => { BuildShell(); ShowPage(6); }));
            }));
            p.Check(Loc.T("Cerrar el lanzador al iniciar el juego", "Close the launcher when the game starts"), _ls.GetBool("close_on_start", false),
                v => _ls.SetBool("close_on_start", v));
            p.Buttons(new KeyValuePair<string, Action>(Loc.T("Acceso directo en el escritorio", "Desktop shortcut"), CreateShortcut));
            p.Section(Loc.T("Archivos", "Files"));
            p.Buttons(
                new KeyValuePair<string, Action>(Loc.T("Carpeta del port", "Port folder"), () => OpenFolder(_playDir)),
                new KeyValuePair<string, Action>("Config.toml", () => OpenFile(ConfigPath)),
                new KeyValuePair<string, Action>(Loc.T("Carpeta de registros", "Logs folder"), () => OpenFolder(Path.Combine(UserData, "Logs"))));
            p.Buttons(new KeyValuePair<string, Action>(Loc.T("Borrar la caché de shaders", "Clear the shader cache"), () =>
            {
                string cache = Path.Combine(UserData, "Cache");
                if (MessageBox.Show(this, Loc.T("¿Borrar la caché de shaders? La próxima partida tendrá algunos tirones mientras se vuelve a crear.",
                        "Clear the shader cache? The next session will stutter a bit while it is rebuilt."), Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try
                {
                    int n = 0;
                    if (Directory.Exists(cache))
                        foreach (string f in Directory.GetFiles(cache, "*.db*", SearchOption.TopDirectoryOnly)) { File.Delete(f); n++; }
                    MessageBox.Show(this, Loc.T("Archivos borrados: ", "Files deleted: ") + n, Text);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
            }));
            p.Info(Loc.T("Si el juego solo muestra una pantalla negra después de actualizar, borrar la caché suele ayudar.",
                         "If the game only shows a black screen after an update, clearing the cache usually helps."), Theme.Muted);
            p.Section(Loc.T("Créditos", "Credits"));
            p.Info("WarioRecomp — " + Loc.T("port nativo para PC de Wario Land: Shake It! (RWLE01), por ", "native PC port of Wario Land: Shake It! (RWLE01), by ") + "Santiago109", Theme.Gold);
            p.Info(Loc.T("Versión en pruebas: puede haber fallos, cuelgues o partes que no funcionen.", "Testing build: expect bugs, crashes or parts that don't work yet."), Theme.Muted);
            foreach (string line in new[]
            {
                "WiiCompiled — patchzyy " + Loc.T("y colaboradores: recompilador y runtime (GPL-3.0)", "and contributors: recompiler and runtime (GPL-3.0)"),
                "Aurora — Luke Street: " + Loc.T("gráficos GX de Wii sobre WebGPU (MIT)", "Wii GX graphics on WebGPU (MIT)"),
                "Dawn, SDL3, llvm-mingw, CMake, Ninja, .NET — " + Loc.T("compilación y ejecución", "build and runtime"),
                "nod / nodtool — Luke Street: " + Loc.T("lectura de imágenes de disco (MIT)", "disc image reading (MIT)"),
                "Dolphin Emulator — " + Loc.T("base de firmas de funciones del SDK (GPL-2.0+)", "SDK function signature database (GPL-2.0+)"),
                "Dreaminerryday — " + Loc.T("fan art de Wario del encabezado y el icono", "Wario fan art for the header and the icon"),
                "Capstone — " + Loc.T("desensamblador para las herramientas de análisis (BSD)", "disassembler for the analysis tools (BSD)"),
                Loc.T("Proyecto de fans no oficial; no incluye el juego.", "Unofficial fan project; the game is not included."),
            })
                p.Info("•  " + line, Theme.Muted);
            p.Buttons(new KeyValuePair<string, Action>(Loc.T("Licencias de terceros", "Third-party notices"), () =>
            {
                string notices = _root == null ? null : Path.Combine(_root, "fw", "THIRD-PARTY-NOTICES.md");
                if (notices != null && File.Exists(notices)) OpenFile(notices);
            }));
            return p;
        }

        private Control BuildLog()
        {
            var host = new Panel { BackColor = Theme.Back, Padding = new Padding(Dpi.S(24), Dpi.S(16), Dpi.S(24), Dpi.S(16)) };
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Dpi.S(44), FlowDirection = FlowDirection.LeftToRight, BackColor = Theme.Back };
            var title = new Label { Text = Loc.T("Registro", "Log"), Font = Theme.Title(18f), ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 0, 24, 0) };
            var box = new RichTextBox
            {
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 8, 18), ForeColor = Theme.Text, BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9f), ReadOnly = true, WordWrap = false, DetectUrls = false,
            };
            var refresh = new FlatBtn(Loc.T("Actualizar", "Refresh"));
            var copy = new FlatBtn(Loc.T("Copiar", "Copy"));
            var open = new FlatBtn(Loc.T("Abrir carpeta", "Open folder"));
            var source = new Label { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(16, 8, 0, 0) };
            Action load = () =>
            {
                string file = LatestLog();
                source.Text = file ?? Loc.T("Todavía no hay registros: se crean al jugar.", "No logs yet: they are made when you play.");
                if (file == null) { box.Text = ""; return; }
                try
                {
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs))
                    {
                        string text = sr.ReadToEnd();
                        if (text.Length > 400000) text = "…\n" + text.Substring(text.Length - 400000);
                        box.Text = text;
                    }
                    box.SelectionStart = box.TextLength;
                    box.ScrollToCaret();
                }
                catch (Exception ex) { box.Text = ex.Message; }
            };
            refresh.Click += (s, e) => load();
            copy.Click += (s, e) => { if (box.TextLength > 0) Clipboard.SetText(box.Text); };
            open.Click += (s, e) => OpenFolder(Path.Combine(UserData, "Logs"));
            top.Controls.AddRange(new Control[] { title, refresh, copy, open, source });
            host.Controls.Add(box);
            host.Controls.Add(top);
            load();
            return host;
        }

        // ================================================================== acciones

        private void Play()
        {
            if (_running != null && !_running.HasExited) { MessageBox.Show(this, Loc.T("El juego ya está abierto.", "The game is already running."), Text); return; }
            if (_root != null && !IsInstalled()) { if (_page != 7) ShowPage(7); InstallFlow(); return; }
            if (!File.Exists(GameExe))
            {
                MessageBox.Show(this, Loc.T("No se encuentra WarioRecomp.exe junto al lanzador.", "WarioRecomp.exe was not found next to the launcher."), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            _cfg.Reload();
            if (!GameFolderOk(_cfg.GetString("paths", "dvd_root", ""), out string why))
            {
                MessageBox.Show(this, why + "\n\n" + Loc.T("Elige la carpeta del juego en la página Juego.", "Pick the game folder on the Game page."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ShowPage(4);
                return;
            }
            try { File.Delete(Path.Combine(_playDir, "autoexit.txt")); } catch { }
            if (_ls.GetBool("backup_on_start", true)) BackupSaves();
            try
            {
                _running = Process.Start(new ProcessStartInfo(GameExe) { WorkingDirectory = _playDir, UseShellExecute = false });
                if (_running == null) return;
                _running.EnableRaisingEvents = true;
                _running.Exited += (s, e) => BeginInvoke(new Action(() =>
                {
                    _play.Enabled = true;
                    _play.Text = Loc.T("JUGAR", "PLAY");
                    if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                    if (_page == 8) ShowPage(8);
                }));
                if (_ls.GetBool("close_on_start", false)) { Close(); return; }
                _play.Enabled = false;
                _play.Text = Loc.T("EN JUEGO…", "PLAYING…");
                WindowState = FormWindowState.Minimized;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateStatus()
        {
            if (_root != null && !File.Exists(GameExe)) { _status.Text = ""; return; }
            try
            {
                string mode = _cfg.GetString("video", "display_mode", "borderless");
                string modeText = mode == "windowed" ? Loc.T("Ventana", "Windowed") : mode == "exclusive" ? Loc.T("Pantalla completa", "Fullscreen") : Loc.T("Sin bordes", "Borderless");
                double s = _cfg.GetDouble("video", "resolution_multiplier", 2.25);
                string res = s <= 0 ? Loc.T("resolución automática", "auto resolution") : (int)Math.Round(480 * s) + "p";
                int asp = _cfg.GetInt("video", "aspect_mode", 2);
                string aspText = asp == 0 ? "4:3" : asp == 1 ? "16:9" : asp == 3 ? "2.39:1" : Loc.T("llenar", "fill");
                string intro = File.Exists(ShortIntroFlag) ? Loc.T("intro corta", "short intro") : Loc.T("intro saltable", "skippable intro");
                _status.Text = res + "  ·  " + aspText + "  ·  " + modeText + "  ·  " + intro;
            }
            catch { _status.Text = ""; }
        }

        private static bool GameFolderOk(string dir, out string why)
        {
            why = "";
            if (string.IsNullOrWhiteSpace(dir)) { why = Loc.T("No has elegido la carpeta del juego.", "No game folder selected."); return false; }
            if (!Directory.Exists(dir)) { why = Loc.T("La carpeta del juego no existe: ", "The game folder does not exist: ") + dir; return false; }
            string dol = Path.Combine(dir, "sys", "main.dol");
            if (!File.Exists(dol) || !Directory.Exists(Path.Combine(dir, "files")))
            {
                why = Loc.T("Esa carpeta no parece el juego extraído (falta sys\\main.dol o files\\).", "That folder does not look like the extracted game (no sys\\main.dol or files\\).");
                return false;
            }
            string boot = Path.Combine(dir, "sys", "boot.bin");
            if (File.Exists(boot))
            {
                try
                {
                    var id = new byte[6];
                    using (var f = File.OpenRead(boot)) f.Read(id, 0, 6);
                    string gid = System.Text.Encoding.ASCII.GetString(id);
                    if (gid != "RWLE01") { why = Loc.T("El juego es ", "The game is ") + gid + Loc.T("; este port es para RWLE01 (EE. UU.).", "; this port is for RWLE01 (US)."); return false; }
                }
                catch { }
            }
            if (!DiscImport.GameFolderMatches(dir))
            {
                why = Loc.T("El main.dol de esa carpeta no es el de RWLE01 sin modificar que usa el port.",
                            "That folder's main.dol is not the unmodified RWLE01 one the port uses.");
                return false;
            }
            return true;
        }

        // ---- partidas ----
        private string SaveDir()
        {
            string nand = _cfg.GetString("paths", "nand_root", "");
            string root = string.IsNullOrWhiteSpace(nand) ? Path.Combine(UserData, "NAND") : nand;
            return Path.Combine(root, SaveSubPath);
        }

        private List<string> ListBackups()
        {
            if (!Directory.Exists(BackupDir)) return new List<string>();
            return Directory.GetDirectories(BackupDir).OrderByDescending(d => d).ToList();
        }

        private static string BackupLabel(string dir)
        {
            string name = Path.GetFileName(dir);
            return DateTime.TryParseExact(name, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t)
                ? t.ToString("yyyy-MM-dd  HH:mm:ss") : name;
        }

        private void BackupSaves()
        {
            try
            {
                string src = SaveDir();
                if (!Directory.Exists(src) || Directory.GetFiles(src).Length == 0) return;
                string dst = Path.Combine(BackupDir, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                CopyDir(src, dst);
                foreach (string old in ListBackups().Skip(10))
                    try { Directory.Delete(old, true); } catch { }
            }
            catch { }
        }

        private void RestoreBackup(string backup)
        {
            try
            {
                BackupSaves();
                string dst = SaveDir();
                Directory.CreateDirectory(dst);
                foreach (string f in Directory.GetFiles(dst)) File.Delete(f);
                CopyDir(backup, dst);
                MessageBox.Show(this, Loc.T("Partidas restauradas.", "Saves restored."), Text);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        private string LatestLog()
        {
            string logs = Path.Combine(UserData, "Logs");
            if (!Directory.Exists(logs)) return null;
            return Directory.GetFiles(logs, "console.log", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }

        private void CreateShortcut()
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnk = Path.Combine(desktop, "Wario Land Shake It!.lnk");
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                dynamic sc = shell.CreateShortcut(lnk);
                sc.TargetPath = Application.ExecutablePath;
                sc.WorkingDirectory = _playDir;
                sc.IconLocation = (File.Exists(GameExe) ? GameExe : Application.ExecutablePath) + ",0";
                sc.Description = "Wario Land: Shake It! (WarioRecomp)";
                sc.Save();
                MessageBox.Show(this, Loc.T("Acceso directo creado en el escritorio.", "Shortcut created on the desktop."), Text);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text); }
        }

        private static void OpenFolder(string dir)
        {
            try { Directory.CreateDirectory(dir); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); } catch { }
        }

        private static void OpenFile(string file)
        {
            try
            {
                if (!File.Exists(file)) File.WriteAllText(file, "");
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + file + "\"") { UseShellExecute = true });
            }
            catch { }
        }

        // Captura de teclas para la pagina Controles (antes de que las flechas muevan el foco).
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (KeyCapture.Active != null) { KeyCapture.Active.Deliver(keyData); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private Action<string> _keyRejected;

        // ---- ayuda para combos ----
        private static DarkCombo Combo(Choice[] items, Choice selected, Action<object> onChange, int width = 300)
        {
            var c = new DarkCombo(width);
            c.Items.AddRange(items);
            c.SelectedItem = selected ?? items[0];
            if (onChange != null) c.SelectedIndexChanged += (s, e) => onChange(((Choice)c.SelectedItem).Value);
            return c;
        }
    }
}
