
namespace PixelPlanetBot.Captcha
{
    partial class CaptchaForm
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
            this.captchaPictureBox = new System.Windows.Forms.PictureBox();
            this.solutionTextBox = new System.Windows.Forms.TextBox();
            this.sendButton = new System.Windows.Forms.Button();
            this.getButton = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.captchaPictureBox)).BeginInit();
            this.SuspendLayout();
            // 
            // captchaPictureBox
            // 
            this.captchaPictureBox.Dock = System.Windows.Forms.DockStyle.Top;
            this.captchaPictureBox.Location = new System.Drawing.Point(0, 0);
            this.captchaPictureBox.Name = "captchaPictureBox";
            this.captchaPictureBox.Size = new System.Drawing.Size(500, 300);
            this.captchaPictureBox.TabIndex = 0;
            this.captchaPictureBox.TabStop = false;
            // 
            // solutionTextBox
            // 
            this.solutionTextBox.Location = new System.Drawing.Point(12, 330);
            this.solutionTextBox.Name = "solutionTextBox";
            this.solutionTextBox.Size = new System.Drawing.Size(100, 20);
            this.solutionTextBox.TabIndex = 1;
            // 
            // sendButton
            // 
            this.sendButton.Location = new System.Drawing.Point(118, 306);
            this.sendButton.Name = "sendButton";
            this.sendButton.Size = new System.Drawing.Size(100, 49);
            this.sendButton.TabIndex = 2;
            this.sendButton.Text = "Send";
            this.sendButton.UseVisualStyleBackColor = true;
            this.sendButton.Click += new System.EventHandler(this.SendButtonClick);
            // 
            // getButton
            // 
            this.getButton.Location = new System.Drawing.Point(388, 306);
            this.getButton.Name = "getButton";
            this.getButton.Size = new System.Drawing.Size(100, 49);
            this.getButton.TabIndex = 2;
            this.getButton.Text = "Get new captcha";
            this.getButton.UseVisualStyleBackColor = true;
            this.getButton.Click += new System.EventHandler(this.GetButtonClick);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(12, 314);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(28, 13);
            this.label1.TabIndex = 3;
            this.label1.Text = "Text";
            // 
            // CaptchaForm
            // 
            this.AcceptButton = this.sendButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(500, 362);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.getButton);
            this.Controls.Add(this.sendButton);
            this.Controls.Add(this.solutionTextBox);
            this.Controls.Add(this.captchaPictureBox);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "CaptchaForm";
            this.ShowIcon = false;
            this.ShowInTaskbar = false;
            this.Text = "Captcha";
            this.Load += new System.EventHandler(this.FormLoad);
            ((System.ComponentModel.ISupportInitialize)(this.captchaPictureBox)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.PictureBox captchaPictureBox;
        private System.Windows.Forms.TextBox solutionTextBox;
        private System.Windows.Forms.Button sendButton;
        private System.Windows.Forms.Button getButton;
        private System.Windows.Forms.Label label1;
    }
}

