using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WarioLauncher
{
    // Updates: the repository has a version.json at its root. The launcher compares it with the local one,
    // downloads the code (a zip of the repository, no game data) over the installed copy and rebuilds.
    public sealed partial class MainForm
    {
        private string UpdateRepo => _ls.Get("update_repo", "santiagovalencia109/Wario-Land-Shake-It-Native-PC");
        private string LocalManifest => _root == null ? null : Path.Combine(_root, "version.json");
        private string BuiltFile => Path.Combine(_playDir, "built_version.txt");

        private static string JsonField(string json, string name)
        {
            var m = Regex.Match(json ?? "", "\"" + name + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static Version Ver(string s) { Version v; return Version.TryParse(s ?? "", out v) ? v : new Version(0, 0, 0); }

        private string LocalField(string name)
        {
            try { return File.Exists(LocalManifest) ? JsonField(File.ReadAllText(LocalManifest), name) : null; } catch { return null; }
        }

        private Version CodeVersion => Ver(LocalField("version"));

        // Version of the code the current WarioRecomp.exe was built from.
        private Version BuiltVersion
        {
            get
            {
                try
                {
                    if (File.Exists(BuiltFile)) return Ver(File.ReadAllText(BuiltFile).Trim());
                    if (File.Exists(GameExe)) { SaveBuiltVersion(); return CodeVersion; }  // installs from before updates existed
                }
                catch { }
                return new Version(0, 0, 0);
            }
        }

        private void SaveBuiltVersion()
        {
            try { Directory.CreateDirectory(_playDir); File.WriteAllText(BuiltFile, CodeVersion.ToString()); } catch { }
        }

        private bool NeedsRebuild => File.Exists(GameExe) && BuiltVersion < CodeVersion;

        // A quick rebuild is enough when the installed build is at least the version named in quick_from.
        private bool QuickRebuildOk()
        {
            string qf = LocalField("quick_from");
            return qf != null && BuiltVersion >= Ver(qf)
                && Directory.Exists(Path.Combine(_root, "nb-win")) && Directory.Exists(Path.Combine(_root, "generated"));
        }

        private static WebClient NewClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "WarioLauncher";
            wc.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            return wc;
        }

        // Returns the remote manifest text, or null if it cannot be reached.
        private Task<string> FetchRemoteManifest()
        {
            string url = "https://raw.githubusercontent.com/" + UpdateRepo + "/main/version.json";
            return Task.Run(() => { try { using (var wc = NewClient()) return wc.DownloadString(url); } catch { return null; } });
        }

        private void BackgroundUpdateCheck()
        {
            if (_root == null || !IsInstalled() || !_ls.GetBool("auto_update", true)) return;
            FetchRemoteManifest().ContinueWith(t =>
            {
                string json = t.Result;
                if (json == null || Ver(JsonField(json, "version")) <= CodeVersion) return;
                BeginInvoke(new Action(() =>
                {
                    if (Building) return;
                    if (MessageBox.Show(this, Loc.T("Hay una versión nueva: ", "A new version is available: ") + JsonField(json, "version") +
                            "\n\n" + Loc.T("¿Abrir la página de instalación para actualizar?", "Open the Install page to update?"),
                            Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) ShowPage(7);
                }));
            });
        }

        private void CheckAndUpdate()
        {
            if (Building) return;
            FetchRemoteManifest().ContinueWith(t => BeginInvoke(new Action(() =>
            {
                string json = t.Result;
                if (json == null)
                {
                    MessageBox.Show(this, Loc.T("No se pudo consultar las actualizaciones (¿sin internet?).", "Could not check for updates (offline?)."), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Version remote = Ver(JsonField(json, "version"));
                if (remote <= CodeVersion)
                {
                    MessageBox.Show(this, Loc.T("Ya tienes la última versión (", "You have the latest version (") + CodeVersion + ").", Text);
                    return;
                }
                string notes = JsonField(json, Loc.English ? "notes_en" : "notes_es");
                if (MessageBox.Show(this, Loc.T("Versión nueva: ", "New version: ") + remote + " (" + Loc.T("actual ", "current ") + CodeVersion + ")" +
                        (string.IsNullOrEmpty(notes) ? "" : "\n\n" + notes) + "\n\n" + Loc.T("¿Descargar y actualizar?", "Download and update?"),
                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                DownloadAndApply();
            })));
        }

        private void DownloadAndApply()
        {
            if (_running != null && !_running.HasExited)
            {
                MessageBox.Show(this, Loc.T("Cierra el juego antes de actualizar.", "Close the game before updating."), Text);
                return;
            }
            Cursor = Cursors.WaitCursor;
            string url = "https://github.com/" + UpdateRepo + "/archive/refs/heads/main.zip";
            string tmp = Path.Combine(Path.GetTempPath(), "WarioRecomp-update.zip");
            Task.Run(() =>
            {
                using (var wc = NewClient()) wc.DownloadFile(url, tmp);
                int files = ApplyZip(tmp);
                string launcherNote = UpdateLauncherBinary();
                return Tuple.Create(files, launcherNote);
            }).ContinueWith(t => BeginInvoke(new Action(() =>
            {
                Cursor = Cursors.Default;
                try { File.Delete(tmp); } catch { }
                if (t.IsFaulted)
                {
                    MessageBox.Show(this, Loc.T("La actualización falló: ", "The update failed: ") + t.Exception.GetBaseException().Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (t.Result.Item2 != null) MessageBox.Show(this, t.Result.Item2, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                AfterUpdate(t.Result.Item1);
            })));
        }

        // ---- Actualizacion del propio launcher ----
        // version.json lleva "launcher": la version del launcher de ese release. Si es mayor que la de este
        // .exe, se baja WarioLauncher.exe de los archivos del release (v<launcher>), este .exe se renombra a
        // WarioLauncher.old.exe (un .exe en uso se puede renombrar, no borrar) y el nuevo ocupa su sitio; se
        // usa al volver a abrir el launcher, que borra el .old.exe al arrancar. tools\build-wario.ps1 hace lo
        // mismo para los launchers anteriores a esto (0.1.0), que no tienen este codigo.
        private static Version Norm(Version v) => new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));
        internal static Version OwnVersion => Norm(typeof(MainForm).Assembly.GetName().Version);

        // Tras cambiar el .exe del launcher: se cierra y se abre el nuevo al terminar la compilacion (o en seguida
        // si no se recompila).
        private bool _reopenAfterBuild;

        private void ReopenLauncher()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(typeof(MainForm).Assembly.Location) { WorkingDirectory = _playDir, UseShellExecute = false }); }
            catch { return; }  // si no se puede abrir, sigue este
            Close();
        }

        // Devuelve un aviso para el usuario si se cambio el launcher (o si fallo), o null si no hacia falta.
        private string UpdateLauncherBinary()
        {
            string field = LocalField("launcher");
            if (string.IsNullOrEmpty(field)) return null;
            Version want = Norm(Ver(field));
            if (want <= OwnVersion) return null;
            string self = typeof(MainForm).Assembly.Location;
            string dir = Path.GetDirectoryName(self);
            string fresh = Path.Combine(dir, "WarioLauncher.new.exe");
            string old = Path.Combine(dir, "WarioLauncher.old.exe");
            try
            {
                string url = "https://github.com/" + UpdateRepo + "/releases/download/v" + field + "/WarioLauncher.exe";
                using (var wc = NewClient()) wc.DownloadFile(url, fresh);
                byte[] head = new byte[2];
                using (var f = File.OpenRead(fresh)) f.Read(head, 0, 2);
                if (new FileInfo(fresh).Length < 100000 || head[0] != (byte)'M' || head[1] != (byte)'Z')
                    throw new InvalidDataException("not an executable");
                if (File.Exists(old)) File.Delete(old);
                File.Move(self, old);
                try { File.Move(fresh, self); }
                catch { File.Move(old, self); throw; }  // deja el launcher como estaba
                _reopenAfterBuild = true;
                return Loc.T("El launcher también se actualizó a la versión ", "The launcher was updated too, to version ") + field + ". " +
                       Loc.T("Se cerrará y se abrirá el nuevo al terminar.", "It will close and open the new one when done.");
            }
            catch (Exception ex)
            {
                try { if (File.Exists(fresh)) File.Delete(fresh); } catch { }
                return Loc.T("No se pudo actualizar el launcher (", "Could not update the launcher (") + ex.Message + "). " +
                       Loc.T("Puedes bajar el zip de la versión ", "You can download the zip of version ") + field +
                       Loc.T(" desde GitHub y copiar WarioLauncher.exe.", " from GitHub and copy WarioLauncher.exe.");
            }
        }

        private static readonly string[] Protected = { "game", "generated", "play", "toolchain", "nb-win", "nb", "build-logs", "dist", "_local", "_papelera" };

        // Copies the repository files over the install, only where the content changed (keeps timestamps for incremental builds).
        private int ApplyZip(string zipPath)
        {
            int n = 0;
            string rootFull = Path.GetFullPath(_root) + Path.DirectorySeparatorChar;
            using (var zip = ZipFile.OpenRead(zipPath))
                foreach (var e in zip.Entries)
                {
                    int slash = e.FullName.IndexOf('/');
                    if (slash < 0 || e.FullName.EndsWith("/")) continue;
                    string rel = e.FullName.Substring(slash + 1);
                    string top = rel.Split('/')[0].ToLowerInvariant();
                    if (Array.IndexOf(Protected, top) >= 0 || top == ".git" || top == ".github") continue;
                    string dest = Path.GetFullPath(Path.Combine(_root, rel.Replace('/', '\\')));
                    if (!dest.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;
                    byte[] data;
                    using (var s = e.Open()) using (var ms = new MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                    if (File.Exists(dest) && Same(dest, data)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    File.WriteAllBytes(dest, data);
                    n++;
                }
            return n;
        }

        private static bool Same(string path, byte[] data)
        {
            try { return new FileInfo(path).Length == data.Length && File.ReadAllBytes(path).AsSpanEquals(data); } catch { return false; }
        }

        private void AfterUpdate(int files)
        {
            bool quick = QuickRebuildOk();
            string msg = Loc.T("Archivos actualizados: ", "Files updated: ") + files + "\n" + Loc.T("Versión: ", "Version: ") + CodeVersion + "\n\n" +
                (quick ? Loc.T("Basta una recompilación rápida (unos minutos).", "A quick rebuild is enough (a few minutes).")
                       : Loc.T("Hay que traducir y compilar todo de nuevo (15 a 45 minutos).", "Everything must be translated and built again (15 to 45 minutes).")) +
                "\n\n" + Loc.T("¿Recompilar ahora?", "Rebuild now?");
            ShowPage(7);
            if (MessageBox.Show(this, msg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) StartBuild(quick);
            else if (_reopenAfterBuild) ReopenLauncher();
        }
    }

    internal static class ByteExt
    {
        public static bool AsSpanEquals(this byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
