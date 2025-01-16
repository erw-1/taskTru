using System;
using System.Windows.Forms;

namespace taskTrough
{
    /// <summary>
    /// Standard WinForms entry point. Runs the <see cref="MainForm"/>.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
