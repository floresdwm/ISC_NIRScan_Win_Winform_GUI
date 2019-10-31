using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DLP_NIR_Win_SDK_WinForm_App_CS
{
    static class Program
    {
        /// <summary>
        /// 應用程式的主要進入點。
        /// </summary>
        [STAThread]
        static void Main()
        {
            bool isFirstOpen;
            Mutex mutex = new Mutex(false, Application.ProductName, out isFirstOpen);
            if (isFirstOpen)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainWindow());
            }
            else
            {
                Message.ShowInfo("重複開啟!");
            }
            mutex.Dispose();
        }
    }
}
