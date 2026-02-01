namespace LoveAlways
{
    partial class SplashForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.uiProcessBar1 = new Sunny.UI.UIProcessBar();
            this.uiLabelStatus = new Sunny.UI.UILabel();
            this.uiLedLabel1 = new Sunny.UI.UILedLabel();
            this.SuspendLayout();
            // 
            // uiProcessBar1
            // 
            this.uiProcessBar1.BackColor = System.Drawing.Color.Transparent;
            this.uiProcessBar1.FillColor = System.Drawing.Color.White;
            this.uiProcessBar1.Font = new System.Drawing.Font("Segoe UI", 10.5F);
            this.uiProcessBar1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(192)))), ((int)(((byte)(255)))));
            this.uiProcessBar1.Location = new System.Drawing.Point(12, 268);
            this.uiProcessBar1.MinimumSize = new System.Drawing.Size(3, 3);
            this.uiProcessBar1.Name = "uiProcessBar1";
            this.uiProcessBar1.Radius = 14;
            this.uiProcessBar1.RectColor = System.Drawing.Color.FromArgb(((int)(((byte)(192)))), ((int)(((byte)(192)))), ((int)(((byte)(255)))));
            this.uiProcessBar1.Size = new System.Drawing.Size(496, 27);
            this.uiProcessBar1.TabIndex = 0;
            this.uiProcessBar1.Text = "uiProcessBar1";
            this.uiProcessBar1.ValueChanged += new Sunny.UI.UIProcessBar.OnValueChanged(this.uiProcessBar1_ValueChanged);
            // 
            // uiLabelStatus
            // 
            this.uiLabelStatus.BackColor = System.Drawing.Color.Transparent;
            this.uiLabelStatus.Font = new System.Drawing.Font("Segoe UI", 10.5F);
            this.uiLabelStatus.ForeColor = System.Drawing.Color.Gray;
            this.uiLabelStatus.Location = new System.Drawing.Point(12, 241);
            this.uiLabelStatus.Name = "uiLabelStatus";
            this.uiLabelStatus.Size = new System.Drawing.Size(235, 24);
            this.uiLabelStatus.TabIndex = 1;
            this.uiLabelStatus.Text = "Initializing...";
            this.uiLabelStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // uiLedLabel1
            // 
            this.uiLedLabel1.BackColor = System.Drawing.Color.Transparent;
            this.uiLedLabel1.Font = new System.Drawing.Font("Segoe UI", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.uiLedLabel1.Location = new System.Drawing.Point(-1, 12);
            this.uiLedLabel1.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiLedLabel1.Name = "uiLedLabel1";
            this.uiLedLabel1.Size = new System.Drawing.Size(522, 298);
            this.uiLedLabel1.TabIndex = 2;
            this.uiLedLabel1.Text = "MultiFlash";
            // 
            // SplashForm
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = System.Drawing.Color.White;
            this.ClientSize = new System.Drawing.Size(520, 310);
            this.Controls.Add(this.uiProcessBar1);
            this.Controls.Add(this.uiLabelStatus);
            this.Controls.Add(this.uiLedLabel1);
            this.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.Name = "SplashForm";
            this.Opacity = 0.85D;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.TopMost = true;
            this.TransparencyKey = System.Drawing.Color.Red;
            this.ResumeLayout(false);

        }

        #endregion

        private Sunny.UI.UIProcessBar uiProcessBar1;
        private Sunny.UI.UILabel uiLabelStatus;
        private Sunny.UI.UILedLabel uiLedLabel1;
    }
}
