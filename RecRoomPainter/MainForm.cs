using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using KGySoft.Drawing;
using KGySoft.Drawing.Imaging;

namespace RecRoomPainter
{

    public partial class MainForm : Form
    {
        private RunningProgressBar RunPBar;
        private static volatile bool stopRequested;
        private static volatile string stopReason;
        public static SavedConfig Config { get; private set; } = SavedConfig.Load();

        private ComboBox canvasTypeBox;
        private Button calibrateColorButton;
        private Button testColorButton;
        private NumericUpDown colorDelayBox;
        private NumericUpDown penSizeBox;
        private CheckBox hexHashBox;
        private CheckBox closePaletteBox;
        private CheckBox typeHexBox;
        private CheckBox rightClickOpenBox;
        private NumericUpDown paletteWaitBox;
        private FlowLayoutPanel palettePanel;
        private Label paletteLabel;
        private ToolTip paletteTip;

        public static ScalingMode scaleType = ScalingMode.Box;
        public static ScalingMode processScaleType = ScalingMode.NearestNeighbor;

        private static volatile bool pauseRequested;
        private static bool pauseKeyWasDown;

        // True while the built queue is being executed against Rec Room.
        private static volatile bool running;

        // True while Draw() builds the action queue on a worker thread.
        private static volatile bool planRunning;
        private static bool planWantsProcess;
        // Bitmap currently handed to the placement overlay; never disposed while it is shown.
        private static Bitmap overlayImage;
        private Button pauseButton;
        private NumericUpDown speedBox;
        private NumericUpDown maxGapBox;
        private ToolTip optionTips;

        public static class Time
        {
            // Unscaled stroke pacing. 100% speed uses exactly these values.
            private const int BaseUpDelay = 40;
            private const int BaseDownDelay = 40;
            private const int BaseTurnDelay = 45;
            private const int BaseSettleDelay = 15;

            // A game renders every ~16 ms, so a press and release closer together than
            // this can merge into one held button: lifting here and pressing far away
            // then reads as a single drag and streaks a line across the canvas.
            private const int MinClickDelay = 20;

            static Time()
            {
                SpeedPercent = 100;
                ColorChangeDelay = 1000;
                EstimatedTime = 0;
                ApplySpeed();
            }
            public static int MouseUpDelay { get; private set; }
            public static int MouseDownDelay { get; private set; }
            public static int MouseTurnDelay { get; private set; }
            public static int PenSettleDelay { get; private set; }
            public static int ColorChangeDelay { get; set; }
            public static long EstimatedTime { get; set; }
            public static int SpeedPercent { get; private set; }

            // Scales the three stroke delays: higher percent = less waiting between
            // mouse actions = faster drawing, lower = more careful.
            public static void SetSpeed(int percent)
            {
                SpeedPercent = Math.Clamp(percent, 25, 400);
                ApplySpeed();
            }

            private static void ApplySpeed()
            {
                double factor = SpeedPercent / 100.0;
                // The button edges keep a floor even at high speed: below it the game can
                // miss the release and join two separate strokes into one drag.
                MouseUpDelay = Math.Max(MinClickDelay, (int)System.Math.Round(BaseUpDelay / factor));
                MouseDownDelay = Math.Max(MinClickDelay, (int)System.Math.Round(BaseDownDelay / factor));
                MouseTurnDelay = Math.Max(1, (int)System.Math.Round(BaseTurnDelay / factor));
                PenSettleDelay = Math.Max(MinClickDelay, (int)System.Math.Round(BaseSettleDelay / factor));
            }

            public static void ColorChangeDefault()
            {
                ColorChangeDelay = 1000;
            }
        }



        public static class Settings
        {
            static Settings()
            {
                DrawW = 0;
                DrawH = 0;
                DrawX = 0;
                DrawY = 0;
                CropW = 0;
                CropH = 0;
                CropX = 0;
                CropY = 0;
                Pixelation = 1;
                MaxColors = 16;
                SkipColors = 0;
                Depth = 0;
                FillFirstLayer = false;
                DitherPattern = 0;
                QuantType = 0;
                PenSize = (Config?.PenSize ?? 0) > 0 ? Config.PenSize : 3;
                RandomOrder = false;
                VectorMode = false;
                DirectDraw = false;
                TapMode = Config?.TapMode ?? false;
                MaxGap = Config?.MaxGap ?? 0;
            }
            public static int DrawW { get; set; }
            public static int DrawH { get; set; }
            public static int DrawX { get; set; }
            public static int DrawY { get; set; }
            public static int CropW { get; set; }
            public static int CropH { get; set; }
            public static int CropX { get; set; }
            public static int CropY { get; set; }
            public static double Pixelation { get; set; }
            public static int MaxColors { get; set; }
            public static int SkipColors { get; set; }

            public static int Depth { get; set; }
            public static bool FillFirstLayer { get; set; }
            public static int DitherPattern { get; set; }
            public static int QuantType { get; set; }
            public static float PenSize { get; set; }
            public static bool VectorMode { get; set; }
            public static bool DirectDraw { get; set; }
            public static bool RandomOrder { get; set; }
            public static bool TapMode { get; set; }
            public static int MaxGap { get; set; }

        }

        public static class DrawImage
        {
            public static Bitmap Original { get; set; }
            public static Bitmap Modified { get; set; }
            public static Bitmap Preview { get; set; }

            // The palette of Modified, computed once while processing instead of being
            // rescanned by the planner and the swatch preview separately.
            public static Color[] Palette { get; set; }
        }




        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public static class ActionQueue
        {
            static ActionQueue()
            {
                AQueue = new Queue<Action>();
            }

            public static Queue<Action> AQueue { get; set; }

            public static void EmptyQueue()
            {
                AQueue.Clear();
            }

            public static void LeftDown(int x, int y, int delay)
            {
                Stats.Touch(x, y);
                Stats.PenDown();
                ActionQueue.AQueue.Enqueue((() => Mouse.DrawClick(0x02, x, y, delay)));
                Time.EstimatedTime += delay;

            }
            public static void LeftUp(int x, int y, int delay)
            {
                Stats.PenUp();
                Stats.Touch(x, y);
                ActionQueue.AQueue.Enqueue((() => Mouse.DrawClick(0x04, x, y, delay)));
                Time.EstimatedTime += delay;
            }
            public static void RightDown(int x, int y, int delay)
            {
                Stats.Touch(x, y);
                ActionQueue.AQueue.Enqueue((() => Mouse.DrawClick(0x08, x, y, delay)));
                Time.EstimatedTime += delay;
            }
            public static void RightUp(int x, int y, int delay)
            {
                Stats.Touch(x, y);
                ActionQueue.AQueue.Enqueue((() => Mouse.DrawClick(0x10, x, y, delay)));
                Time.EstimatedTime += delay;
            }
            public static void Move(int x, int y, int delay)
            {
                Stats.Touch(x, y);
                ActionQueue.AQueue.Enqueue((() => Mouse.Move(x, y, delay)));
                Time.EstimatedTime += delay;
            }
            public static void Sleep(int delay)
            {
                ActionQueue.AQueue.Enqueue((() => PumpSleep(delay)));
                Time.EstimatedTime += delay;
            }
            public static void KeyPress(string key) => ActionQueue.AQueue.Enqueue((() => SendKeys.SendWait(key)));
            public static void SetClipboard(string text) => ActionQueue.AQueue.Enqueue(() =>
            {
                try
                {
                    Clipboard.SetText(text);
                }
                catch
                {
                    Clipboard.SetDataObject(text, true);
                }
            });
            public static void SetColorSkipValue(int value) => ActionQueue.AQueue.Enqueue((() => Settings.SkipColors = value));
            public static void Custom(Action action) => ActionQueue.AQueue.Enqueue(action);

        }

        // Records the shape of the plan while the queue is built, so Estimate can
        // report how many strokes the drawing needs and how far the cursor travels.
        public static class Stats
        {
            public static int Strokes { get; private set; }
            public static long TravelPx { get; private set; }
            private static int lastX;
            private static int lastY;
            private static bool hasLast;

            // Longest distance one stroke travels with the button held down. A value far
            // larger than anything in the image means a stroke is bridging empty areas.
            public static int LongestStrokePx { get; private set; }
            private static int currentStrokePx;
            private static bool penDown;

            public static void Reset()
            {
                Strokes = 0;
                TravelPx = 0;
                LongestStrokePx = 0;
                currentStrokePx = 0;
                penDown = false;
                hasLast = false;
            }

            public static void Stroke()
            {
                Strokes++;
            }

            public static void PenDown()
            {
                penDown = true;
                currentStrokePx = 0;
            }

            public static void PenUp()
            {
                if (penDown && currentStrokePx > LongestStrokePx)
                    LongestStrokePx = currentStrokePx;
                penDown = false;
            }

            public static void Touch(int x, int y)
            {
                if (hasLast)
                {
                    int distance = Math.Abs(x - lastX) + Math.Abs(y - lastY);
                    TravelPx += distance;
                    if (penDown)
                        currentStrokePx += distance;
                }
                lastX = x;
                lastY = y;
                hasLast = true;
            }
        }

        public static class Mouse
        {

            [DllImport("user32.dll")]
            private static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo);

            // This is a replacement for Cursor.Position in WinForms
            [DllImport("user32.dll")]
            private static extern bool SetCursorPos(int x, int y);
            public static void Move(int x, int y, int delay)
            {
                SetCursorPos(x, y);
                Thread.Sleep(delay);
            }

            public static void DrawClick(int id, int x, int y, int delay)
            {

                SetCursorPos(x, y);
                mouse_event(id, x, y, 0, 0);
                Thread.Sleep(delay);
            }


        }


        public MainForm()
        {
            InitializeComponent();
            AddCanvasAndMarkerControls();
            LoadSavedUi();
        }

        public static void ActivateWindow(string windowTitle)
        {
            IntPtr windowHandle = FindWindow(null, windowTitle);

            if (windowHandle != IntPtr.Zero)
            {
                // Bring the window to the foreground
                SetForegroundWindow(windowHandle);

                // Set focus to the window
                SetFocus(windowHandle);
            }
            else
            {
                Console.WriteLine("Window not found with the specified title.");
            }
        }


        void EnableControls(bool enable)
        {
            widthInput.Enabled = enable;
            heightInput.Enabled = enable;
            pixelateBar.Enabled = enable;
            DrawPositionButton.Enabled = enable;
            skipColorBox.Enabled = enable;
            maxColorsBox.Enabled = enable;
            firstLayerFill.Enabled = enable;
            ditherBox.Enabled = enable;
            quantBox.Enabled = enable;
            startButton.Enabled = enable;
            clearButton.Enabled = enable;
            if (calibrateColorButton != null)
            {
                canvasTypeBox.Enabled = true;
                calibrateColorButton.Enabled = true;
                testColorButton.Enabled = true;
                colorDelayBox.Enabled = true;
                penSizeBox.Enabled = enable;
                hexHashBox.Enabled = true;
                closePaletteBox.Enabled = true;
                typeHexBox.Enabled = true;
                rightClickOpenBox.Enabled = true;
            }
            XBox.Enabled = enable;
            YBox.Enabled = enable;
            cropXnum.Enabled = enable;
            cropYnum.Enabled = enable;
            cropWnum.Enabled = enable;
            cropHnum.Enabled = enable;
        }

        public void UpdateUITextValues()
        {
            widthInput.Text = Settings.DrawW.ToString();
            heightInput.Text = Settings.DrawH.ToString();
            skipColorBox.Text = Settings.SkipColors.ToString();
            maxColorsBox.Text = Settings.MaxColors.ToString();
            XBox.Text = Settings.DrawX.ToString();
            YBox.Text = Settings.DrawY.ToString();
            cropXnum.Value = Settings.CropX;
            cropYnum.Value = Settings.CropY;
            cropWnum.Value = Settings.CropW;
            cropHnum.Value = Settings.CropH;
        }


        public void ProcessImage()
        {
            // Planning walks this exact bitmap on a worker thread; reprocessing it underneath
            // that walk would change the image mid-plan. Remember the request and run it once
            // the plan is done.
            if (planRunning)
            {
                planWantsProcess = true;
                return;
            }

            if (DrawImage.Original == null)
                return;

            try
            {
                EnableControls(false);
                progressBar1.Value = 0;
                // Flatten PNG transparency before anything reads the pixels: the resize
                // would otherwise average colour against transparent black, and the
                // quantizer drops anything below its own alpha threshold entirely.
                NormalizeAlpha(DrawImage.Original);
                ReplaceModified(BitmapExtensions.Resize(DrawImage.Original, new Size((int)Math.Round(Settings.DrawW * Settings.Pixelation), (int)Math.Round(Settings.DrawH * Settings.Pixelation)), processScaleType));
                NormalizeAlpha(DrawImage.Modified);

                progressBar1.Value = 10;
                IDitherer dither = OrderedDitherer.Bayer2x2;

                IQuantizer quantizer = OptimizedPaletteQuantizer.Wu(Settings.MaxColors);
                switch (Settings.QuantType)
                {
                    case 1:
                        quantizer = OptimizedPaletteQuantizer.MedianCut(Settings.MaxColors);
                        break;
                    case 2:
                        quantizer = OptimizedPaletteQuantizer.Octree(Settings.MaxColors);
                        break;
                    default:
                        quantizer = OptimizedPaletteQuantizer.Wu(Settings.MaxColors);
                        break;
                }

                switch (Settings.DitherPattern)
                {
                    case 2:
                        dither = OrderedDitherer.Bayer3x3;
                        break;
                    case 3:
                        dither = OrderedDitherer.Bayer4x4;
                        break;
                    case 4:
                        dither = OrderedDitherer.Bayer8x8;
                        break;
                    case 5:
                        dither = OrderedDitherer.BlueNoise;
                        break;
                    case 6:
                        dither = OrderedDitherer.DottedHalftone;
                        break;
                    case 7:
                        dither = ErrorDiffusionDitherer.Atkinson;
                        break;
                    case 8:
                        dither = ErrorDiffusionDitherer.Burkes;
                        break;
                    case 9:
                        dither = ErrorDiffusionDitherer.FloydSteinberg;
                        break;
                    case 10:
                        dither = ErrorDiffusionDitherer.JarvisJudiceNinke;
                        break;
                    case 11:
                        dither = ErrorDiffusionDitherer.Sierra2;
                        break;
                    case 12:
                        dither = ErrorDiffusionDitherer.Sierra3;
                        break;
                    case 13:
                        dither = ErrorDiffusionDitherer.SierraLite;
                        break;
                    case 14:
                        dither = ErrorDiffusionDitherer.StevensonArce;
                        break;
                    case 15:
                        dither = ErrorDiffusionDitherer.Stucki;
                        break;
                }
                if (Settings.DitherPattern > 0)
                {
                    BitmapExtensions.Dither(DrawImage.Modified, quantizer, dither);
                }
                else
                {
                    ReplaceModified(DrawImage.Modified.ConvertPixelFormat(PixelFormat.Format32bppArgb, quantizer));
                }
                progressBar1.Value = 50;
                //DrawImage.Modified = BitmapExtensions.Resize(DrawImage.Modified, DrawImage.Original.Size, ScalingMode.NearestNeighbor);
                ReplaceModified(CropImageSet(DrawImage.Modified));
                ReplaceModified(BitmapExtensions.Resize(DrawImage.Modified, new Size((int)Math.Round(Settings.DrawW / Settings.PenSize), (int)Math.Round(Settings.DrawH / Settings.PenSize)), ScalingMode.NearestNeighbor));
                DrawImage.Palette = ImageToPallet(DrawImage.Modified);
                SetPreview();
                RefreshPalettePreview();

                progressBar1.Value = 100;
            }
            catch (Exception)
            {
                MessageBox.Show(new Form() { TopMost = true }, "Image could not be processed", "Process Error", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
            }
            finally
            {
                // Controls must never be left disabled when processing fails.
                EnableControls(true);
            }
        }

        // Swaps in the next stage of the pipeline and disposes the bitmap it replaced.
        // Nothing outside ProcessImage holds those intermediates, and without this every
        // run left a full copy of the canvas behind for the garbage collector to chew on.
        private static void ReplaceModified(Bitmap next)
        {
            Bitmap previous = DrawImage.Modified;
            DrawImage.Modified = next;
            if (previous == null || ReferenceEquals(previous, next))
                return;
            if (ReferenceEquals(previous, DrawImage.Original) ||
                ReferenceEquals(previous, DrawImage.Preview) ||
                ReferenceEquals(previous, overlayImage))
                return;
            try
            {
                previous.Dispose();
            }
            catch
            {
                // Already disposed elsewhere; nothing to do.
            }
        }


        private async Task StartAsync()
        {
            try
            {
                bool planned = await PlanAsync();
                // The window may have been closed while the plan was being built.
                if (IsDisposed)
                    return;
                if (planned)
                    RunQueue();
                else
                    MessageBox.Show(new Form() { TopMost = true }, "Drawing halted", "Incomplete", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
            }
            catch (Exception)
            {
                MessageBox.Show(new Form() { TopMost = true }, "Drawing Failed", "Drawing Error", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
            }
        }

        // Builds the whole mouse-action queue. On a big canvas that is hundreds of thousands
        // of path searches, so it runs on a worker thread: planning on the UI thread is what
        // left the window unresponsive between pressing Start / Estimate and drawing.
        private async Task<bool> PlanAsync()
        {
            if (DrawImage.Modified == null)
                return false;

            planRunning = true;
            SetBusy(true);
            var progress = new Progress<int>(value =>
            {
                if (progressBar1 != null && !progressBar1.IsDisposed)
                    progressBar1.Value = Math.Clamp(value, 0, 100);
            });

            try
            {
                return await Task.Run(() => Draw(progress));
            }
            finally
            {
                planRunning = false;
                SetBusy(false);
                if (planWantsProcess)
                {
                    planWantsProcess = false;
                    ProcessImage();
                }
            }
        }

        // Keeps the window interactive while planning by reflecting it in the cursor.
        private void SetBusy(bool busy)
        {
            if (IsDisposed)
                return;
            UseWaitCursor = busy;
        }

        public static void SetPenColor(Color color)
        {
            Time.ColorChangeDelay = Math.Max(200, Config.ColorChangeDelay);
            MagicMarker.QueueSetCustomColor(color, Config);
        }

        // A PNG with an alpha channel used to break the pipeline in two ways: the quantizer
        // maps everything below its alpha threshold to fully transparent (so a soft or
        // half-transparent image produced an empty palette and nothing was drawn), and it
        // premultiplies the rest against transparent black (50% red came out dark red).
        // Normalising alpha first fixes both: whatever is the strongest alpha in the image
        // becomes fully opaque and keeps its exact colour, weaker pixels become fully
        // transparent, and transparent pixels lose their hidden RGB so they cannot add
        // phantom colours to the palette.
        private static void NormalizeAlpha(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0)
                return;

            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int bytes = Math.Abs(data.Stride) * bitmap.Height;
                // A 3000x2000 photo needs a 24 MB buffer here, and allocating a fresh one on
                // every processing run was the single largest allocation in the app (45 MB per
                // run with the writes). One scratch buffer is kept and grown as needed instead;
                // ArrayPool does not help because its shared pool only retains 1 MB arrays.
                byte[] pixelData = GetAlphaScratchBuffer(bytes);

                Marshal.Copy(data.Scan0, pixelData, 0, bytes);

                byte maxAlpha = 0;
                bool anyTransparent = false;
                for (int i = 3; i < bytes; i += 4)
                {
                    byte alpha = pixelData[i];
                    if (alpha > maxAlpha)
                        maxAlpha = alpha;
                    if (alpha != 255)
                        anyTransparent = true;
                }

                // Fully transparent image: nothing is visible, leave it alone.
                if (maxAlpha == 0)
                    return;

                // Already opaque: no pixel can change, so skip rewriting the whole buffer.
                if (anyTransparent)
                {
                    int threshold = Math.Max(1, maxAlpha / 2);
                    for (int i = 3; i < bytes; i += 4)
                    {
                        if (pixelData[i] >= threshold)
                        {
                            pixelData[i] = 255;
                        }
                        else
                        {
                            pixelData[i] = 0;
                            pixelData[i - 3] = 0;
                            pixelData[i - 2] = 0;
                            pixelData[i - 1] = 0;
                        }
                    }

                    Marshal.Copy(pixelData, 0, data.Scan0, bytes);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        // Scratch buffer reused by NormalizeAlpha so processing a photo does not allocate
        // tens of megabytes each time the settings change.
        private static byte[] alphaScratch = Array.Empty<byte>();

        private static byte[] GetAlphaScratchBuffer(int bytes)
        {
            if (alphaScratch.Length < bytes)
                alphaScratch = new byte[bytes];
            return alphaScratch;
        }

        private static uint PackedColor(Color color) =>
            ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;

        // The planner used to call CoverageMatrix once per palette color, and each call
        // copied the entire bitmap again (LockBits + Marshal.Copy) before scanning it, so
        // an N-color image read the image N times. One pass that stores the palette index
        // of every pixel answers all of those questions with an integer compare instead.
        private static int[,] PaletteIndexMap(Bitmap img, Color[] palette)
        {
            int width = img.Width;
            int height = img.Height;
            int[,] map = new int[width, height];

            var lookup = new Dictionary<uint, int>(palette.Length);
            for (int i = 0; i < palette.Length; i++)
                lookup[PackedColor(palette[i])] = i;

            Rectangle rect = new Rectangle(0, 0, width, height);
            BitmapData data = img.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int bytes = Math.Abs(stride) * height;
                byte[] pixels = new byte[bytes];
                Marshal.Copy(data.Scan0, pixels, 0, bytes);

                for (int y = 0; y < height; y++)
                {
                    int rowStart = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int index = rowStart + x * 4;
                        uint key = ((uint)pixels[index + 3] << 24) |
                                   ((uint)pixels[index + 2] << 16) |
                                   ((uint)pixels[index + 1] << 8) |
                                   pixels[index];
                        map[x, y] = lookup.TryGetValue(key, out int paletteIndex) ? paletteIndex : -1;
                    }
                }
            }
            finally
            {
                img.UnlockBits(data);
            }

            return map;
        }

        // Marks every pixel that belongs to one palette entry. Pixels are only ever set
        // to true, so accumulating into the same matrix keeps the already-painted mask.
        private static void MarkColor(bool[,] matrix, int[,] indexMap, int paletteIndex)
        {
            int width = indexMap.GetLength(0);
            int height = indexMap.GetLength(1);
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (indexMap[x, y] == paletteIndex)
                        matrix[x, y] = true;
                }
            }
        }

        public struct PathStep
        {
            public Point Location { get; set; }
            public long Score { get; set; }
            public int Skipped { get; set; }

            public PathStep()
            {
                Location = new Point(0, 0);
                Score = 0;
            }

            public PathStep(Point location, long score)
            {
                Location = location;
                Score = score;
            }
        }
        public static class Path
        {


            private static bool IsWithinBounds(bool[,] matrix, Point loc)
            {
                return loc.X >= 0 && loc.X < matrix.GetLength(0) &&
                       loc.Y >= 0 && loc.Y < matrix.GetLength(1);
            }

            private static PathStep GetPathScore(bool[,] tMatrix, bool[,] matrix, Point startLocation, Point direction)
            {
                var step = new PathStep(startLocation, 0);

                // D-Draw is strict: stop at the first pixel that is not the current color.
                // Otherwise a stroke may bridge at most MaxGap pixels, so it can never drag
                // a line straight across an area that was never meant to be this color.
                int maxGap = Settings.DirectDraw ? 0 : Math.Max(0, Settings.MaxGap);
                int gap = 0;     // pixels of another color in a row
                int pending = 0; // pixels of another color crossed since the last match

                while (true)
                {
                    Point newLoc = new Point(startLocation.X + direction.X, startLocation.Y + direction.Y);

                    if (!IsWithinBounds(matrix, newLoc) || tMatrix[newLoc.X, newLoc.Y])
                        break;

                    startLocation = newLoc;
                    if (matrix[startLocation.X, startLocation.Y])
                    {
                        step.Score++;
                        step.Location = startLocation;
                        // Only skips that are actually crossed on the way to another pixel
                        // of this color count, so a gap at the very end of a run is free.
                        step.Skipped += pending;
                        pending = 0;
                        gap = 0;
                    }
                    else
                    {
                        pending++;
                        if (++gap > maxGap)
                            break;
                    }

                }
                return step;
            }

            // Longest run first; on a tie prefer the direction that crossed fewer gaps.
            private static void SortPaths(List<PathStep> paths)
            {
                paths.Sort((a, b) =>
                {
                    int byScore = b.Score.CompareTo(a.Score);
                    return byScore != 0 ? byScore : a.Skipped.CompareTo(b.Skipped);
                });
            }

            private static List<PathStep> GetAllPathValues(bool[,] tMatrix, bool[,] matrix, Point startLocation, List<Point> directions)
            {
                var paths = new List<PathStep>();
                if (directions == null || directions.Count == 0)
                    return paths;

                foreach (var dir in directions)
                {
                    paths.Add(GetPathScore(tMatrix, matrix, startLocation, dir));
                }
                // Best score first
                SortPaths(paths);
                return paths;
            }

            public static PathStep PathSearch(bool[,] tMatrix, bool[,] matrix, Point startLocation, List<Point> directions)
            {
                return PathSearchHelper(tMatrix, matrix, startLocation, startLocation, directions, 0);
            }
            private static PathStep PathSearchHelper(bool[,] tMatrix, bool[,] matrix, Point start, Point end, List<Point> directions, int currentDepth)
            {
                var moves = ClearPath(matrix, start, end);
                var paths = GetAllPathValues(tMatrix, matrix, end, directions);
                SortPaths(paths);

                if (paths[0].Score == 0)
                {
                    RestoreMoves(matrix, moves);
                    return paths[0];
                }

                if (currentDepth < Settings.Depth)
                {
                    for (int i = 0; i < paths.Count; i++)
                    {
                        PathStep currentPath = paths[i];
                        currentPath.Score += PathSearchHelper(tMatrix, matrix, end, currentPath.Location, directions, currentDepth + 1).Score;
                        paths[i] = currentPath;
                    }
                }
                SortPaths(paths);
                RestoreMoves(matrix, moves);
                return paths[0];
            }

            public static List<Point> ClearPath(bool[,] matrix, Point start, Point end)
            {
                int dx = Math.Sign(end.X - start.X);
                int dy = Math.Sign(end.Y - start.Y);

                List<Point> moves = [];

                while (IsWithinBounds(matrix, start))
                {
                    if (matrix[start.X, start.Y])
                    {
                        moves.Add(start);
                        matrix[start.X, start.Y] = false;
                    }

                    // Break after processing the endpoint
                    if (start.X == end.X && start.Y == end.Y)
                        break;

                    start.X += dx;
                    start.Y += dy;

                }
                return moves;
            }

            public static void RestoreMoves(bool[,] matrix, List<Point> moves)
            {
                foreach (var p in moves)
                {
                    matrix[p.X, p.Y] = true;
                }

            }
        }



        public static void NeighborLineDraw(bool[,] tMatrix, bool[,] matrix, Point loc, List<Point> directions)
        {
            // The gap limit is a budget for the whole stroke, not for each step. When it
            // was per step, a dithered area let one held stroke hop from dot to dot for
            // the full height of the canvas, dragging a line over everything between.
            // Spending the budget ends the stroke, so the pen is lifted, the cursor moves
            // with the button up, and the rest is painted as its own stroke.
            int gapBudget = Settings.DirectDraw ? 0 : Math.Max(0, Settings.MaxGap);
            int gapUsed = 0;

            while (true)
            {
                PathStep path = Path.PathSearch(tMatrix, matrix, loc, directions);

                if (path.Score <= 0 || gapUsed + path.Skipped > gapBudget)
                    break;

                gapUsed += path.Skipped;
                Path.ClearPath(matrix, loc, path.Location);

                loc = path.Location;
                ActionQueue.Move(Settings.DrawX + (int)Math.Round(loc.X * Settings.PenSize),
                           Settings.DrawY + (int)Math.Round(loc.Y * Settings.PenSize),
                           Time.MouseTurnDelay);
            }
            ActionQueue.LeftUp(Settings.DrawX + (int)Math.Round(loc.X * Settings.PenSize),
                         Settings.DrawY + (int)Math.Round(loc.Y * Settings.PenSize),
                         Time.MouseDownDelay);
        }

        private static Color[] ImageToPallet(Bitmap img)
        {
            if (img == null)
                throw new ArgumentNullException(nameof(img));

            HashSet<Color> uniqueColors = new HashSet<Color>();

            // Lock the bitmap
            Rectangle rect = new Rectangle(0, 0, img.Width, img.Height);
            BitmapData bmpData = img.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = bmpData.Stride;
                byte[] pixelData = new byte[Math.Abs(stride) * img.Height];
                Marshal.Copy(bmpData.Scan0, pixelData, 0, pixelData.Length);

                for (int y = 0; y < img.Height; y++)
                {
                    int rowStart = y * stride;
                    for (int x = 0; x < img.Width; x++)
                    {
                        int index = rowStart + x * 4;
                        byte b = pixelData[index];
                        byte g = pixelData[index + 1];
                        byte r = pixelData[index + 2];
                        byte a = pixelData[index + 3];

                        uniqueColors.Add(Color.FromArgb(a, r, g, b));
                    }
                }
            }
            finally
            {
                img.UnlockBits(bmpData);
            }

            // Convert HashSet to array
            Color[] palette = uniqueColors.ToArray();

            // Sort colors by brightness (descending)
            Array.Sort(palette, (c1, c2) => c2.GetBrightness().CompareTo(c1.GetBrightness()));

            return palette;
        }


        static void DrawPixel(bool[,] tMatrix, bool[,] matrix, Point loc, List<Point> directions)
        {
            int xpos = Settings.DrawX + (int)Math.Round(loc.X * Settings.PenSize);
            int ypos = Settings.DrawY + (int)Math.Round(loc.Y * Settings.PenSize);

            Stats.Stroke();

            if (Settings.TapMode)
            {
                // Tap mode: one click per pixel, no button held down while moving,
                // so a stray pointer jump cannot streak a line across the canvas.
                ActionQueue.Move(xpos, ypos, Time.PenSettleDelay);
                ActionQueue.LeftDown(xpos, ypos, Time.MouseDownDelay);
                ActionQueue.LeftUp(xpos, ypos, Time.MouseUpDelay);
                matrix[loc.X, loc.Y] = false;
                return;
            }

            // Park the cursor on the stroke start with the button still up and let it sit
            // for a moment. Jumping straight into the press let the game see the new
            // position while the previous button state was still down, which drew a line
            // all the way from the end of the last stroke to this one.
            ActionQueue.Move(xpos, ypos, Time.PenSettleDelay);
            ActionQueue.LeftDown(xpos, ypos, Time.MouseDownDelay);
            NeighborLineDraw(tMatrix, matrix, loc, directions);
            matrix[loc.X, loc.Y] = false;
            // NeighborLineDraw already releases the button at the end of the stroke.
            // Releasing again here only teleported the cursor back to the stroke start
            // and cost a full delay per stroke.
        }

        private bool Draw(IProgress<int> progress = null)
        {
            if (DrawImage.Modified == null)
                return false;

            ActionQueue.EmptyQueue();
            Stats.Reset();
            List<Point> directions = new()
                {
                    new(0, 1),  // Up
                    new(0, -1), // Down
                    new(1, 0),  // Left
                    new(-1, 0)  // Right
                };

            if (Settings.VectorMode)
            {
                directions.Add(new(1, 1)); //TopRight
                directions.Add(new(1, -1)); //BottomRight
                directions.Add(new(-1, 1)); //TopLeft
                directions.Add(new(-1, -1)); //BottomLeft
            }

            Time.ColorChangeDelay = Math.Max(200, Config.ColorChangeDelay);

            Color[] pallet = DrawImage.Palette ?? ImageToPallet(DrawImage.Modified);
            DrawImage.Palette = pallet;

            int width = DrawImage.Modified.Width;
            int height = DrawImage.Modified.Height;
            int[,] indexMap = PaletteIndexMap(DrawImage.Modified, pallet);
            bool[,] tMatrix = new bool[width, height];

            for (int c = 0; c < pallet.Length; c++)
            {

                float percent = (float)(c / (float)pallet.Length) * 100;
                progress?.Report((int)percent);


                if (c > 0)
                {
                    MarkColor(tMatrix, indexMap, c - 1);
                }

                if (c < Settings.SkipColors || pallet[c].A < 16 || pallet[c] == Color.FromArgb(255, 255, 0, 255))
                {
                    continue;
                }

                bool[,] cMatrix = new bool[width, height];

                if (Settings.FillFirstLayer && c == 0)
                {
                    for (int i = 0; i < width; i++)
                    {
                        for (int j = 0; j < height; j++)
                        {
                            cMatrix[i, j] = true;
                        }
                    }
                }
                else
                {
                    MarkColor(cMatrix, indexMap, c);
                }

                SetPenColor(pallet[c]);

                // Visiting the covered pixels directly avoids building one anonymous object
                // and one int[] per pixel (then a list of both) for every color: a 1024x1024
                // canvas allocated over a million objects per color before.
                if (Settings.RandomOrder)
                {
                    int[] order = CoveredPixelsInRandomOrder(cMatrix, width, height);

                    // Two passes over the same shuffled order: the first paints strokes, the
                    // second revisits pixels that were blocked and could not be reached.
                    for (int pass = 0; pass < 2; pass++)
                    {
                        foreach (int packed in order)
                        {
                            int x = packed % width;
                            int y = packed / width;
                            if (cMatrix[x, y])
                                DrawPixel(tMatrix, cMatrix, new Point(x, y), directions);
                        }
                    }
                }
                else
                {
                    for (int pass = 0; pass < 2; pass++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            for (int y = 0; y < height; y++)
                            {
                                if (cMatrix[x, y])
                                    DrawPixel(tMatrix, cMatrix, new Point(x, y), directions);
                            }
                        }
                    }
                }
                ActionQueue.SetColorSkipValue(c);
            }
            ActionQueue.SetColorSkipValue(0);
            return true;
        }

        // The pixels of one color in random order, packed as y * width + x so a pixel costs
        // four bytes instead of an object plus an int[].
        private static int[] CoveredPixelsInRandomOrder(bool[,] matrix, int width, int height)
        {
            int count = 0;
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (matrix[x, y])
                        count++;

            int[] order = new int[count];
            int index = 0;
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    if (matrix[x, y])
                        order[index++] = y * width + x;

            Random rng = new Random();
            for (int i = order.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            return order;
        }
        // Can be called from a button click while the queue is running: the loop
        // picks the request up on its next iteration and releases the mouse button.
        public static void RequestStop(string reason = null)
        {
            stopReason = reason;
            stopRequested = true;
        }

        // Pause between actions: the queue is kept, so printing continues exactly where
        // it stopped. F8 / the Pause key toggles it too, so it works while Rec Room has focus.
        private void SetPaused(bool paused)
        {
            pauseRequested = paused;
            if (pauseButton != null && !pauseButton.IsDisposed)
                pauseButton.Text = paused ? "Resume (F8)" : "Pause (F8)";
        }

        private static bool PauseKeyPressed()
        {
            // Edge triggered, otherwise holding the key toggles pause every iteration.
            bool down = GetAsyncKeyState(0x77) < 0 || GetAsyncKeyState(0x13) < 0; // F8 or Pause/Break
            bool pressed = down && !pauseKeyWasDown;
            pauseKeyWasDown = down;
            return pressed;
        }

        // Sleep without freezing the window. A color change waits seconds for the palette,
        // and that one blocking Sleep left the app looking dead in the middle of a print.
        // Pumping the message loop in slices keeps it repainting, and keeps the Pause and
        // STOP buttons answerable while the queue is held.
        public static void PumpSleep(int milliseconds)
        {
            long deadline = Environment.TickCount64 + Math.Max(0, milliseconds);
            while (true)
            {
                long left = deadline - Environment.TickCount64;
                if (left <= 0)
                    break;
                Application.DoEvents();
                Thread.Sleep((int)Math.Min(25, left));
            }
        }

        public void RunQueue()
        {
            running = true;
            try
            {
                RunQueueCore();
            }
            finally
            {
                running = false;
            }
        }

        private void RunQueueCore()
        {
            int startLength = ActionQueue.AQueue.Count;
            var drawingStartTime = DateTime.Now;
            var drawingCurrentTime = DateTime.Now;

            stopRequested = false;
            stopReason = null;
            SetPaused(false);
            pauseKeyWasDown = false;

            if (RunPBar == null || RunPBar.IsDisposed)
            {
                RunPBar = new RunningProgressBar();
                RunPBar.UpdateProgress(0, startLength, 0);
                RunPBar.Show();
            }
            Application.DoEvents();

            if (!RecRoomWindow.Focus())
                ActivateWindow("Rec Room");
            PumpSleep(1000);
            // Do not open paused because a key was still held from before the run.
            PauseKeyPressed();

            bool finished = true;
            DateTime previousTime = DateTime.MinValue;

            void UpdateHud()
            {
                drawingCurrentTime = DateTime.Now;
                double elapsed = (drawingCurrentTime - drawingStartTime).TotalMilliseconds;
                int remaining = ActionQueue.AQueue.Count;
                int done = Math.Max(0, startLength - remaining);
                int eta = -1;
                if (done > 0 && remaining > 0)
                    eta = (int)(elapsed / done * remaining);
                Application.DoEvents();
                RunPBar.UpdateProgress(remaining, startLength, (int)elapsed, eta, pauseRequested);
            }

            while (ActionQueue.AQueue.Count > 0)
            {
                if (PauseKeyPressed())
                    SetPaused(!pauseRequested);

                if (pauseRequested)
                {
                    // Release the pen before holding, so moving the mouse while paused
                    // cannot drag a line across the canvas.
                    Mouse.DrawClick(0x04, Cursor.Position.X, Cursor.Position.Y, 0);
                    UpdateHud();
                    while (pauseRequested && !stopRequested && ActionQueue.AQueue.Count > 0)
                    {
                        if (PauseKeyPressed())
                            SetPaused(false);
                        Application.DoEvents();
                        Thread.Sleep(50);
                    }
                    previousTime = DateTime.MinValue;
                    UpdateHud();
                    previousTime = DateTime.Now;
                }

                if (ActionQueue.AQueue.Count == 0)
                    break;

                // Four HUD updates a second instead of one: it keeps the progress overlay
                // live and lets the window process repaints between strokes.
                if ((DateTime.Now - previousTime).TotalMilliseconds >= 250)
                {
                    UpdateHud();
                    previousTime = DateTime.Now;
                }
                bool keyStop = GetAsyncKeyState(0x21) < 0 || GetAsyncKeyState(0x12) < 0; // PageUp or Alt
                if (stopRequested || keyStop)
                {
                    Mouse.DrawClick(0x04, Cursor.Position.X, Cursor.Position.Y, 0);
                    string reason = stopReason
                        ?? (keyStop ? "PageUp (or Alt) key was pressed." : "STOP button was pressed.");
                    MessageBox.Show(new Form() { TopMost = true }, $"Drawing stopped by user.\n{reason}\n{ConvertMillisecondsToTimeFormat((int)(drawingCurrentTime - drawingStartTime).TotalMilliseconds)}", "Incomplete", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                    finished = false;
                    break;
                }


                Action action = ActionQueue.AQueue.Dequeue();
                action();
            }
            RunPBar.Close();
            SetPaused(false);

            if (finished)
            {
                MessageBox.Show(new Form() { TopMost = true }, "Drawing finished", "Finished", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
            }
            UpdateUITextValues();
        }


        private void UploadButton_Click(object sender, EventArgs e)
        {
            if (running || planRunning)
                return;

            // Show the Open File dialog. If the user clicks OK, load the
            // picture that the user chose.
            openFileDialog1.Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*";
            if (openFileDialog1.ShowDialog() != DialogResult.OK)
                return;

            Bitmap loaded;
            try
            {
                // Read through a stream so the file is not kept locked by the app.
                using (var stream = new FileStream(openFileDialog1.FileName, FileMode.Open, FileAccess.Read))
                using (var source = new Bitmap(stream))
                {
                    loaded = source.ConvertPixelFormat(PixelFormat.Format32bppArgb);
                }
            }
            catch (Exception)
            {
                MessageBox.Show(new Form() { TopMost = true }, "That file could not be loaded as an image.\nSupported formats: jpg, png, bmp, gif.", "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                return;
            }

            if (DrawImage.Original != null)
                DrawImage.Original.Dispose();

            DrawImage.Original = loaded;
            Settings.DrawW = DrawImage.Original.Width;
            Settings.DrawH = DrawImage.Original.Height;
            Settings.DrawX = 0;
            Settings.DrawY = 0;
            Settings.CropW = DrawImage.Original.Width;
            Settings.CropH = DrawImage.Original.Height;
            Settings.CropX = 0;
            Settings.CropY = 0;
            Settings.DirectDraw = false;
            UpdateUITextValues();
            DrawImage.Modified = DrawImage.Original;
            DrawImage.Palette = null;
            if (Config.CanvasKind != CanvasKind.Custom)
                CanvasTypeBox_SelectedIndexChanged(canvasTypeBox, EventArgs.Empty);
            ProcessImage();
            SetPreviewImage(BitmapExtensions.Resize(DrawImage.Modified, new Size(1920, 1080), scaleType, true));
            DrawImage.Preview = DrawImage.Modified;
            EnableControls(true);
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            SetPreviewImage(null);
            DrawImage.Original = null;
            DrawImage.Modified = null;
            DrawImage.Preview = null;
            DrawImage.Palette = null;
            EnableControls(false);
        }


        private async void StartButton_Click(object sender, EventArgs e)
        {
            // Set Drawing Position
            if (DrawImage.Original == null)
            {
                MessageBox.Show(new Form() { TopMost = true }, "No image was found. Load an image first.", "Image Error", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                return;
            }

            // The queue is pumped with DoEvents now, so a second Start click could otherwise
            // start a second run on top of the first one.
            if (running || planRunning)
                return;

            try
            {
                ProcessImage();
                // Show the transparent overlay so the draw area can be lined up with the canvas.
                if (!ShowDrawOverlay())
                    return;
                await StartAsync();
            }
            catch (Exception)
            {
                MessageBox.Show(new Form() { TopMost = true }, "No image was found", "Image Error", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
            }
        }


        private void firstLayerFill_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                Settings.FillFirstLayer = firstLayerFill.Checked;
            }
            catch (Exception)
            {
                Settings.FillFirstLayer = true;
            }
        }

        private void ditherBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {

                Settings.DitherPattern = ditherBox.SelectedIndex;
                ProcessImage();
            }
            catch (Exception)
            {
                Settings.DitherPattern = 0;
            }
        }

        private void OpenFileDialog1_FileOk(object sender, System.ComponentModel.CancelEventArgs e)
        {

        }

        public static Bitmap CropImageSet(Bitmap img)
        {
            if (img == null)
                return null;

            int width = img.Width;
            int height = img.Height;
            int left = Settings.CropX;
            int top = Settings.CropY;
            int right = Settings.CropW;
            int bottom = Settings.CropH;

            // Everything outside [left..right] x [top..bottom] is masked. When the crop
            // already covers the whole image there is nothing to mask, so the copy (and
            // the whole pass over it) can be skipped.
            if (left <= 0 && top <= 0 && right >= width - 1 && bottom >= height - 1)
                return img;

            Bitmap newImg = (Bitmap)img.Clone();

            // The old version called SetPixel once per pixel, which goes through the GDI+
            // interop layer every time and took ~0.4 s on a 1024x1024 image. Writing the
            // pixels through one locked buffer does the same work in a few milliseconds.
            Rectangle rect = new Rectangle(0, 0, width, height);
            BitmapData data = newImg.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int bytes = Math.Abs(stride) * height;
                byte[] pixels = new byte[bytes];
                Marshal.Copy(data.Scan0, pixels, 0, bytes);

                for (int y = 0; y < height; y++)
                {
                    int rowStart = y * stride;
                    bool rowMasked = y < top || y > bottom;
                    if (rowMasked)
                    {
                        for (int x = 0; x < width; x++)
                            MaskPixel(pixels, rowStart + x * 4);
                        continue;
                    }

                    for (int x = 0; x < left && x < width; x++)
                        MaskPixel(pixels, rowStart + x * 4);
                    for (int x = Math.Max(0, right + 1); x < width; x++)
                        MaskPixel(pixels, rowStart + x * 4);
                }

                Marshal.Copy(pixels, 0, data.Scan0, bytes);
            }
            finally
            {
                newImg.UnlockBits(data);
            }
            return newImg;
        }

        // The magic-marker magenta used to mark cropped-away pixels.
        private static void MaskPixel(byte[] pixels, int index)
        {
            pixels[index] = 255;     // B
            pixels[index + 1] = 0;   // G
            pixels[index + 2] = 255; // R
            pixels[index + 3] = 255; // A
        }
        public void SetPreview()
        {
            Bitmap previousPreview = DrawImage.Preview;
            DrawImage.Preview = (Bitmap)DrawImage.Modified.Clone();
            if (previousPreview != null &&
                !ReferenceEquals(previousPreview, overlayImage) &&
                !ReferenceEquals(previousPreview, DrawImage.Modified) &&
                !ReferenceEquals(previousPreview, DrawImage.Original))
            {
                try { previousPreview.Dispose(); } catch { }
            }
            SetPreviewImage(BitmapExtensions.Resize(DrawImage.Preview, new Size((int)Math.Round(DrawImage.Preview.Width * Settings.PenSize), (int)Math.Round(DrawImage.Preview.Height * Settings.PenSize)), scaleType, true));
        }

        // Replaces the canvas picture without leaking the bitmap it replaced. The old code
        // assigned a new full-size image on every processing run and left the previous one
        // alive, so memory and GDI usage kept climbing the longer the app stayed open.
        private void SetPreviewImage(Image image)
        {
            Image previous = pictureBox1.Image;
            if (ReferenceEquals(previous, image))
                return;

            pictureBox1.Image = image;
            if (previous == null)
                return;
            if (ReferenceEquals(previous, DrawImage.Original) ||
                ReferenceEquals(previous, DrawImage.Modified) ||
                ReferenceEquals(previous, DrawImage.Preview) ||
                ReferenceEquals(previous, overlayImage))
                return;
            try { previous.Dispose(); } catch { }
        }

        private async void estButton_Click(object sender, EventArgs e)
        {
            Time.EstimatedTime = 0;
            progressBar1.Value = 0;
            if (DrawImage.Original == null)
            {
                MessageBox.Show(new Form() { TopMost = true }, "No image was found. Upload an image first.", "Image Error", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                return;
            }
            if (running || planRunning)
                return;
            ProcessImage();
            bool planned = await PlanAsync();
            if (IsDisposed)
                return;
            if (!planned)
            {
                progressBar1.Value = 0;
                MessageBox.Show(new Form() { TopMost = true }, "No image was found. Upload an image first.", "Image Error", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                return;
            }
            progressBar1.Value = 100;
            estTime.Text = ConvertMillisecondsToTimeFormat(Time.EstimatedTime);

            // Report the shape of the plan so speed settings can be judged before a long run.
            RefreshPalettePreview();
            if (paletteLabel != null)
                paletteLabel.Text += $"  ·  {Stats.Strokes:N0} strokes  ·  longest stroke {Stats.LongestStrokePx:N0} px  ·  {Stats.TravelPx:N0} px travel";

            // The plan is not needed to run the estimate, so do not hold it in memory.
            ActionQueue.EmptyQueue();
        }

        public static string ConvertMillisecondsToTimeFormat(long milliseconds)
        {
            // Convert milliseconds to a TimeSpan
            TimeSpan timeSpan = TimeSpan.FromMilliseconds(milliseconds);

            // Format the TimeSpan as "dd:hh:mm:ss"
            string formattedTime = $"D{timeSpan.Days:D2}:H{timeSpan.Hours:D2}:M{timeSpan.Minutes:D2}:S{timeSpan.Seconds:D2}";

            return formattedTime;
        }

        private void widthInput_Leave(object sender, EventArgs e)
        {
            // Change in width of image
            try
            {
                Settings.DrawW = Convert.ToInt32(widthInput.Text);
                Settings.DrawW = int.Parse(widthInput.Text);
            }
            catch (Exception)
            {
                Settings.DrawW = DrawImage.Modified.Width;
            }
            ProcessImage();

        }

        private void heightInput_Leave(object sender, EventArgs e)
        {
            // Change in height of image
            try
            {
                Settings.DrawH = Convert.ToInt32(heightInput.Text);
                Settings.DrawH = int.Parse(heightInput.Text);
            }
            catch (Exception)
            {
                Settings.DrawH = DrawImage.Modified.Height;
            }
            ProcessImage();
        }

        private void maxColorsBox_Leave(object sender, EventArgs e)
        {
            try
            {
                Settings.MaxColors = Convert.ToInt32(maxColorsBox.Text);
                Settings.MaxColors = int.Parse(maxColorsBox.Text);
                if (Settings.MaxColors < 2)
                {
                    Settings.MaxColors = 2;
                    maxColorsBox.Text = "2";
                }

            }
            catch (Exception)
            {
                Settings.MaxColors = 2;
            }
            ProcessImage();
        }

        private void label6_Click(object sender, EventArgs e)
        {

        }

        private void pixelateBar_Scroll(object sender, EventArgs e)
        {
            Settings.Pixelation = Math.Pow(0.5, pixelateBar.Value - 1);
        }

        private void pixelateBar_MouseCaptureChanged(object sender, EventArgs e)
        {
            ProcessImage();
        }

        private void DrawPositionButton_Click_1(object sender, EventArgs e)
        {
            // Set Drawing Position
            try
            {
                ProcessImage();
                ShowDrawOverlay();
            }
            catch (Exception)
            {
                MessageBox.Show(new Form() { TopMost = true }, "No image was found", "Image Error", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
            }
        }

        // Shows a semi transparent overlay of the processed image so the draw area
        // can be dragged/resized into place over Rec Room. Returns true on confirm.
        private bool ShowDrawOverlay()
        {
            const int locationOffset = 20;
            const int sizeOffset = 40;

            if (DrawImage.Preview == null)
                return false;

            DrawPreviewForm overlay = new DrawPreviewForm(null)
            {
                Opacity = 0.5,
                TopMost = true,
                ShowInTaskbar = false
            };
            overlay.Size = new Size(Math.Max(1, Settings.DrawW) + sizeOffset, Math.Max(1, Settings.DrawH) + sizeOffset);
            overlay.Location = new Point(Settings.DrawX - locationOffset, Settings.DrawY - locationOffset);

            bool confirmed = false;
            bool cancelled = false;
            FormWindowState previousState = WindowState;

            overlay.setButton.Click += (_, _) => confirmed = true;
            overlay.setButtonTop.Click += (_, _) => confirmed = true;
            overlay.KeyPreview = true;
            overlay.KeyDown += (_, keyArgs) =>
            {
                if (keyArgs.KeyCode == Keys.Escape)
                    cancelled = true;
            };
            overlay.FormClosed += (_, _) => cancelled = true;

            using (var hint = new ToolTip())
            {
                string confirmHint = "Confirm the draw area.\nDrag the image or the red corner handles to move / resize.\n" +
                                    "Arrow keys nudge 1 px, Shift + arrows nudge 10 px.\nEsc cancels.";
                hint.SetToolTip(overlay.setButton, confirmHint);
                hint.SetToolTip(overlay.setButtonTop, confirmHint);

                overlay.Show();
                overlay.Activate();
                WindowState = FormWindowState.Minimized;

                Size lastSize = overlay.Size;
                long lastResizeAt = 0;
                bool resizePending = false;
                while (!confirmed && !cancelled && !overlay.IsDisposed)
                {
                    Application.DoEvents();

                    Settings.DrawX = overlay.Location.X + locationOffset;
                    Settings.DrawY = overlay.Location.Y + locationOffset;

                    if (overlay.Size != lastSize)
                    {
                        Settings.DrawW = Math.Max(1, overlay.Size.Width - sizeOffset);
                        Settings.DrawH = Math.Max(1, overlay.Size.Height - sizeOffset);
                        Settings.CropW = Settings.DrawW;
                        Settings.CropH = Settings.DrawH;
                        UpdateUITextValues();
                        lastSize = overlay.Size;
                        resizePending = true;
                        lastResizeAt = Environment.TickCount64;
                    }

                    // Reprocessing the image is far more expensive than a mouse move, and
                    // doing it for every step of a drag is what made the overlay crawl. Let
                    // the size settle, then process once.
                    if (resizePending && Environment.TickCount64 - lastResizeAt >= 150)
                    {
                        resizePending = false;
                        ProcessImage();
                        overlayImage = DrawImage.Preview;
                        overlay.UpdateImage(DrawImage.Preview);
                    }
                }

                // The confirmed size must be processed even if the drag ended mid-throttle.
                if (resizePending)
                {
                    ProcessImage();
                    overlayImage = DrawImage.Preview;
                }
                overlayImage = null;
            }

            if (!overlay.IsDisposed)
                overlay.Close();
            overlay.Dispose();
            WindowState = previousState == FormWindowState.Minimized ? FormWindowState.Normal : previousState;
            UpdateUITextValues();

            return confirmed;
        }

        private void YBox_Leave(object sender, EventArgs e)
        {
            // Delay interval between the drawing of each pixel
            try
            {
                Settings.DrawY = Convert.ToInt32(YBox.Text);
                Settings.DrawY = int.Parse(YBox.Text);
            }
            catch (Exception)
            {
                Settings.DrawY = 0;
            }
        }

        private void XBox_Leave(object sender, EventArgs e)
        {
            // Delay interval between the drawing of each pixel
            try
            {
                Settings.DrawX = Convert.ToInt32(XBox.Text);
                Settings.DrawX = int.Parse(XBox.Text);
            }
            catch (Exception)
            {
                Settings.DrawX = 0;
            }
        }

        private void skipColorBox_Leave(object sender, EventArgs e)
        {
            try
            {
                Settings.SkipColors = Convert.ToInt32(skipColorBox.Text);
                Settings.SkipColors = int.Parse(skipColorBox.Text);
            }
            catch (Exception)
            {
                Settings.SkipColors = 0;
            }
        }

        private void MainForm_Enter(object sender, EventArgs e)
        {

        }

        private void MainForm_Activated(object sender, EventArgs e)
        {
            // This used to re-run the whole quantize pipeline every time the window got
            // focus back (after a dialog, the overlay, Alt-Tab ...). Every setting that can
            // change the result already triggers ProcessImage itself, so that work was pure
            // waiting: several hundred milliseconds of freeze per activation.
            UpdateUITextValues();
        }

        private void bicubic_CheckedChanged(object sender, EventArgs e)
        {

            try
            {
                if (bicubicBox.Checked)
                {
                    processScaleType = ScalingMode.Bicubic;
                }
                else
                {
                    processScaleType = ScalingMode.NearestNeighbor;
                }
                ProcessImage();

            }
            catch (Exception)
            {
                processScaleType = ScalingMode.Box;
            }

        }

        private void tableLayoutPanel21_Paint(object sender, PaintEventArgs e)
        {

        }

        private void vectorBox_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                Settings.VectorMode = vectorBox.Checked;
            }
            catch (Exception)
            {
                Settings.VectorMode = false;
            }
        }

        private void directDrawBox_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                Settings.DirectDraw = directDrawBox.Checked;
            }
            catch (Exception)
            {
                Settings.DirectDraw = false;
            }
        }

        private void quantBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {

                Settings.QuantType = quantBox.SelectedIndex;
                ProcessImage();
            }
            catch (Exception)
            {
                Settings.QuantType = 0;
            }
        }

        private void randomBox_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                Settings.RandomOrder = randomBox.Checked;
            }
            catch (Exception)
            {
                Settings.RandomOrder = false;
            }
        }

        private void cropHnum_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                Settings.CropH = (int)cropHnum.Value;
            }
            catch (Exception)
            {
                Settings.CropH = 0;
            }
            ProcessImage();
        }

        private void cropYnum_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                Settings.CropY = (int)cropYnum.Value;
            }
            catch (Exception)
            {
                Settings.CropY = 0;
            }
            ProcessImage();
        }

        private void cropWnum_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                Settings.CropW = (int)cropWnum.Value;
            }
            catch (Exception)
            {
                Settings.CropW = 0;
            }
            ProcessImage();
        }

        private void cropXnum_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                Settings.CropX = (int)cropXnum.Value;
            }
            catch (Exception)
            {
                Settings.CropX = 0;
            }
            ProcessImage();
        }

        private void depthNumBox_ValueChanged(object sender, EventArgs e)
        {
            Settings.Depth = (int)depthNumBox.Value;
        }

        private void AddCanvasAndMarkerControls()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 204,
                Padding = new Padding(8, 6, 8, 6),
                BackColor = Color.FromArgb(245, 238, 228)
            };

            var canvasLabel = new Label { Text = "Canvas", AutoSize = true, Location = new Point(10, 12) };
            canvasTypeBox = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(70, 8),
                Width = 210
            };
            canvasTypeBox.Items.AddRange(new object[]
            {
                "Shirt canvas (custom shirts)",
                "Art canvas (paintings)",
                "Custom size"
            });
            canvasTypeBox.SelectedIndex = (int)Config.CanvasKind;
            canvasTypeBox.SelectedIndexChanged += CanvasTypeBox_SelectedIndexChanged;

            var penLabel = new Label { Text = "Pen", AutoSize = true, Location = new Point(290, 12) };
            penSizeBox = new NumericUpDown
            {
                Location = new Point(325, 8),
                Width = 50,
                Minimum = 1,
                Maximum = 20,
                DecimalPlaces = 1,
                Increment = 0.5M,
                Value = (decimal)Math.Clamp(Config.PenSize, 1f, 20f)
            };
            penSizeBox.ValueChanged += (_, _) =>
            {
                Settings.PenSize = (float)penSizeBox.Value;
                Config.PenSize = Settings.PenSize;
                Config.Save();
                if (DrawImage.Modified != null)
                    ProcessImage();
            };

            var delayLabel = new Label { Text = "Color delay ms", AutoSize = true, Location = new Point(390, 12) };
            colorDelayBox = new NumericUpDown
            {
                Location = new Point(490, 8),
                Width = 70,
                Minimum = 200,
                Maximum = 5000,
                Increment = 50,
                Value = Math.Clamp(Config.ColorChangeDelay, 200, 5000)
            };
            colorDelayBox.ValueChanged += (_, _) =>
            {
                Config.ColorChangeDelay = (int)colorDelayBox.Value;
                Time.ColorChangeDelay = Config.ColorChangeDelay;
                Config.Save();
            };

            var loadImageButton = new Button
            {
                Text = "Load image...",
                Location = new Point(600, 8),
                Width = 150,
                Height = 28
            };
            loadImageButton.Click += UploadButton_Click;

            var stopButton = new Button
            {
                Text = "STOP printing",
                Location = new Point(760, 8),
                Width = 120,
                Height = 28,
                BackColor = Color.IndianRed,
                FlatStyle = FlatStyle.Flat
            };
            stopButton.Click += (_, _) => RequestStop();

            calibrateColorButton = new Button
            {
                Text = "Calibrate Magic Marker colors",
                Location = new Point(10, 42),
                Width = 220,
                Height = 28
            };
            calibrateColorButton.Click += CalibrateColorButton_Click;

            testColorButton = new Button
            {
                Text = "Test custom color",
                Location = new Point(240, 42),
                Width = 150,
                Height = 28
            };
            testColorButton.Click += TestColorButton_Click;

            hexHashBox = new CheckBox
            {
                Text = "Paste #RRGGBB (custom hex)",
                AutoSize = true,
                Location = new Point(410, 46),
                Checked = Config.IncludeHash
            };
            hexHashBox.CheckedChanged += (_, _) =>
            {
                Config.IncludeHash = hexHashBox.Checked;
                Config.Save();
            };

            closePaletteBox = new CheckBox
            {
                Text = "Click palette DONE after color",
                AutoSize = true,
                Location = new Point(10, 76),
                Checked = Config.ClosePaletteAfterColor
            };
            closePaletteBox.CheckedChanged += (_, _) =>
            {
                Config.ClosePaletteAfterColor = closePaletteBox.Checked;
                Config.Save();
            };

            typeHexBox = new CheckBox
            {
                Text = "Type hex instead of paste",
                AutoSize = true,
                Location = new Point(240, 76),
                Checked = Config.TypeHexInsteadOfPaste
            };
            typeHexBox.CheckedChanged += (_, _) =>
            {
                Config.TypeHexInsteadOfPaste = typeHexBox.Checked;
                Config.Save();
            };

            rightClickOpenBox = new CheckBox
            {
                Text = "Right-click to open palette",
                AutoSize = true,
                Location = new Point(430, 76),
                Checked = Config.RightClickToOpenPalette
            };
            rightClickOpenBox.CheckedChanged += (_, _) =>
            {
                Config.RightClickToOpenPalette = rightClickOpenBox.Checked;
                Config.Save();
            };

            var tapModeBox = new CheckBox
            {
                Text = "Tap each pixel (no drag)",
                AutoSize = true,
                Location = new Point(600, 46),
                Checked = Config.TapMode
            };
            tapModeBox.CheckedChanged += (_, _) =>
            {
                Config.TapMode = tapModeBox.Checked;
                Settings.TapMode = tapModeBox.Checked;
                Config.Save();
            };

            var pressFBox = new CheckBox
            {
                Text = "Press F before color",
                AutoSize = true,
                Location = new Point(600, 76),
                Checked = Config.PressFToOpenPalette
            };
            pressFBox.CheckedChanged += (_, _) =>
            {
                Config.PressFToOpenPalette = pressFBox.Checked;
                Config.Save();
            };

            paletteWaitBox = new NumericUpDown
            {
                Location = new Point(760, 74),
                Width = 70,
                Minimum = 0,
                Maximum = 10000,
                Increment = 100,
                Value = Math.Clamp(Config.PaletteWaitMs, 0, 10000)
            };
            paletteWaitBox.ValueChanged += (_, _) =>
            {
                Config.PaletteWaitMs = (int)paletteWaitBox.Value;
                Config.Save();
            };

            paletteLabel = new Label
            {
                Text = "Print colors (Magic Marker custom, not black-only):",
                AutoSize = true,
                Location = new Point(10, 104)
            };
            palettePanel = new FlowLayoutPanel
            {
                Location = new Point(10, 124),
                Size = new Size(900, 36),
                WrapContents = false,
                AutoScroll = true,
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };

            var speedLabel = new Label { Text = "Speed %", AutoSize = true, Location = new Point(10, 170) };
            speedBox = new NumericUpDown
            {
                Location = new Point(75, 166),
                Width = 70,
                Minimum = 25,
                Maximum = 400,
                Increment = 25,
                Value = Math.Clamp(Config.SpeedPercent, 25, 400)
            };
            speedBox.ValueChanged += (_, _) =>
            {
                Config.SpeedPercent = (int)speedBox.Value;
                Time.SetSpeed(Config.SpeedPercent);
                Config.Save();
            };

            var gapLabel = new Label { Text = "Max gap px", AutoSize = true, Location = new Point(160, 170) };
            maxGapBox = new NumericUpDown
            {
                Location = new Point(245, 166),
                Width = 60,
                Minimum = 0,
                Maximum = 40,
                Value = Math.Clamp(Config.MaxGap, 0, 40)
            };
            maxGapBox.ValueChanged += (_, _) =>
            {
                Config.MaxGap = (int)maxGapBox.Value;
                Settings.MaxGap = Config.MaxGap;
                Config.Save();
            };

            pauseButton = new Button
            {
                Text = "Pause (F8)",
                Location = new Point(320, 164),
                Width = 120,
                Height = 28
            };
            pauseButton.Click += (_, _) => SetPaused(!pauseRequested);

            // Explain the strategy options instead of leaving them as bare checkboxes.
            optionTips = new ToolTip();
            optionTips.SetToolTip(bicubicBox,
                "On = bicubic resize: smooth blends, but it invents in-between colors.\n" +
                "Off = nearest neighbor: keeps the exact colors and crisp pixel edges.");
            optionTips.SetToolTip(directDrawBox,
                "On = a stroke stops at the first pixel that is a different color (accurate, more strokes).\n" +
                "Off = a stroke may bridge gaps of up to Max gap px (faster, slightly looser).");
            optionTips.SetToolTip(vectorBox,
                "On = strokes may also travel diagonally, so diagonals are one drag instead of steps.");
            optionTips.SetToolTip(randomBox,
                "On = visit the pixels of each color in random order instead of column by column.");
            optionTips.SetToolTip(tapModeBox,
                "On = every pixel is its own click instead of a held drag. Slower, but it cannot streak.");
            optionTips.SetToolTip(pressFBox,
                "Press F to open the Magic Marker palette before every color change.\n" +
                "The box on the right is how long Rec Room gets to load that window.");
            optionTips.SetToolTip(paletteWaitBox,
                "Milliseconds to wait after pressing F before touching the palette.");
            optionTips.SetToolTip(speedBox,
                "Scales the mouse delays of every stroke. 100% = default, higher = faster, lower = more careful.\n" +
                "Above ~200% the mouse can outrun the game and separate strokes may join into one line.");
            optionTips.SetToolTip(maxGapBox,
                "Total pixels of another color one stroke may cross. 0 = never: the stroke ends " +
                "at the first foreign pixel, the pen lifts, and the rest becomes a new stroke.");
            optionTips.SetToolTip(pauseButton,
                "Hold the queue without losing it. F8 or the Pause key also toggles it mid-print.");

            panel.Controls.AddRange(new Control[]
            {
                canvasLabel, canvasTypeBox, penLabel, penSizeBox, delayLabel, colorDelayBox, loadImageButton, stopButton,
                calibrateColorButton, testColorButton, hexHashBox, closePaletteBox, typeHexBox, rightClickOpenBox,
                tapModeBox, pressFBox, paletteWaitBox, paletteLabel, palettePanel,
                speedLabel, speedBox, gapLabel, maxGapBox, pauseButton
            });

            Controls.Add(panel);
            panel.BringToFront();
            MinimumSize = new Size(Math.Max(MinimumSize.Width, 980), MinimumSize.Height + 204);
        }

        private void LoadSavedUi()
        {
            Settings.PenSize = Config.PenSize > 0 ? Config.PenSize : 3;
            Settings.MaxGap = Math.Clamp(Config.MaxGap, 0, 40);
            Time.SetSpeed(Math.Clamp(Config.SpeedPercent, 25, 400));
            Time.ColorChangeDelay = Config.ColorChangeDelay;
            if (quantBox.Items.Count > 0 && quantBox.SelectedIndex < 0)
                quantBox.SelectedIndex = 0;
        }

        private void CanvasTypeBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            Config.CanvasKind = (CanvasKind)Math.Clamp(canvasTypeBox.SelectedIndex, 0, 2);
            Config.Save();
            Size size = CanvasProfiles.DefaultSize(Config.CanvasKind);
            if (DrawImage.Original == null)
            {
                Settings.DrawW = size.Width;
                Settings.DrawH = size.Height;
                UpdateUITextValues();
                return;
            }

            if (Config.CanvasKind == CanvasKind.Custom)
                return;

            double aspect = DrawImage.Original.Width / (double)Math.Max(1, DrawImage.Original.Height);
            double presetAspect = size.Width / (double)size.Height;
            if (aspect >= presetAspect)
            {
                Settings.DrawW = size.Width;
                Settings.DrawH = Math.Max(1, (int)Math.Round(size.Width / aspect));
            }
            else
            {
                Settings.DrawH = size.Height;
                Settings.DrawW = Math.Max(1, (int)Math.Round(size.Height * aspect));
            }
            Settings.CropX = 0;
            Settings.CropY = 0;
            Settings.CropW = Settings.DrawW;
            Settings.CropH = Settings.DrawH;
            UpdateUITextValues();
            ProcessImage();
        }

        private void CalibrateColorButton_Click(object sender, EventArgs e)
        {
            MessageBox.Show(
                "Equip Magic Marker in Rec Room and open the color menu.\n\n" +
                "For each step, hover the mouse over the Rec Room control until it captures.\n" +
                "You can press Enter to capture immediately.",
                "Calibrate Magic Marker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            RecRoomWindow.Focus();
            Rectangle bounds = RecRoomWindow.Bounds();

            if (!CapturePoint("1 / 4  CUSTOM",
                    "Hover over CUSTOM on the Magic Marker palette.",
                    out Point custom))
                return;
            SavedConfig.Store(bounds, custom, out double cx, out double cy);

            // Open the CUSTOM COLOR window ourselves, otherwise the next two points are
            // captured from whatever happens to be on screen and DONE gets missed later.
            Mouse.DrawClick(0x02, custom.X, custom.Y, 60);
            Mouse.DrawClick(0x04, custom.X, custom.Y, 60);
            Thread.Sleep(Math.Max(700, Config.ColorChangeDelay));

            if (!CapturePoint("2 / 4  Hex field",
                    "Hover over the hex box of the open CUSTOM COLOR window (example #42522B).",
                    out Point hex))
                return;
            SavedConfig.Store(bounds, hex, out double hx, out double hy);

            if (!CapturePoint("3 / 4  Custom Color DONE",
                    "Hover over DONE on the same CUSTOM COLOR window.",
                    out Point done))
                return;
            SavedConfig.Store(bounds, done, out double dx, out double dy);

            // DONE sits below the hex box in Rec Room's dialog; if the captured point is not,
            // the click would land somewhere else and the color would never be applied.
            if (done.Y <= hex.Y || Math.Abs(done.X - hex.X) > bounds.Width * 0.25)
            {
                MessageBox.Show(
                    "The DONE point is not below the hex box, so the click would miss the button.\n\n" +
                    "Open the CUSTOM COLOR window (click CUSTOM on the Magic Marker palette), then run " +
                    "the calibration again and hover directly over its DONE button.\n\nNothing was saved.",
                    "Calibration looks wrong", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Config.CustomButtonX = cx;
            Config.CustomButtonY = cy;
            Config.HexFieldX = hx;
            Config.HexFieldY = hy;
            Config.CustomDoneX = dx;
            Config.CustomDoneY = dy;

            if (CapturePoint("4 / 4  Palette DONE",
                    "Hover over DONE on the Magic Marker palette (Skip if unused).",
                    out Point paletteDone))
            {
                SavedConfig.Store(bounds, paletteDone, out double px, out double py);
                Config.PaletteDoneX = px;
                Config.PaletteDoneY = py;
                Config.ClosePaletteAfterColor = true;
                closePaletteBox.Checked = true;
            }

            Config.Save();
            MessageBox.Show("Magic Marker custom color clicks saved.\nUse Test custom color to verify a non-black hex.",
                "Calibration saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static bool CapturePoint(string title, string instruction, out Point point)
        {
            using var form = new CalibrationForm(title, instruction);
            DialogResult result = form.ShowDialog();
            point = form.Captured;
            return result == DialogResult.OK && !form.Skipped;
        }

        private void TestColorButton_Click(object sender, EventArgs e)
        {
            if (running || planRunning)
                return;

            using var dialog = new ColorDialog
            {
                FullOpen = true,
                Color = Color.FromArgb(255, 66, 82, 43)
            };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            ActionQueue.EmptyQueue();
            Time.ColorChangeDefault();
            Time.ColorChangeDelay = Config.ColorChangeDelay;
            SetPenColor(dialog.Color);

            if (!RecRoomWindow.Focus())
                ActivateWindow("Rec Room");
            Thread.Sleep(400);
            RunQueue();
        }

        private void RefreshPalettePreview()
        {
            if (palettePanel == null || DrawImage.Modified == null)
                return;

            palettePanel.SuspendLayout();

            // Clearing the panel left every old swatch and its own ToolTip alive: each run
            // added a hidden native tooltip window that was never removed again.
            Control[] oldSwatches = palettePanel.Controls.Cast<Control>().ToArray();
            palettePanel.Controls.Clear();
            foreach (Control oldSwatch in oldSwatches)
                oldSwatch.Dispose();

            Color[] palette = DrawImage.Palette ?? ImageToPallet(DrawImage.Modified);
            int shown = 0;
            foreach (Color color in palette)
            {
                if (color.A < 16 || color == Color.FromArgb(255, 255, 0, 255))
                    continue;
                var swatch = new Panel
                {
                    Width = 28,
                    Height = 28,
                    Margin = new Padding(2),
                    BackColor = Color.FromArgb(255, color.R, color.G, color.B),
                    BorderStyle = BorderStyle.FixedSingle
                };
                string hex = MagicMarker.ToRecRoomHex(color, true);
                paletteTip ??= new ToolTip();
                paletteTip.SetToolTip(swatch, hex);
                palettePanel.Controls.Add(swatch);
                shown++;
            }
            if (shown == 0)
            {
                paletteLabel.Text = DrawImage.Original == null
                    ? "Print colors: none (load an image)"
                    : "Print colors: none — this image is fully transparent, so there is nothing to draw";
            }
            else
            {
                paletteLabel.Text = $"Print colors: {shown} custom Magic Marker hex values (not black-only)";
            }
            palettePanel.ResumeLayout();
        }
    }
}
