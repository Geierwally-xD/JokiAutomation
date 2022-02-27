
using System.Windows.Forms;

namespace JokiAutomation
{
    partial class Autozoom
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
            this.zoomValue = new System.Windows.Forms.TextBox();
            this.buttonZoom = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // 
            // zoomValue
            // 
            this.zoomValue.Font = new System.Drawing.Font("Microsoft Sans Serif", 16.1F);
            this.zoomValue.Location = new System.Drawing.Point(85, 23);
            this.zoomValue.Name = "zoomValue";
            this.zoomValue.Size = new System.Drawing.Size(107, 68);
            this.zoomValue.TabIndex = 0;
            this.zoomValue.Text = "100";
            this.zoomValue.TextChanged += new System.EventHandler(this.zoomValue_TextChanged);
            // 
            // buttonZoom
            // 
            this.buttonZoom.Font = new System.Drawing.Font("Microsoft Sans Serif", 14.1F);
            this.buttonZoom.Location = new System.Drawing.Point(58, 126);
            this.buttonZoom.Name = "buttonZoom";
            this.buttonZoom.Size = new System.Drawing.Size(170, 67);
            this.buttonZoom.TabIndex = 1;
            this.buttonZoom.Text = "OK";
            this.buttonZoom.UseVisualStyleBackColor = true;
            this.buttonZoom.Click += new System.EventHandler(this.buttonZoom_Click);
            // 
            // Autozoom
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(16F, 31F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(288, 232);
            this.Controls.Add(this.buttonZoom);
            this.Controls.Add(this.zoomValue);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "Autozoom";
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "Autozoom";
            this.TopMost = true;
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        public System.Windows.Forms.TextBox zoomValue;
        private System.Windows.Forms.Button buttonZoom;
    }
}