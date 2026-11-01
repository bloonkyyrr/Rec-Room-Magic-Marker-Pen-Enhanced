using System.Drawing;
using System.Windows.Forms;
using KGySoft.Drawing;

namespace RecRoomPainter
{
    public partial class DrawPreviewForm : Form
    {
        Bitmap imagePreview = MainForm.DrawImage.Preview;
        private Point initialMousePosition;


        public DrawPreviewForm(MouseEventArgs mouse)
        {

            imagePreview = BitmapExtensions.Resize(imagePreview, new Size(imagePreview.Width, imagePreview.Height), MainForm.scaleType, false);
            InitializeComponent();

            UpdateImage(imagePreview);
        }

        public void UpdateImage(Bitmap img)
        {
            // While the overlay is open it takes over each preview bitmap it is handed, so
            // the one being replaced has no other owner left and can be released.
            Image previous = pictureBox1.Image;
            pictureBox1.Image = img;
            if (previous == null || ReferenceEquals(previous, img))
                return;
            try
            {
                previous.Dispose();
            }
            catch
            {
                // Already released by the owner; nothing to do.
            }
        }

        // Pixel-accurate placement: dragging is coarse, so the arrow keys nudge the
        // whole overlay by one pixel (ten with Shift) without touching its size.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            int step = (keyData & Keys.Shift) == Keys.Shift ? 10 : 1;
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left:
                    Location = new Point(Location.X - step, Location.Y);
                    return true;
                case Keys.Right:
                    Location = new Point(Location.X + step, Location.Y);
                    return true;
                case Keys.Up:
                    Location = new Point(Location.X, Location.Y - step);
                    return true;
                case Keys.Down:
                    Location = new Point(Location.X, Location.Y + step);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void pictureBox1_Click(object sender, System.EventArgs e)
        {

        }

        private void buttonRD_MouseDown_1(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                initialMousePosition = new Point(e.X, e.Y);  // Store initial mouse position
            }
        }

        private void buttonRD_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // Calculate the new window position relative to the initial mouse position
                int deltaX = e.X - initialMousePosition.X;
                int deltaY = e.Y - initialMousePosition.Y;
                this.Location = new Point(this.Location.X, this.Location.Y);
                this.Size = new Size(this.Size.Width + deltaX, this.Size.Height + deltaY);
            }
        }

        private void buttonRD_MouseUp(object sender, MouseEventArgs e)
        {

        }

        private void RU_MouseUp(object sender, MouseEventArgs e)
        {
        }

        private void RU_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // Calculate the new window position relative to the initial mouse position
                int deltaX = e.X - initialMousePosition.X;
                int deltaY = e.Y - initialMousePosition.Y;
                this.Location = new Point(this.Location.X, this.Location.Y + deltaY);
                this.Size = new Size(this.Size.Width + deltaX, this.Size.Height - deltaY);
            }
        }

        private void RU_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                initialMousePosition = new Point(e.X, e.Y);  // Store initial mouse position
            }
        }

        private void buttonLD_MouseUp(object sender, MouseEventArgs e)
        {
        }

        private void buttonLD_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // Calculate the new window position relative to the initial mouse position
                int deltaX = e.X - initialMousePosition.X;
                int deltaY = e.Y - initialMousePosition.Y;
                this.Location = new Point(this.Location.X + deltaX, this.Location.Y);
                this.Size = new Size(this.Size.Width - deltaX, this.Size.Height + deltaY);
            }
        }

        private void buttonLD_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                initialMousePosition = new Point(e.X, e.Y);  // Store initial mouse position
            }
        }

        private void buttonLU_MouseUp(object sender, MouseEventArgs e)
        {
        }

        private void buttonLU_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // Calculate the new window position relative to the initial mouse position
                int deltaX = e.X - initialMousePosition.X;
                int deltaY = e.Y - initialMousePosition.Y;
                this.Location = new Point(this.Location.X + deltaX, this.Location.Y + deltaY);
                this.Size = new Size(this.Size.Width - deltaX, this.Size.Height - deltaY);
            }
        }

        private void buttonLU_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                initialMousePosition = new Point(e.X, e.Y);  // Store initial mouse position
            }
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // Calculate the new window position relative to the initial mouse position
                int deltaX = e.X - initialMousePosition.X;
                int deltaY = e.Y - initialMousePosition.Y;
                this.Location = new Point(this.Location.X + deltaX, this.Location.Y + deltaY);
            }
        }

        private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                initialMousePosition = new Point(e.X, e.Y);  // Store initial mouse position
            }
        }

        private void setButton_Click(object sender, System.EventArgs e)
        {

        }
    }
}
