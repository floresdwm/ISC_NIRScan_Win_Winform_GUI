using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DLP_NIR_Win_SDK_WinForm_App_CS
{
    public partial class ProgressBar : Form
    {
        public ProgressBar(String title, String Content)
        {
            InitializeComponent();
            this.Text = title;
            label1.Text = Content;
        }

        private void button_cancel_Click(object sender, EventArgs e)
        {
            MainWindow.UserCancelScan = true;
        }
    }
}
