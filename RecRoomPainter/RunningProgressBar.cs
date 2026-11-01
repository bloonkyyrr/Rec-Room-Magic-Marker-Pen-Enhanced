using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static RecRoomPainter.MainForm;


namespace RecRoomPainter
{
    public partial class RunningProgressBar : Form
    {
        public RunningProgressBar()
        {
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new System.Drawing.Point(0, 0);
            this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            this.TopMost = true;
            InitializeComponent();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x20; // WS_EX_TRANSPARENT makes the window click-through.
                return cp;
            }
        }

        // Compact H:MM:SS / M:SS text for the small overlay.
        private static string ShortTime(int milliseconds)
        {
            TimeSpan span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
                : $"{span.Minutes}:{span.Seconds:00}";
        }

        public void UpdateProgress(int currentValue, int endValue, int time, int eta = -1, bool paused = false)
        {
            string elapsed;
            try
            {
                elapsed = ShortTime(time);
            }
            catch
            {
                elapsed = "0:00";
            }

            // Second line: a self-correcting ETA, or the paused state.
            timeLabel.Text = paused
                ? $"{elapsed}   PAUSED"
                : $"{elapsed}   ETA {(eta > 0 ? ShortTime(eta) : "--")}";
            try
            {
                int percent = (int)((double)(endValue - currentValue) / endValue * 100);
                runningProgressBar1.Value = percent;
                runningLabel.Text = $"{percent}%\n{(endValue - currentValue)}/{endValue}";
            }
            catch
            {
                runningProgressBar1.Value = 0;
                runningLabel.Text = $"0%\n0/0";
            }
        }
    }
}

