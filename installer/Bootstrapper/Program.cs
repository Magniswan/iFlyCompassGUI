using System;
using System.Security.Principal;
using System.Windows.Forms;

namespace iFlyCompassGUI.Bootstrapper
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (!IsRunningAsAdmin())
            {
                MessageBox.Show(
                    "安装程序需要管理员权限运行，请右键选择\"以管理员身份运行\"。",
                    "权限不足",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Application.Run(new MainForm());
        }

        private static bool IsRunningAsAdmin()
        {
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
