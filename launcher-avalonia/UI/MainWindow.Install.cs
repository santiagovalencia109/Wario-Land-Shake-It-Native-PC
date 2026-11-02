using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using WarioLauncher.Core;

namespace WarioLauncher.UI
{
    // Instalacion: con la copia del juego de cada usuario, traduce y compila el port en su PC
    // (tools/build-wario.ps1 en Windows, tools/build-wario.sh en Linux). La descarga no lleva nada del juego.
    // Tambien las actualizaciones (codigo del repositorio + el propio lanzador).
    public sealed partial class MainWindow
    {
        private Process _build;
        private DispatcherTimer _buildTimer;
        private TextBox _buildLog;
        private TextBlock _buildState;
        private bool _reopenAfterBuild;

        private bool Building
        {
            get { try { return _build != null && !_build.HasExited; } catch { return false; } }
        }

        private Control BuildInstall()
        {
            var p = new Page();
            bool installed = _port.IsInstalled();
            p.Header(Loc.T("Instalación", "Install"), installed
                ? Loc.T("El port está instalado. Aquí puedes cambiar el juego o volver a compilar.", "The port is installed. Here you can change the game or build again.")
                : Loc.T("Para empezar, elige tu copia de Wario Land: Shake It! y pulsa Instalar. Las demás páginas se activan al terminar.",
                        "To start, pick your copy of Wario Land: Shake It! and press Install. The other pages unlock when it finishes."));
            if (_port.Root == null)
            {
                p.Info(Loc.T("Este lanzador no está junto a las herramientas del port (falta tools/" + Platform.BuildScriptName + "), así que aquí no puede instalar. Descarga el paquete WarioRecomp-<versión> del release de GitHub, descomprímelo y abre el lanzador de dentro.",
                             "This launcher is not next to the port's tools (tools/" + Platform.BuildScriptName + " is missing), so it cannot install here. Download the WarioRecomp-<version> package from the GitHub release, extract it and open the launcher inside."), WarioTheme.Bad);
                return p;
            }

            // ---- 1. juego
            p.Section(Loc.T("1. Tu juego", "1. Your game"));
            string dvd = Cfg.GetString("paths", "dvd_root", "");
            bool gameOk = Port.GameFolderOk(dvd, out string why);
            p.Info(gameOk ? "✔ " + Loc.T("Juego listo en ", "Game ready in ") + dvd
                          : (string.IsNullOrWhiteSpace(dvd) ? "• " + Loc.T("Todavía no has elegido el juego.", "No game chosen yet.") : "✖ " + why),
                   gameOk ? WarioTheme.Good : string.IsNullOrWhiteSpace(dvd) ? WarioTheme.Muted : WarioTheme.Bad);
            p.Info(Loc.T("Una imagen de disco se extrae una vez (4,4 GB) en la carpeta que elijas.", "A disc image is extracted once (4.4 GB) to the folder you pick."), WarioTheme.Muted);
            p.Buttons(
                (Loc.T("Elegir imagen de disco…", "Choose disc image…"), async () => { if (await ImportDiscInteractive()) Reshow(InstallPage); }),
                (Loc.T("Elegir carpeta ya extraída…", "Choose extracted folder…"), async () => { if (await PickExtractedFolder()) Reshow(InstallPage); }));

            // ---- 2. instalar
            p.Section(Loc.T("2. Instalar", "2. Install"));
            bool built = _port.GameBuilt;
            if (built)
                p.Info("✔ " + Platform.GameExeName + " " + Loc.T("compilado el ", "built on ") + File.GetLastWriteTime(_port.GameExe).ToString("yyyy-MM-dd HH:mm"), WarioTheme.Good);
            else
                p.Info(Platform.IsWindows
                    ? Loc.T("Tarda de 15 a 45 minutos según el PC y ocupa unos 3 GB.", "Takes 15 to 45 minutes depending on the PC and uses about 3 GB.")
                    : Loc.T("Tarda de 15 a 45 minutos según el PC y ocupa unos 3 GB. Hacen falta clang, cmake, ninja, .NET 8 y las librerías de desarrollo de SDL: si falta algo, el registro dice el comando para instalarlo.",
                            "Takes 15 to 45 minutes depending on the PC and uses about 3 GB. It needs clang, cmake, ninja, .NET 8 and SDL's development libraries: if something is missing, the log shows the command to install it."), WarioTheme.Muted);
            var buttons = new List<(string, Action)>();
            if (!built) buttons.Add((Loc.T("Instalar", "Install"), InstallFlow));
            else
            {
                buttons.Add((Loc.T("Recompilar todo", "Rebuild everything"), () => StartBuild(false)));
                if (_port.CanQuickRebuild) buttons.Add((Loc.T("Recompilar rápido", "Quick rebuild"), () => StartBuild(true)));
            }
            buttons.Add((Loc.T("Cancelar", "Cancel"), CancelBuild));
            buttons.Add((Loc.T("Registros", "Logs"), () => Platform.OpenFolder(_port.BuildLogsDir)));
            p.Buttons(buttons.ToArray());

            // ---- 3. actualizaciones
            p.Section(Loc.T("3. Actualizaciones", "3. Updates"));
            p.Info(Loc.T("Versión del código: ", "Code version: ") + _port.CodeVersion + (built ? "   |   " + Loc.T("compilada: ", "built: ") + _port.BuiltVersion : "") +
                   "   |   " + Loc.T("lanzador: ", "launcher: ") + Updates.OwnVersion, WarioTheme.Muted);
            if (_port.NeedsRebuild)
                p.Info("⚠ " + Loc.T("El código es más nuevo que el programa compilado: recompila.", "The code is newer than the built program: rebuild."), WarioTheme.Gold);
            p.Buttons((Loc.T("Buscar actualizaciones", "Check for updates"), CheckAndUpdate));

            _buildState = p.Info("", WarioTheme.Gold);
            _buildLog = new TextBox
            {
                IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, FontFamily = WarioTheme.Mono, FontSize = 12,
                Background = WarioTheme.LogBack, Foreground = WarioTheme.Text, BorderThickness = new Thickness(0), Height = 240, MaxWidth = 820,
            };
            p.Add(_buildLog);
            RefreshBuildView();
            return p;
        }

        // Elige la imagen del disco y la carpeta de destino, extrae el juego y lo guarda como paths.dvd_root.
        private async Task<bool> ImportDiscInteractive()
        {
            string image = await PickDiscImage();
            if (image == null) return false;
            string suggested = Path.Combine(_port.UserData, "Game");
            try { Directory.CreateDirectory(suggested); } catch { }
            string dest = await PickFolder(Loc.T("¿Dónde extraer el juego? (hacen falta unos 4,5 GB libres; se crea una carpeta DATA dentro)",
                                                 "Where to extract the game? (about 4.5 GB free needed; a DATA folder is made inside)"), suggested);
            if (dest == null) return false;

            string play = _port.PlayDir;
            if (!File.Exists(DiscTool.NodPath(play)))
            {
                if (!await Dialogs.Ask(this, Loc.T("Para leer el disco hace falta nodtool (" + Platform.NodVersion + ", unos 3 MB, de github.com/encounter/nod, licencia MIT). ¿Descargarlo ahora?",
                                                   "Reading the disc needs nodtool (" + Platform.NodVersion + ", about 3 MB, from github.com/encounter/nod, MIT license). Download it now?"))) return false;
                string err = await Dialogs.Progress(this, Loc.T("Descargando nodtool…", "Downloading nodtool…"), async () =>
                {
                    try { await DiscTool.DownloadNod(play); return null; } catch (Exception ex) { return ex.Message; }
                });
                if (err != null) { await Dialogs.Info(this, Loc.T("No se pudo descargar nodtool: ", "Could not download nodtool: ") + err); return false; }
            }

            string gameId = null, readErr = null;
            await Dialogs.Progress(this, Loc.T("Leyendo el disco…", "Reading the disc…"), async () =>
            {
                try { gameId = await DiscTool.ReadGameId(play, image); } catch (Exception ex) { readErr = ex.Message; }
                return 0;
            });
            if (readErr != null) { await Dialogs.Info(this, Loc.T("nodtool no pudo leer el disco:\n", "nodtool could not read the disc:\n") + readErr); return false; }
            if (gameId == null) { await Dialogs.Info(this, Loc.T("No se reconoce el disco.", "The disc was not recognized.")); return false; }
            if (gameId != Port.GameId)
            {
                await Dialogs.Info(this, Loc.T("Este disco es " + gameId + ". El port es para Wario Land: Shake It! de EE. UU. (" + Port.GameId + ").",
                                               "This disc is " + gameId + ". The port is for US Wario Land: Shake It! (" + Port.GameId + ")."));
                return false;
            }

            string data = Path.Combine(dest, "DATA");
            if (Directory.Exists(data) && Directory.GetFileSystemEntries(data).Length > 0)
            {
                if (!Port.MainDolMatches(data))
                {
                    await Dialogs.Info(this, Loc.T("La carpeta " + data + " ya tiene otros archivos. Elige otra carpeta de destino.",
                                                   "The folder " + data + " already has other files. Pick another destination."));
                    return false;
                }
                await Dialogs.Info(this, Loc.T("El juego ya estaba extraído en " + data + ". Se usará esa carpeta.", "The game was already extracted to " + data + ". That folder will be used."));
            }
            else
            {
                string exErr = await Dialogs.Progress(this, Loc.T("Extrayendo el juego (unos 4 GB, puede tardar unos minutos)…", "Extracting the game (about 4 GB, may take a few minutes)…"), async () =>
                {
                    try { await DiscTool.Extract(play, image, data); return null; } catch (Exception ex) { return ex.Message; }
                });
                if (exErr != null) { await Dialogs.Info(this, Loc.T("Falló la extracción:\n", "Extraction failed:\n") + exErr); return false; }
                if (!Port.MainDolMatches(data))
                {
                    await Dialogs.Info(this, Loc.T("El disco se extrajo, pero su main.dol no coincide con la versión que usa el port (RWLE01, revisión 0 sin modificar).",
                                                   "The disc was extracted, but its main.dol does not match the version the port uses (unmodified RWLE01, revision 0)."));
                    return false;
                }
            }
            Write(t => t.SetString("paths", "dvd_root", data));
            await Dialogs.Info(this, Loc.T("Juego listo en " + data + ". La imagen original ya no hace falta.", "Game ready in " + data + ". The original image is no longer needed."));
            return true;
        }

        // Carpeta con el juego ya extraido (sys/main.dol + files/).
        private async Task<bool> PickExtractedFolder()
        {
            string dir = await PickFolder(Loc.T("Elige la carpeta del juego extraído (la que tiene sys y files)", "Pick the extracted game folder (the one with sys and files)"), null);
            if (dir == null) return false;
            dir = Port.NormalizeGameFolder(dir);
            if (!Port.GameFolderOk(dir, out string why)) { await Dialogs.Info(this, why); return false; }
            Write(t => t.SetString("paths", "dvd_root", dir));
            return true;
        }

        // Instalar: si falta el juego, primero se elige la imagen y donde extraerla; despues se compila.
        private async void InstallFlow()
        {
            if (Building) return;
            Cfg.Reload();
            if (!Port.GameFolderOk(Cfg.GetString("paths", "dvd_root", ""), out _))
            {
                if (!await ImportDiscInteractive()) return;
                ShowPage(InstallPage);
            }
            StartBuild(false);
        }

        private async void StartBuild(bool quick)
        {
            if (Building || _port.Root == null) return;
            Cfg.Reload();
            if (!File.Exists(Path.Combine(_port.Root, "game", "main.dol")) && !Port.GameFolderOk(Cfg.GetString("paths", "dvd_root", ""), out string why))
            {
                await Dialogs.Info(this, why + "\n\n" + Loc.T("Primero importa el disco.", "Import the disc first."));
                return;
            }
            if (_running != null && !_running.HasExited)
            {
                await Dialogs.Info(this, Loc.T("Cierra el juego antes de compilar.", "Close the game before building."));
                return;
            }
            try
            {
                if (File.Exists(_port.StatusFile)) File.Delete(_port.StatusFile);
                _build = Process.Start(Platform.BuildProcess(_port.Root, quick, Loc.English));
                if (_build == null) return;
                _build.EnableRaisingEvents = true;
                _build.Exited += (s, e) => Dispatcher.UIThread.Post(BuildFinished);
            }
            catch (Exception ex) { await Dialogs.Info(this, ex.Message); return; }
            _play.IsEnabled = false;
            EnsureBuildTimer();
            RefreshBuildView();
        }

        private async void CancelBuild()
        {
            if (!Building) return;
            if (!await Dialogs.Ask(this, Loc.T("¿Detener la compilación? Se puede retomar después.", "Stop the build? It can be resumed later."))) return;
            // Termina el script y todo lo que lanzo (cmake, ninja, clang, dotnet).
            try { _build.Kill(true); } catch { }
        }

        private async void BuildFinished()
        {
            int code = -1;
            try { code = _build.ExitCode; } catch { }
            _play.IsEnabled = true;
            RefreshBuildView();
            bool ok = code == 0 && _port.GameBuilt;
            if (ok) _port.SaveBuiltVersion();
            await Dialogs.Info(this, ok ? Loc.T("¡Listo! El juego está compilado. Ya puedes jugar.", "Done! The game is built. You can play now.")
                                        : Loc.T("La compilación no terminó. Revisa el registro de la página Instalación (build-logs).", "The build did not finish. Check the log on the Install page (build-logs)."));
            if (ok && _reopenAfterBuild) { ReopenLauncher(); return; }  // el ejecutable del lanzador se cambio en la actualizacion
            if (ok) BuildShell();  // quita el boton "Recompilar ahora" de la barra lateral
            ShowPage(ok ? HomePage : InstallPage);
        }

        private void EnsureBuildTimer()
        {
            if (_buildTimer != null) return;
            _buildTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _buildTimer.Tick += (s, e) =>
            {
                if (_page == InstallPage) RefreshBuildView();
                if (!Building) { _buildTimer.Stop(); _buildTimer = null; }
            };
            _buildTimer.Start();
        }

        private void RefreshBuildView()
        {
            if (_buildLog == null) return;
            string text = _port.StatusFile != null && File.Exists(_port.StatusFile) ? Port.ReadShared(_port.StatusFile) : "";
            if (_buildLog.Text != text)
            {
                _buildLog.Text = text;
                _buildLog.CaretIndex = text.Length;
            }
            string last = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
            if (_buildState != null)
                _buildState.Text = Building ? "⏳ " + Loc.T("Compilando… ", "Building… ") + (last.Length > 20 ? last.Substring(20) : last)
                                            : (last.Length > 0 ? Loc.T("Última compilación: ", "Last build: ") + last : "");
        }

        // ---- actualizaciones ----

        private async void BackgroundUpdateCheck()
        {
            if (_port.Root == null || !_port.IsInstalled() || !Ls.GetBool("auto_update", true)) return;
            string json = await Updates.FetchRemoteManifest(_port.UpdateRepo);
            if (json == null || Port.Ver(Port.JsonField(json, "version")) <= _port.CodeVersion || Building) return;
            if (await Dialogs.Ask(this, Loc.T("Hay una versión nueva: ", "A new version is available: ") + Port.JsonField(json, "version") +
                    "\n\n" + Loc.T("¿Abrir la página de instalación para actualizar?", "Open the Install page to update?")))
                ShowPage(InstallPage);
        }

        private async void CheckAndUpdate()
        {
            if (Building || _port.Root == null) return;
            string json = await Updates.FetchRemoteManifest(_port.UpdateRepo);
            if (json == null) { await Dialogs.Info(this, Loc.T("No se pudo consultar las actualizaciones (¿sin internet?).", "Could not check for updates (offline?).")); return; }
            Version remote = Port.Ver(Port.JsonField(json, "version"));
            if (remote <= _port.CodeVersion) { await Dialogs.Info(this, Loc.T("Ya tienes la última versión (", "You have the latest version (") + _port.CodeVersion + ")."); return; }
            string notes = Port.JsonField(json, Loc.English ? "notes_en" : "notes_es");
            if (!await Dialogs.Ask(this, Loc.T("Versión nueva: ", "New version: ") + remote + " (" + Loc.T("actual ", "current ") + _port.CodeVersion + ")" +
                    (string.IsNullOrEmpty(notes) ? "" : "\n\n" + notes) + "\n\n" + Loc.T("¿Descargar y actualizar?", "Download and update?"))) return;
            if (_running != null && !_running.HasExited) { await Dialogs.Info(this, Loc.T("Cierra el juego antes de actualizar.", "Close the game before updating.")); return; }

            int files = 0;
            string error = null, launcherNote = null;
            await Dialogs.Progress(this, Loc.T("Descargando la actualización…", "Downloading the update…"), async () =>
            {
                try { files = await Updates.DownloadAndApplyCode(_port); }
                catch (Exception ex) { error = ex.Message; return 0; }
                try
                {
                    if (await Updates.UpdateLauncherBinary(_port))
                    {
                        _reopenAfterBuild = true;
                        launcherNote = Loc.T("El lanzador también se actualizó a la versión ", "The launcher was updated too, to version ") + _port.LocalField("launcher") + ". " +
                                       Loc.T("Se cerrará y se abrirá el nuevo al terminar.", "It will close and open the new one when done.");
                    }
                }
                catch (Exception ex)
                {
                    launcherNote = Loc.T("No se pudo actualizar el lanzador (", "Could not update the launcher (") + ex.Message + "). " +
                                   Loc.T("Puedes bajarlo del release " + _port.LocalField("launcher") + " en GitHub.", "You can download it from the " + _port.LocalField("launcher") + " release on GitHub.");
                }
                return 0;
            });
            if (error != null) { await Dialogs.Info(this, Loc.T("La actualización falló: ", "The update failed: ") + error); return; }
            if (launcherNote != null) await Dialogs.Info(this, launcherNote);
            bool quick = _port.QuickRebuildOk();
            ShowPage(InstallPage);
            string msg = Loc.T("Archivos actualizados: ", "Files updated: ") + files + "\n" + Loc.T("Versión: ", "Version: ") + _port.CodeVersion + "\n\n" +
                (quick ? Loc.T("Basta una recompilación rápida (unos minutos).", "A quick rebuild is enough (a few minutes).")
                       : Loc.T("Hay que traducir y compilar todo de nuevo (15 a 45 minutos).", "Everything must be translated and built again (15 to 45 minutes).")) +
                "\n\n" + Loc.T("¿Recompilar ahora?", "Rebuild now?");
            if (await Dialogs.Ask(this, msg)) StartBuild(quick);
            else if (_reopenAfterBuild) ReopenLauncher();
        }

        // Tras cambiar el ejecutable del lanzador: abre el nuevo y cierra este.
        private void ReopenLauncher()
        {
            try { Process.Start(new ProcessStartInfo(Updates.OwnExe) { WorkingDirectory = _port.PlayDir, UseShellExecute = false }); }
            catch { return; }  // si no se puede abrir, sigue este
            Close();
        }
    }
}
