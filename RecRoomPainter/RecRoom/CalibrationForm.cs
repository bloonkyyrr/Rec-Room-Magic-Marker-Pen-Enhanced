using System;
using System.Drawing;
using System.Windows.Forms;

namespace RecRoomPainter
{
    public sealed class CalibrationForm : Form
    {
        private readonly Label _instruction;
        private readonly Label _countdown;
        private readonly Button _capture;
        private readonly Button _skip;
        private readonly Timer _timer;
        private int _secondsLeft = 5;
        public Point Captured { get; private set; }
        public bool Skipped { get; private set; }

        public CalibrationForm(string title, string instruction)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(20, 20);
            MinimizeBox = false;
            MaximizeBox = false;
            TopMost = true;
            ClientSize = new Size(560, 150);
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.White;
            ShowInTaskbar = false;

            _instruction = new Label
            {
                Dock = DockStyle.Top,
                Height = 70,
                Text = instruction,
                Font = new Font("Segoe UI", 10f),
                TextAlign = ContentAlignment.MiddleCenter
            };
            _countdown = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                Text = "Captures in 5s  —  hover the Rec Room control",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.Orange
            };
            _capture = new Button
            {
                Text = "Capture now",
                Width = 140,
                Height = 36,
                Location = new Point(110, 125)
            };
            _skip = new Button
            {
                Text = "Skip",
                Width = 140,
                Height = 36,
                Location = new Point(270, 125)
            };
            _capture.Click += (_, _) => Finish(false);
            _skip.Click += (_, _) => Finish(true);
            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) Finish(false);
                if (e.KeyCode == Keys.Escape) Finish(true);
            };

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += (_, _) =>
            {
                _secondsLeft--;
                _countdown.Text = _secondsLeft > 0
                    ? $"Captures in {_secondsLeft}s  —  hover the Rec Room control"
                    : "Capturing...";
                if (_secondsLeft <= 0)
                    Finish(false);
            };

            Controls.Add(_capture);
            Controls.Add(_skip);
            Controls.Add(_countdown);
            Controls.Add(_instruction);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RecRoomWindow.Focus();
            _timer.Start();
        }

        private void Finish(bool skip)
        {
            _timer.Stop();
            Skipped = skip;
            Captured = Cursor.Position;
            DialogResult = skip ? DialogResult.Cancel : DialogResult.OK;
            Close();
        }
    }
}
