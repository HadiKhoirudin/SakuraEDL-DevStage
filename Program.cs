using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LoveAlways
{
    internal static class Program
    {
        /// <summary>
        /// 应用程序的主入口点。
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 显示启动动画（Splash）窗体，关闭后再启动主窗体
            using (var splash = new SplashForm())
            {
                splash.ShowDialog();
            }

            Application.Run(new Form1());
        }

    }
}
