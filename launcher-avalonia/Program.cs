using System;
using System.IO;
using Avalonia;

namespace WarioLauncher
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Tras una actualizacion del propio lanzador, el anterior queda renombrado a *.old (un ejecutable en uso
            // se puede renombrar pero no borrar); aqui ya no esta en uso.
            try
            {
                string self = Environment.ProcessPath;
                if (self != null)
                {
                    string old = Core.Updates.OldExePath(self);
                    if (File.Exists(old)) File.Delete(old);
                    // Lo que dejaba el lanzador anterior (WinForms, .NET Framework).
                    string config = Path.Combine(Path.GetDirectoryName(self), "WarioLauncher.exe.config");
                    if (OperatingSystem.IsWindows() && File.Exists(config)) File.Delete(config);
                }
            }
            catch { }
            // Cualquier error que cierre el programa queda en launcher-error.log, junto al ejecutable.
            AppDomain.CurrentDomain.UnhandledException += (s, e) => LogCrash(e.ExceptionObject as Exception, "AppDomain");
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) => { LogCrash(e.Exception, "Task"); e.SetObserved(); };
            try { BuildAvaloniaApp().StartWithClassicDesktopLifetime(args); }
            catch (Exception ex) { LogCrash(ex, "Main"); throw; }
            LogCrash(null, "fin normal (la ventana se cerro)");
        }

        public static void LogCrash(Exception ex, string where)
        {
            try
            {
                string log = Path.Combine(AppContext.BaseDirectory, "launcher-error.log");
                File.AppendAllText(log, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [" + where + "]  " + (ex?.ToString() ?? "") + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<UI.App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}
