using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WarioLauncher
{
    // Instalacion: con la copia del juego de cada usuario, traduce y compila WarioRecomp.exe en su PC
    // (tools\build-wario.ps1), como el instalador de WiiCompiled. El repositorio y la descarga no llevan
    // nada del juego: solo el codigo fuente y las herramientas.
    public sealed partial class MainForm
    {
        private Process _build;
        private Timer _buildTimer;
        private RichTextBox _buildLog;
        private Label _buildState;

        private static string FindRoot(string dir)
        {
            foreach (string c in new[] { dir, Path.GetDirectoryName(dir) })
                if (!string.IsNullOrEmpty(c) && File.Exists(Path.Combine(c, "tools", "build-wario.ps1"))) return c;
            return null;
        }

        private string StatusFile => _root == null ? null : Path.Combine(_root, "build-logs", "status.txt");
        private bool Building => _build != null && !_build.HasExited;

        // Instalado = juego valido configurado y WarioRecomp.exe compilado.
        private bool IsInstalled()
        {
            if (!File.Exists(GameExe)) return false;
            _cfg.Reload();
            return GameFolderOk(_cfg.GetString("paths", "dvd_root", ""), out _);
        }

        private Control BuildInstall()
        {
            var p = new Page();
            bool installed = IsInstalled();
            p.Header(Loc.T("Instalación", "Install"), installed
                ? Loc.T("El port está instalado. Aquí puedes cambiar el juego o volver a compilar.", "The port is installed. Here you can change the game or build again.")
                : Loc.T("Para empezar, elige tu copia de Wario Land: Shake It! y pulsa Instalar. Las demás páginas se activan al terminar.",
                        "To start, pick your copy of Wario Land: Shake It! and press Install. The other pages unlock when it finishes."));
            if (_root == null)
            {
                p.Info(Loc.T("Este lanzador no está junto a las herramientas del port (falta tools\\build-wario.ps1), así que aquí no puede instalar. Descarga el zip WarioRecomp-<versión> del release de GitHub, descomprímelo y abre el WarioLauncher.exe de dentro.",
                             "This launcher is not next to the port's tools (tools\\build-wario.ps1 is missing), so it cannot install here. Download the WarioRecomp-<version> zip from the GitHub release, extract it and open the WarioLauncher.exe inside."), Theme.Bad);
                return p;
            }

            // ---- 1. juego
            p.Section(Loc.T("1. Tu juego", "1. Your game"));
            string dvd = _cfg.GetString("paths", "dvd_root", "");
            bool gameOk = GameFolderOk(dvd, out string why);
            p.Info(gameOk ? "✔ " + Loc.T("Juego listo en ", "Game ready in ") + dvd
                          : (string.IsNullOrWhiteSpace(dvd) ? "• " + Loc.T("Todavía no has elegido el juego.", "No game chosen yet.") : "✖ " + why),
                   gameOk ? Theme.Good : string.IsNullOrWhiteSpace(dvd) ? Theme.Muted : Theme.Bad);
            p.Info(Loc.T("Una imagen de disco se extrae una vez (4,4 GB) en la carpeta que elijas.", "A disc image is extracted once (4.4 GB) to the folder you pick."), Theme.Muted);
            p.Buttons(
                new KeyValuePair<string, Action>(Loc.T("Elegir imagen de disco…", "Choose disc image…"), () => { if (ImportDiscInteractive()) Reshow(7); }),
                new KeyValuePair<string, Action>(Loc.T("Elegir carpeta ya extraída…", "Choose extracted folder…"), () => { if (PickExtractedFolder()) Reshow(7); }));

            // ---- 2. instalar
            p.Section(Loc.T("2. Instalar", "2. Install"));
            bool built = File.Exists(GameExe);
            bool toolsHere = File.Exists(Path.Combine(_root, "toolchain", "llvm-mingw", "bin", "x86_64-w64-mingw32-clang++.exe"));
            if (built)
                p.Info("✔ WarioRecomp.exe " + Loc.T("compilado el ", "built on ") + File.GetLastWriteTime(GameExe).ToString("yyyy-MM-dd HH:mm"), Theme.Good);
            else
                p.Info(Loc.T("Tarda de 15 a 45 minutos según el PC y ocupa unos 3 GB.", "Takes 15 to 45 minutes depending on the PC and uses about 3 GB.") +
                       (toolsHere ? "" : " " + Loc.T("La primera vez descarga las herramientas (~2 GB).", "The first time it downloads the tools (~2 GB).")), Theme.Muted);
            var buttons = new List<KeyValuePair<string, Action>>();
            if (!built)
                buttons.Add(new KeyValuePair<string, Action>(Loc.T("Instalar", "Install"), InstallFlow));
            else
            {
                buttons.Add(new KeyValuePair<string, Action>(Loc.T("Recompilar todo", "Rebuild everything"), () => StartBuild(false)));
                if (Directory.Exists(Path.Combine(_root, "nb-win")) && Directory.Exists(Path.Combine(_root, "generated")))
                    buttons.Add(new KeyValuePair<string, Action>(Loc.T("Recompilar rápido", "Quick rebuild"), () => StartBuild(true)));
            }
            buttons.Add(new KeyValuePair<string, Action>(Loc.T("Cancelar", "Cancel"), CancelBuild));
            buttons.Add(new KeyValuePair<string, Action>(Loc.T("Registros", "Logs"), () => OpenFolder(Path.Combine(_root, "build-logs"))));
            p.Buttons(buttons.ToArray());

            // ---- 3. updates
            p.Section(Loc.T("3. Actualizaciones", "3. Updates"));
            p.Info(Loc.T("Versión del código: ", "Code version: ") + CodeVersion + (built ? "   |   " + Loc.T("compilada: ", "built: ") + BuiltVersion : ""), Theme.Muted);
            if (NeedsRebuild)
                p.Info("⚠ " + Loc.T("El código es más nuevo que el programa compilado: recompila.", "The code is newer than the built program: rebuild."), Theme.Gold);
            p.Buttons(new KeyValuePair<string, Action>(Loc.T("Buscar actualizaciones", "Check for updates"), CheckAndUpdate));

            _buildState = p.Info("", Theme.Gold);
            _buildLog = new RichTextBox
            {
                BackColor = Color.FromArgb(12, 8, 18), ForeColor = Theme.Text, BorderStyle = BorderStyle.None, ReadOnly = true,
                Font = new Font("Consolas", 9f), WordWrap = false, DetectUrls = false, Size = new Size(Dpi.S(820), Dpi.S(220)),
            };
            p.Embed(_buildLog);
            RefreshBuildView();
            return p;
        }

        // Instalar: si falta el juego, primero se elige la imagen y donde extraerla; despues se compila.
        private void InstallFlow()
        {
            if (Building) return;
            _cfg.Reload();
            if (!GameFolderOk(_cfg.GetString("paths", "dvd_root", ""), out _))
            {
                if (!ImportDiscInteractive()) return;
                ShowPage(7);
            }
            StartBuild(false);
        }

        // Carpeta con el juego ya extraido (sys\main.dol + files\).
        private bool PickExtractedFolder()
        {
            using (var fb = new FolderPicker { Description = Loc.T("Elige la carpeta del juego extraído (la que tiene sys y files)", "Pick the extracted game folder (the one with sys and files)"), ShowNewFolderButton = false })
            {
                if (fb.ShowDialog(this) != DialogResult.OK) return false;
                string dir = fb.SelectedPath;
                // Si eligen la carpeta de encima (con DATA dentro), se usa DATA.
                if (!File.Exists(Path.Combine(dir, "sys", "main.dol")) && File.Exists(Path.Combine(dir, "DATA", "sys", "main.dol"))) dir = Path.Combine(dir, "DATA");
                if (!GameFolderOk(dir, out string why)) { MessageBox.Show(this, why, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
                Write(t => t.SetString("paths", "dvd_root", dir));
                return true;
            }
        }

        private void StartBuild(bool quick)
        {
            if (Building) return;
            _cfg.Reload();
            if (!File.Exists(Path.Combine(_root, "game", "main.dol")) && !GameFolderOk(_cfg.GetString("paths", "dvd_root", ""), out string why))
            {
                MessageBox.Show(this, why + "\n\n" + Loc.T("Primero importa el disco.", "Import the disc first."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_running != null && !_running.HasExited)
            {
                MessageBox.Show(this, Loc.T("Cierra el juego antes de compilar.", "Close the game before building."), Text);
                return;
            }
            string args = "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(_root, "tools", "build-wario.ps1") + "\" -SkipRun -Lang " + (Loc.English ? "en" : "es");
            if (quick) args += " -SkipTools -SkipTranslate";
            try
            {
                if (File.Exists(StatusFile)) File.Delete(StatusFile);
                _build = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), args)
                {
                    WorkingDirectory = _root, UseShellExecute = false, CreateNoWindow = true,
                });
                _build.EnableRaisingEvents = true;
                _build.Exited += (s, e) => BeginInvoke(new Action(BuildFinished));
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            _play.Enabled = false;
            EnsureBuildTimer();
            RefreshBuildView();
        }

        private void CancelBuild()
        {
            if (!Building) return;
            if (MessageBox.Show(this, Loc.T("¿Detener la compilación? Se puede retomar después.", "Stop the build? It can be resumed later."),
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                // Termina powershell y todo lo que lanzo (cmake, ninja, clang, dotnet).
                Process.Start(new ProcessStartInfo("taskkill", "/T /F /PID " + _build.Id) { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(10000);
            }
            catch { }
        }

        private void BuildFinished()
        {
            int code = -1;
            try { code = _build.ExitCode; } catch { }
            _play.Enabled = true;
            RefreshBuildView();
            bool ok = code == 0 && File.Exists(GameExe);
            if (ok) SaveBuiltVersion();
            MessageBox.Show(this, ok ? Loc.T("¡Listo! WarioRecomp.exe está compilado. Ya puedes jugar.", "Done! WarioRecomp.exe is built. You can play now.")
                                     : Loc.T("La compilación no terminó. Revisa el registro de la página Instalación (build-logs).", "The build did not finish. Check the log on the Install page (build-logs)."),
                Text, MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (ok && _reopenAfterBuild) { ReopenLauncher(); return; }  // el .exe del launcher se cambio en la actualizacion
            if (ok) BuildShell();  // quita el boton "Recompilar ahora" de la barra lateral
            ShowPage(ok ? 0 : 7);  // al terminar bien se activan las demas paginas
            UpdateStatus();
        }

        private void EnsureBuildTimer()
        {
            if (_buildTimer != null) return;
            _buildTimer = new Timer { Interval = 1000 };
            _buildTimer.Tick += (s, e) => { if (_page == 7) RefreshBuildView(); if (!Building) { _buildTimer.Stop(); _buildTimer.Dispose(); _buildTimer = null; } };
            _buildTimer.Start();
        }

        private void RefreshBuildView()
        {
            if (_buildLog == null || _buildLog.IsDisposed) return;
            string text = "";
            try
            {
                if (StatusFile != null && File.Exists(StatusFile))
                    using (var fs = new FileStream(StatusFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs)) text = sr.ReadToEnd();
            }
            catch { }
            if (_buildLog.Text != text)
            {
                _buildLog.Text = text;
                _buildLog.SelectionStart = _buildLog.TextLength;
                _buildLog.ScrollToCaret();
            }
            string last = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
            if (_buildState != null && !_buildState.IsDisposed)
                _buildState.Text = Building ? "⏳ " + Loc.T("Compilando… ", "Building… ") + (last.Length > 20 ? last.Substring(20) : last)
                                            : (last.Length > 0 ? Loc.T("Última compilación: ", "Last build: ") + last : "");
        }
    }
}
