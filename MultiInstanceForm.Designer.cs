namespace LoveAlways
{
    partial class MultiInstanceForm
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
            this.uiLabelMessage = new Sunny.UI.UILabel();
            this.uiButtonClose = new Sunny.UI.UIButton();
            this.SuspendLayout();
            // 
            // uiLabelMessage
            // 
            this.uiLabelMessage.Font = new System.Drawing.Font("Segoe UI", 12F);
            this.uiLabelMessage.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(48)))), ((int)(((byte)(48)))), ((int)(((byte)(48)))));
            this.uiLabelMessage.Location = new System.Drawing.Point(15, 49);
            this.uiLabelMessage.Name = "uiLabelMessage";
            this.uiLabelMessage.Size = new System.Drawing.Size(352, 134);
            this.uiLabelMessage.TabIndex = 0;
            this.uiLabelMessage.Text = "Multiple instances detected, please close other programs";
            this.uiLabelMessage.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            // 
            // uiButtonClose
            // 
            this.uiButtonClose.Cursor = System.Windows.Forms.Cursors.Hand;
            this.uiButtonClose.FillColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.uiButtonClose.FillColor2 = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.uiButtonClose.FillHoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(115)))), ((int)(((byte)(115)))));
            this.uiButtonClose.FillPressColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.uiButtonClose.FillSelectedColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.uiButtonClose.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.uiButtonClose.LightColor = System.Drawing.Color.FromArgb(((int)(((byte)(253)))), ((int)(((byte)(243)))), ((int)(((byte)(243)))));
            this.uiButtonClose.Location = new System.Drawing.Point(44, 200);
            this.uiButtonClose.MinimumSize = new System.Drawing.Size(1, 1);
            this.uiButtonClose.Name = "uiButtonClose";
            this.uiButtonClose.Radius = 14;
            this.uiButtonClose.RectColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.uiButtonClose.RectHoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(115)))), ((int)(((byte)(115)))));
            this.uiButtonClose.RectPressColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.uiButtonClose.RectSelectedColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.uiButtonClose.Size = new System.Drawing.Size(296, 40);
            this.uiButtonClose.Style = Sunny.UI.UIStyle.Custom;
            this.uiButtonClose.TabIndex = 1;
            this.uiButtonClose.Text = "Close";
            this.uiButtonClose.TipsColor = System.Drawing.Color.Salmon;
            this.uiButtonClose.TipsFont = new System.Drawing.Font("宋体", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.uiButtonClose.Click += new System.EventHandler(this.uiButtonClose_Click);
            // 
            // MultiInstanceForm
            // 
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.ClientSize = new System.Drawing.Size(389, 259);
            this.ControlBox = false;
            this.Controls.Add(this.uiLabelMessage);
            this.Controls.Add(this.uiButtonClose);
            this.Font = new System.Drawing.Font("Segoe UI", 10F);
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "MultiInstanceForm";
            this.RectColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Tip";
            this.TitleColor = System.Drawing.Color.FromArgb(((int)(((byte)(255)))), ((int)(((byte)(128)))), ((int)(((byte)(128)))));
            this.TitleFont = new System.Drawing.Font("Segoe UI", 10.5F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.TopMost = true;
            this.ZoomScaleRect = new System.Drawing.Rectangle(15, 15, 400, 300);
            this.ResumeLayout(false);

        }

        #endregion

        private Sunny.UI.UILabel uiLabelMessage;
        private Sunny.UI.UIButton uiButtonClose;
    }
}
