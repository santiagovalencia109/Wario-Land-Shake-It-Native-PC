using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace WarioLauncher.Core
{
    // Lo que cambia entre Windows y Linux: nombres de programas, scripts de compilacion y como abrir carpetas.
    public static class Platform
    {
        public static bool IsWindows => OperatingSystem.IsWindows();

        // El juego compilado (Play/WarioRecomp.exe en Windows, Play/WarioRecomp en Linux).
        public static string GameExeName => IsWindows ? "WarioRecomp.exe" : "WarioRecomp";

        // Script de compilacion del port (tools/).
        public static string BuildScriptName => IsWindows ? "build-wario.ps1" : "build-wario.sh";

        // Carpeta de compilacion nativa que deja el script (la compilacion rapida la necesita).
        public static string NativeBuildDirName => IsWindows ? "nb-win" : "nb-linux";

        // nodtool (encounter/nod, MIT): lee las imagenes de disco de Wii.
        public const string NodVersion = "v2.0.0-alpha.10";
        public static string NodAsset => IsWindows ? "nodtool-windows-x86_64.exe"
            : RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "nodtool-linux-aarch64" : "nodtool-linux-x86_64";
        public static string NodUrl => "https://github.com/encounter/nod/releases/download/" + NodVersion + "/" + NodAsset;
        public static string NodFileName => IsWindows ? "nodtool.exe" : "nodtool";

        // El lanzador como archivo del release (autoactualizacion): un solo ejecutable por sistema.
        public static string LauncherAssetName => IsWindows ? "WarioLauncher.exe" : "WarioLauncher-linux-x64";
        public static string LauncherFileName => IsWindows ? "WarioLauncher.exe" : "WarioLauncher";

        // Proceso que ejecuta el script de compilacion. args: opciones comunes ya traducidas.
        public static ProcessStartInfo BuildProcess(string root, bool quick, bool english)
        {
            string script = Path.Combine(root, "tools", BuildScriptName);
            ProcessStartInfo psi;
            if (IsWindows)
            {
                string ps = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                string args = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -SkipRun -Lang " + (english ? "en" : "es");
                if (quick) args += " -SkipTools -SkipTranslate";
                psi = new ProcessStartInfo(ps, args);
            }
            else
            {
                psi = new ProcessStartInfo("bash");
                psi.ArgumentList.Add(script);
                psi.ArgumentList.Add("--lang");
                psi.ArgumentList.Add(english ? "en" : "es");
                if (quick) psi.ArgumentList.Add("--skip-translate");
            }
            psi.WorkingDirectory = root;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            return psi;
        }

        public static void OpenFolder(string dir)
        {
            try
            {
                Directory.CreateDirectory(dir);
                if (IsWindows) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo("xdg-open", "\"" + dir + "\"") { UseShellExecute = false });
            }
            catch { }
        }

        public static void OpenFile(string file)
        {
            try
            {
                if (!File.Exists(file)) File.WriteAllText(file, "");
                if (IsWindows) Process.Start(new ProcessStartInfo("notepad.exe", "\"" + file + "\"") { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo("xdg-open", "\"" + file + "\"") { UseShellExecute = false });
            }
            catch { }
        }

        // En Linux un archivo descargado necesita el permiso de ejecucion.
        public static void MakeExecutable(string file)
        {
            if (OperatingSystem.IsWindows()) return;
            try
            {
                File.SetUnixFileMode(file, File.GetUnixFileMode(file) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            catch { }
        }

        // Acceso directo: .lnk en el escritorio (Windows) o .desktop en el menu de aplicaciones (Linux).
        // Devuelve null si salio bien, o el error.
        public static string CreateShortcut(string launcherPath, string workDir, string iconPath)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                    string lnk = Path.Combine(desktop, "Wario Land Shake It!.lnk");
                    Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType == null) return "WScript.Shell";
                    dynamic shell = Activator.CreateInstance(shellType);
                    dynamic sc = shell.CreateShortcut(lnk);
                    sc.TargetPath = launcherPath;
                    sc.WorkingDirectory = workDir;
                    sc.IconLocation = iconPath + ",0";
                    sc.Description = "Wario Land: Shake It! (WarioRecomp)";
                    sc.Save();
                }
                else
                {
                    string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications");
                    Directory.CreateDirectory(apps);
                    string file = Path.Combine(apps, "wariorecomp.desktop");
                    File.WriteAllText(file,
                        "[Desktop Entry]\nType=Application\nName=Wario Land: Shake It! (WarioRecomp)\n" +
                        "Exec=\"" + launcherPath + "\"\nPath=" + workDir + "\n" +
                        (File.Exists(iconPath) ? "Icon=" + iconPath + "\n" : "") + "Terminal=false\nCategories=Game;\n");
                    MakeExecutable(file);
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }
}
