using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace WarioLauncher.Core
{
    // Actualizaciones: el repositorio tiene version.json en la raiz. Se compara con el local, se baja el codigo
    // (zip del repositorio, sin nada del juego) sobre la instalacion y se recompila. El propio lanzador se
    // actualiza desde los archivos del release (v<launcher>): un solo ejecutable por sistema.
    public static class Updates
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("WarioLauncher");
            c.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            return c;
        }

        // Version de este lanzador (la del .csproj).
        public static Version OwnVersion => Port.Norm(typeof(Updates).Assembly.GetName().Version ?? new Version(0, 0, 0));

        // Ruta del ejecutable en uso (con publicacion de un solo archivo, Assembly.Location esta vacia).
        public static string OwnExe => Environment.ProcessPath;

        // Manifiesto remoto, o null si no se puede consultar.
        public static async Task<string> FetchRemoteManifest(string repo)
        {
            try { return await Http.GetStringAsync("https://raw.githubusercontent.com/" + repo + "/main/version.json"); }
            catch { return null; }
        }

        public static async Task DownloadFile(string url, string dest)
        {
            string dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(dest);
            await src.CopyToAsync(dst);
        }

        // Baja el codigo de main y lo copia sobre la instalacion. Devuelve cuantos archivos cambiaron.
        public static async Task<int> DownloadAndApplyCode(Port port)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "WarioRecomp-update.zip");
            try
            {
                await DownloadFile("https://github.com/" + port.UpdateRepo + "/archive/refs/heads/main.zip", tmp);
                return await Task.Run(() => ApplyZip(port.Root, tmp));
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        private static readonly string[] Protected =
            { "game", "generated", "play", "toolchain", "nb-win", "nb-linux", "nb", "build-logs", "dist", "_local", "_papelera" };

        // Copia los archivos del repositorio sobre la instalacion, solo los que cambiaron (conserva las fechas para
        // la compilacion incremental).
        public static int ApplyZip(string root, string zipPath)
        {
            int n = 0;
            string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var cmp = Platform.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var e in zip.Entries)
            {
                int slash = e.FullName.IndexOf('/');
                if (slash < 0 || e.FullName.EndsWith("/")) continue;
                string rel = e.FullName.Substring(slash + 1);
                string top = rel.Split('/')[0].ToLowerInvariant();
                if (Array.IndexOf(Protected, top) >= 0 || top == ".git" || top == ".github") continue;
                string dest = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!dest.StartsWith(rootFull, cmp)) continue;
                byte[] data;
                using (var s = e.Open())
                using (var ms = new MemoryStream()) { s.CopyTo(ms); data = ms.ToArray(); }
                if (File.Exists(dest) && Same(dest, data)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.WriteAllBytes(dest, data);
                if (dest.EndsWith(".sh", StringComparison.Ordinal)) Platform.MakeExecutable(dest);
                n++;
            }
            return n;
        }

        private static bool Same(string path, byte[] data)
        {
            try { return new FileInfo(path).Length == data.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(data); } catch { return false; }
        }

        // ---- el propio lanzador ----
        // version.json "launcher" = version del lanzador de ese release. Si es mayor que la de este, se baja el
        // ejecutable del release (v<launcher>), este se renombra a *.old (un ejecutable en uso se puede renombrar,
        // no borrar) y el nuevo ocupa su sitio. Program borra el .old al arrancar.
        // Devuelve true si se cambio; lanza una excepcion si fallo.
        public static async Task<bool> UpdateLauncherBinary(Port port)
        {
            string field = port.LocalField("launcher");
            if (string.IsNullOrEmpty(field)) return false;
            if (Port.Norm(Port.Ver(field)) <= OwnVersion) return false;
            string self = OwnExe;
            if (string.IsNullOrEmpty(self)) return false;
            string dir = Path.GetDirectoryName(self);
            string fresh = Path.Combine(dir, Platform.LauncherFileName + ".new");
            string old = OldExePath(self);
            try
            {
                await DownloadFile("https://github.com/" + port.UpdateRepo + "/releases/download/v" + field + "/" + Platform.LauncherAssetName, fresh);
                byte[] head = new byte[4];
                using (var f = File.OpenRead(fresh)) f.ReadExactly(head, 0, 4);
                bool exe = Platform.IsWindows ? head[0] == 'M' && head[1] == 'Z' : head[0] == 0x7F && head[1] == 'E' && head[2] == 'L' && head[3] == 'F';
                if (new FileInfo(fresh).Length < 100000 || !exe) throw new InvalidDataException("not an executable");
                if (File.Exists(old)) File.Delete(old);
                File.Move(self, old);
                try { File.Move(fresh, self); }
                catch { File.Move(old, self); throw; }  // deja el lanzador como estaba
                Platform.MakeExecutable(self);
                return true;
            }
            finally { try { if (File.Exists(fresh)) File.Delete(fresh); } catch { } }
        }

        public static string OldExePath(string self) =>
            Platform.IsWindows ? Path.Combine(Path.GetDirectoryName(self), "WarioLauncher.old.exe") : self + ".old";
    }
}
