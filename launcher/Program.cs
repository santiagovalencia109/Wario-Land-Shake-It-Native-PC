using System;
using System.Drawing;
using System.Windows.Forms;

namespace WarioLauncher
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // Tras una actualizacion del propio launcher, el anterior queda renombrado a WarioLauncher.old.exe
            // (un .exe en uso se puede renombrar pero no borrar); aqui ya no esta en uso.
            try
            {
                string old = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WarioLauncher.old.exe");
                if (System.IO.File.Exists(old)) System.IO.File.Delete(old);
            }
            catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Dpi.Factor = Math.Max(1f, g.DpiX / 96f);
            Application.Run(new MainForm());
        }
    }
}
