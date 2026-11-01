using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using static RecRoomPainter.MainForm;

namespace RecRoomPainter
{
    public static class MagicMarker
    {
        public static string ToRecRoomHex(Color color, bool includeHash)
        {
            string hex = $"{color.R:X2}{color.G:X2}{color.B:X2}";
            return includeHash ? "#" + hex : hex;
        }

        public static bool TryParseHex(string text, out Color color)
        {
            color = Color.Black;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            text = text.Trim().TrimStart('#');
            if (text.Length != 6)
                return false;
            if (!int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int value))
                return false;
            color = Color.FromArgb(255, (value >> 16) & 255, (value >> 8) & 255, value & 255);
            return true;
        }

        public static void QueueSetCustomColor(Color color, SavedConfig config)
        {
            string hex = ToRecRoomHex(color, config.IncludeHash);
            Rectangle bounds = RecRoomWindow.Bounds();
            Point custom = config.CustomButton(bounds);
            Point hexField = config.HexField(bounds);
            Point customDone = config.CustomDone(bounds);
            Point paletteDone = config.PaletteDone(bounds);
            int delay = Math.Max(200, config.ColorChangeDelay);

            void Click(Point p)
            {
                ActionQueue.Sleep(delay);
                ActionQueue.Move(p.X, p.Y, 120); // let Rec Room see the cursor on the target before clicking
                ActionQueue.LeftDown(p.X, p.Y, 40);
                ActionQueue.LeftUp(p.X, p.Y, 40);
            }

            if (config.PressFToOpenPalette)
            {
                // Open the palette with F and let it finish loading before the clicks.
                ActionQueue.Sleep(150);
                ActionQueue.KeyPress("f");
                ActionQueue.Sleep(Math.Max(0, config.PaletteWaitMs));
            }

            if (config.RightClickToOpenPalette)
            {
                ActionQueue.Sleep(delay);
                ActionQueue.Move(custom.X, custom.Y, 0);
                ActionQueue.RightDown(custom.X, custom.Y, 40);
                ActionQueue.RightUp(custom.X, custom.Y, 40);
            }

            Click(custom);
            Click(hexField);
            ActionQueue.Sleep(delay);

            if (config.TypeHexInsteadOfPaste)
            {
                ActionQueue.KeyPress("^{a}");
                ActionQueue.Sleep(120);
                ActionQueue.KeyPress("{DEL}");
                ActionQueue.Sleep(80);
                ActionQueue.KeyPress(EscapeSendKeys(hex));
            }
            else
            {
                ActionQueue.SetClipboard(hex);
                ActionQueue.KeyPress("^{a}");
                ActionQueue.Sleep(delay);
                ActionQueue.KeyPress("^{v}");
            }

            // DONE is clicked from the queue below so it can be verified and retried:
            // a missed click leaves the window open and the marker keeps the previous color.
            ActionQueue.Custom(() => PressDoneUntilClosed(customDone, delay));

            if (config.ClosePaletteAfterColor)
                Click(paletteDone);

            ActionQueue.Sleep(delay);
        }

        // DONE has a small hitbox and the click goes to a window Rec Room does not have focused,
        // so confirm the CUSTOM COLOR window actually closed and retry before giving up.
        private static void PressDoneUntilClosed(Point done, int delay)
        {
            const int settle = 200;
            Mouse.Move(done.X, done.Y, settle);

            Color[] openLook = SampleAround(done);
            if (openLook == null)
            {
                // Screen sampling is unavailable: fall back to one deliberate click.
                Mouse.DrawClick(0x02, done.X, done.Y, 60);
                Mouse.DrawClick(0x04, done.X, done.Y, 60);
                return;
            }

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                Mouse.DrawClick(0x02, done.X, done.Y, 60);
                Mouse.DrawClick(0x04, done.X, done.Y, 60);
                // Pump instead of blocking: this runs inside the draw queue, and a silent
                // second here looked like the app had frozen.
                MainForm.PumpSleep(Math.Max(400, delay));

                if (ScreenChanged(openLook, done))
                    return;

                Mouse.Move(done.X, done.Y, settle);
            }

            MainForm.RequestStop("The CUSTOM COLOR window stayed open after pressing DONE, so the color was never applied. " +
                "Re-run \"Calibrate Magic Marker colors\" and capture the DONE point from the open window.");
        }

        private static readonly Point[] DialogProbeOffsets =
        {
            new(0, 0), new(-20, 0), new(20, 0), new(0, -14), new(0, 14),
            new(-45, -30), new(45, -30), new(-45, 30), new(45, 30)
        };

        private static Color[] SampleAround(Point center)
        {
            var colors = new Color[DialogProbeOffsets.Length];
            try
            {
                using var bmp = new Bitmap(1, 1);
                using var graphics = Graphics.FromImage(bmp);
                for (int i = 0; i < DialogProbeOffsets.Length; i++)
                {
                    graphics.CopyFromScreen(center.X + DialogProbeOffsets[i].X,
                                            center.Y + DialogProbeOffsets[i].Y, 0, 0, new Size(1, 1));
                    colors[i] = bmp.GetPixel(0, 0);
                }
            }
            catch
            {
                return null;
            }
            return colors;
        }

        private static bool ScreenChanged(Color[] before, Point center)
        {
            Color[] after = SampleAround(center);
            if (after == null)
                return true;

            int changed = 0;
            for (int i = 0; i < before.Length; i++)
            {
                if (Math.Abs(before[i].R - after[i].R) +
                    Math.Abs(before[i].G - after[i].G) +
                    Math.Abs(before[i].B - after[i].B) > 40)
                    changed++;
            }
            return changed >= 3;
        }

        private static string EscapeSendKeys(string text)
        {
            var builder = new StringBuilder();
            foreach (char c in text)
            {
                if ("+^%~(){}".Contains(c))
                    builder.Append('{').Append(c).Append('}');
                else
                    builder.Append(c);
            }
            return builder.ToString();
        }
    }
}
