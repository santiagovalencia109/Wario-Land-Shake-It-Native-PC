using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace WarioLauncher.Core
{
    // Rutas y estado de la instalacion del port: donde esta el codigo (tools/), la carpeta Play, el juego
    // compilado, Config.toml, las partidas y sus copias, y las versiones (version.json / built_version.txt).
    public sealed class Port
    {
        public const string GameId = "RWLE01";
        public const string MainDolSha256 = "053BE60DDE84C8042F45ED2F4BA80C17EFB2D20FEDF95A414C2C7E37D336912A";
        private static readonly string SaveSubPath = Path.Combine("title", "00010000", "52574c45", "data");

        public readonly string BaseDir;
        public readonly string Root;          // carpeta del port (tiene tools/), o null si el lanzador esta suelto
        public readonly TomlFile Config;
        public readonly LauncherSettings Settings;

        public Port(string baseDir)
        {
            BaseDir = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Root = FindRoot(BaseDir);
            Config = new TomlFile(ConfigPath);
            Settings = new LauncherSettings(Path.Combine(UserData, "launcher.ini"));
        }

        // El lanzador esta en la raiz del port (junto a tools/) o en Play/, junto al juego. Se buscan unas
        // carpetas mas arriba para que tambien funcione al probarlo con "dotnet run" (launcher-avalonia/bin/...).
        private static string FindRoot(string dir)
        {
            for (string c = dir; !string.IsNullOrEmpty(c) && dir.Length - c.Length < 400; c = Path.GetDirectoryName(c))
            {
                if (File.Exists(Path.Combine(c, "tools", Platform.BuildScriptName))) return c;
                if (dir.Split(Path.DirectorySeparatorChar).Length - c.Split(Path.DirectorySeparatorChar).Length >= 5) break;
            }
            return null;
        }

        public string PlayDir => Root != null ? Path.Combine(Root, "Play") : BaseDir;
        public string GameExe => Path.Combine(PlayDir, Platform.GameExeName);
        public string UserData => Path.Combine(PlayDir, "UserData");
        public string ConfigPath => Path.Combine(UserData, "Config.toml");
        public string ShortIntroFlag => Path.Combine(PlayDir, "short_intro.txt");
        public string BackupDir => Path.Combine(UserData, "SaveBackups");
        public string LogsDir => Path.Combine(UserData, "Logs");
        public string StatusFile => Root == null ? null : Path.Combine(Root, "build-logs", "status.txt");
        public string BuildLogsDir => Root == null ? null : Path.Combine(Root, "build-logs");
        public string LocalManifest => Root == null ? null : Path.Combine(Root, "version.json");
        public string BuiltFile => Path.Combine(PlayDir, "built_version.txt");
        public string UpdateRepo => Settings.Get("update_repo", "santiagovalencia109/Wario-Land-Shake-It-Native-PC");

        public bool GameBuilt => File.Exists(GameExe);

        // Instalado = juego valido configurado y el programa compilado.
        public bool IsInstalled()
        {
            if (!GameBuilt) return false;
            Config.Reload();
            return GameFolderOk(Config.GetString("paths", "dvd_root", ""), out _);
        }

        // ---- el juego del usuario ----

        public static bool GameFolderOk(string dir, out string why)
        {
            why = "";
            if (string.IsNullOrWhiteSpace(dir)) { why = Loc.T("No has elegido la carpeta del juego.", "No game folder selected."); return false; }
            if (!Directory.Exists(dir)) { why = Loc.T("La carpeta del juego no existe: ", "The game folder does not exist: ") + dir; return false; }
            string dol = Path.Combine(dir, "sys", "main.dol");
            if (!File.Exists(dol) || !Directory.Exists(Path.Combine(dir, "files")))
            {
                why = Loc.T("Esa carpeta no parece el juego extraído (falta sys/main.dol o files/).", "That folder does not look like the extracted game (no sys/main.dol or files/).");
                return false;
            }
            string boot = Path.Combine(dir, "sys", "boot.bin");
            if (File.Exists(boot))
            {
                try
                {
                    var id = new byte[6];
                    using (var f = File.OpenRead(boot)) f.ReadExactly(id, 0, 6);
                    string gid = Encoding.ASCII.GetString(id);
                    if (gid != GameId) { why = Loc.T("El juego es ", "The game is ") + gid + Loc.T("; este port es para RWLE01 (EE. UU.).", "; this port is for RWLE01 (US)."); return false; }
                }
                catch { }
            }
            if (!MainDolMatches(dir))
            {
                why = Loc.T("El main.dol de esa carpeta no es el de RWLE01 sin modificar que usa el port.",
                            "That folder's main.dol is not the unmodified RWLE01 one the port uses.");
                return false;
            }
            return true;
        }

        public static bool MainDolMatches(string data)
        {
            string dol = Path.Combine(data, "sys", "main.dol");
            if (!File.Exists(dol)) return false;
            try
            {
                using var f = File.OpenRead(dol);
                return Convert.ToHexString(SHA256.HashData(f)) == MainDolSha256;
            }
            catch { return false; }
        }

        // Si eligen la carpeta de encima (con DATA dentro), se usa DATA.
        public static string NormalizeGameFolder(string dir)
        {
            if (!File.Exists(Path.Combine(dir, "sys", "main.dol")) && File.Exists(Path.Combine(dir, "DATA", "sys", "main.dol")))
                return Path.Combine(dir, "DATA");
            return dir;
        }

        // ---- partidas ----

        public string SaveDir()
        {
            string nand = Config.GetString("paths", "nand_root", "");
            string root = string.IsNullOrWhiteSpace(nand) ? Path.Combine(UserData, "NAND") : nand;
            return Path.Combine(root, SaveSubPath);
        }

        public List<string> ListBackups()
        {
            if (!Directory.Exists(BackupDir)) return new List<string>();
            return Directory.GetDirectories(BackupDir).OrderByDescending(d => d).ToList();
        }

        public static string BackupLabel(string dir)
        {
            string name = Path.GetFileName(dir);
            return DateTime.TryParseExact(name, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime t)
                ? t.ToString("yyyy-MM-dd  HH:mm:ss") : name;
        }

        public void BackupSaves()
        {
            try
            {
                string src = SaveDir();
                if (!Directory.Exists(src) || Directory.GetFiles(src).Length == 0) return;
                CopyDir(src, Path.Combine(BackupDir, DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                foreach (string old in ListBackups().Skip(10))
                    try { Directory.Delete(old, true); } catch { }
            }
            catch { }
        }

        public void RestoreBackup(string backup)
        {
            BackupSaves();
            string dst = SaveDir();
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(dst)) File.Delete(f);
            CopyDir(backup, dst);
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        public string LatestLog()
        {
            if (!Directory.Exists(LogsDir)) return null;
            return Directory.GetFiles(LogsDir, "console.log", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }

        // Lee un archivo que otro proceso puede estar escribiendo.
        public static string ReadShared(string file, int maxChars = 0)
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                string text = sr.ReadToEnd();
                if (maxChars > 0 && text.Length > maxChars) text = "…\n" + text.Substring(text.Length - maxChars);
                return text;
            }
            catch { return ""; }
        }

        // ---- versiones ----

        public static string JsonField(string json, string name)
        {
            var m = Regex.Match(json ?? "", "\"" + name + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static Version Ver(string s) => Version.TryParse(s ?? "", out Version v) ? v : new Version(0, 0, 0);

        public static Version Norm(Version v) => new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));

        public string LocalField(string name)
        {
            try { return LocalManifest != null && File.Exists(LocalManifest) ? JsonField(File.ReadAllText(LocalManifest), name) : null; } catch { return null; }
        }

        public Version CodeVersion => Ver(LocalField("version"));

        // Version del codigo con que se compilo el programa actual.
        public Version BuiltVersion
        {
            get
            {
                try
                {
                    if (File.Exists(BuiltFile)) return Ver(File.ReadAllText(BuiltFile).Trim());
                    if (GameBuilt) { SaveBuiltVersion(); return CodeVersion; }
                }
                catch { }
                return new Version(0, 0, 0);
            }
        }

        public void SaveBuiltVersion()
        {
            try { Directory.CreateDirectory(PlayDir); File.WriteAllText(BuiltFile, CodeVersion.ToString()); } catch { }
        }

        public bool NeedsRebuild => GameBuilt && BuiltVersion < CodeVersion;

        public bool CanQuickRebuild => Root != null
            && Directory.Exists(Path.Combine(Root, Platform.NativeBuildDirName)) && Directory.Exists(Path.Combine(Root, "generated"));

        // Basta una recompilacion rapida cuando lo instalado es al menos la version de quick_from.
        public bool QuickRebuildOk()
        {
            string qf = LocalField("quick_from");
            return qf != null && BuiltVersion >= Ver(qf) && CanQuickRebuild;
        }
    }
}
