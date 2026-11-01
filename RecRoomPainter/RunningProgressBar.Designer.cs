namespace RecRoomPainter
{
    partial class RunningProgressBar
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
            tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
            timeLabel = new System.Windows.Forms.Label();
            runningProgressBar1 = new System.Windows.Forms.ProgressBar();
            runningLabel = new System.Windows.Forms.Label();
            tableLayoutPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.CausesValidation = false;
            tableLayoutPanel1.ColumnCount = 2;
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 87F));
            tableLayoutPanel1.Controls.Add(timeLabel, 0, 1);
            tableLayoutPanel1.Controls.Add(runningProgressBar1, 0, 0);
            tableLayoutPanel1.Controls.Add(runningLabel, 1, 0);
            tableLayoutPanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            tableLayoutPanel1.ImeMode = System.Windows.Forms.ImeMode.Off;
            tableLayoutPanel1.Location = new System.Drawing.Point(0, 0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.RowCount = 2;
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 52F));
            tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 48F));
            tableLayoutPanel1.Size = new System.Drawing.Size(260, 66);
            tableLayoutPanel1.TabIndex = 0;
            // 
            // timeLabel
            // 
            timeLabel.AutoSize = false;
            timeLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            timeLabel.ForeColor = System.Drawing.SystemColors.HighlightText;
            timeLabel.Location = new System.Drawing.Point(3, 34);
            timeLabel.Name = "timeLabel";
            timeLabel.Size = new System.Drawing.Size(167, 32);
            timeLabel.TabIndex = 2;
            timeLabel.Text = "0:00   ETA --";
            timeLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // runningProgressBar1
            // 
            runningProgressBar1.Dock = System.Windows.Forms.DockStyle.Fill;
            runningProgressBar1.Location = new System.Drawing.Point(3, 3);
            runningProgressBar1.Name = "runningProgressBar1";
            runningProgressBar1.Size = new System.Drawing.Size(167, 23);
            runningProgressBar1.TabIndex = 0;
            runningProgressBar1.Value = 50;
            // 
            // runningLabel
            // 
            runningLabel.AutoSize = true;
            runningLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            runningLabel.ForeColor = System.Drawing.SystemColors.HighlightText;
            runningLabel.Location = new System.Drawing.Point(176, 0);
            runningLabel.Name = "runningLabel";
            runningLabel.Size = new System.Drawing.Size(81, 29);
            runningLabel.TabIndex = 1;
            runningLabel.Text = "0/0";
            runningLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            // 
            // RunningProgressBar
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            BackColor = System.Drawing.SystemColors.ActiveCaptionText;
            ClientSize = new System.Drawing.Size(260, 66);
            Controls.Add(tableLayoutPanel1);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            Name = "RunningProgressBar";
            Text = "Form1";
            tableLayoutPanel1.ResumeLayout(false);
            tableLayoutPanel1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        public System.Windows.Forms.ProgressBar runningProgressBar1;
        public System.Windows.Forms.Label runningLabel;
        private System.Windows.Forms.ProgressBar progressBar1;
        public System.Windows.Forms.Label timeLabel;
    }
}