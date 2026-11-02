using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WarioLauncher.Core
{
    // Importa la imagen del disco (.iso, .ciso, .wbfs, .rvz, .wia, .gcz) con nodtool (encounter/nod, MIT): la extrae
    // a una carpeta DATA (sys/ + files/) y comprueba que es Wario Land: Shake It! de EE. UU. con el main.dol exacto.
    public static class DiscTool
    {
        public static readonly string[] Patterns = { "*.iso", "*.ciso", "*.wbfs", "*.rvz", "*.wia", "*.gcz" };

        public static string NodPath(string playDir) => Path.Combine(playDir, "Tools", Platform.NodFileName);

        public static async Task DownloadNod(string playDir)
        {
            string dest = NodPath(playDir);
            string tmp = dest + ".tmp";
            await Updates.DownloadFile(Platform.NodUrl, tmp);
            if (new FileInfo(tmp).Length < 100000) { File.Delete(tmp); throw new IOException("incomplete download"); }
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(tmp, dest);
            Platform.MakeExecutable(dest);
        }

        // Id del juego del disco (p. ej. RWLE01). Lanza una excepcion con el mensaje de nodtool si no lo puede leer.
        public static async Task<string> ReadGameId(string playDir, string image)
        {
            var r = await RunTool(NodPath(playDir), "info", image);
            if (r.code != 0) throw new IOException(r.output.Trim());
            var id = Regex.Match(r.output, @"^Game ID: (\S+)", RegexOptions.Multiline);
            return id.Success ? id.Groups[1].Value : null;
        }

        public static async Task Extract(string playDir, string image, string dataDir)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dataDir));
            var r = await RunTool(NodPath(playDir), "extract", image, dataDir, "-q");
            if (r.code != 0) throw new IOException(r.output.Trim());
        }

        private static async Task<(int code, string output)> RunTool(string exe, params string[] args)
        {
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (string a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi) ?? throw new IOException("nodtool");
            Task<string> err = p.StandardError.ReadToEndAsync();
            string output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return (p.ExitCode, output + await err);
        }
    }
}
