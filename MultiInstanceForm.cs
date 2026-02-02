using Sunny.UI;
using System;
using System.Windows.Forms;

namespace LoveAlways
{
    public partial class MultiInstanceForm : UIForm
    {
        public MultiInstanceForm()
        {
            InitializeComponent();
        }

        public string Message
        {
            get => uiLabelMessage.Text;
            set => uiLabelMessage.Text = value;
        }

        private void uiButtonClose_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
