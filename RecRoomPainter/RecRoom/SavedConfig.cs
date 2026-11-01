using System;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RecRoomPainter
{
    public enum CanvasKind
    {
        Shirt = 0,
        Art = 1,
        Custom = 2
    }

    public sealed class SavedConfig
    {
        public double CustomButtonX { get; set; } = 0.58;
        public double CustomButtonY { get; set; } = 0.70;
        public double HexFieldX { get; set; } = 0.62;
        public double HexFieldY { get; set; } = 0.58;
        public double CustomDoneX { get; set; } = 0.64;
        public double CustomDoneY { get; set; } = 0.64;
        public double PaletteDoneX { get; set; } = 0.64;
        public double PaletteDoneY { get; set; } = 0.70;
        public bool ClosePaletteAfterColor { get; set; } = true;
        public bool IncludeHash { get; set; } = true;
        public bool TypeHexInsteadOfPaste { get; set; } = false;
        public bool RightClickToOpenPalette { get; set; } = false;
        public int ColorChangeDelay { get; set; } = 900;
        public CanvasKind CanvasKind { get; set; } = CanvasKind.Shirt;
        public float PenSize { get; set; } = 3f;
        public bool TapMode { get; set; } = false;
        public bool PressFToOpenPalette { get; set; } = true;
        public int PaletteWaitMs { get; set; } = 2000;

        // Stroke pacing multiplier (100 = default delays, higher = faster drawing).
        public int SpeedPercent { get; set; } = 100;

        // Total pixels of a different color one stroke may cross (0 = never).
        public int MaxGap { get; set; } = 0;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RecRoomPainter", "settings.json");

        public static SavedConfig Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<SavedConfig>(File.ReadAllText(FilePath), JsonOptions) ?? new SavedConfig();
            }
            catch
            {
                // keep defaults
            }
            return new SavedConfig();
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }

        public Point CustomButton(Rectangle bounds) => Map(bounds, CustomButtonX, CustomButtonY);
        public Point HexField(Rectangle bounds) => Map(bounds, HexFieldX, HexFieldY);
        public Point CustomDone(Rectangle bounds) => Map(bounds, CustomDoneX, CustomDoneY);
        public Point PaletteDone(Rectangle bounds) => Map(bounds, PaletteDoneX, PaletteDoneY);

        public static Point Map(Rectangle bounds, double x, double y) =>
            new(bounds.Left + (int)Math.Round(bounds.Width * x),
                bounds.Top + (int)Math.Round(bounds.Height * y));

        public static void Store(Rectangle bounds, Point screen, out double x, out double y)
        {
            x = bounds.Width <= 0 ? 0.5 : Math.Clamp((screen.X - bounds.Left) / (double)bounds.Width, 0, 1);
            y = bounds.Height <= 0 ? 0.5 : Math.Clamp((screen.Y - bounds.Top) / (double)bounds.Height, 0, 1);
        }
    }

    public static class CanvasProfiles
    {
        public static Size DefaultSize(CanvasKind kind) => kind switch
        {
            CanvasKind.Shirt => new Size(256, 320),
            CanvasKind.Art => new Size(400, 400),
            _ => new Size(300, 300)
        };

        public static string Description(CanvasKind kind) => kind switch
        {
            CanvasKind.Shirt => "Shirt canvas (custom shirts / mannequin)",
            CanvasKind.Art => "Art canvas (wall / easel paintings)",
            _ => "Custom size"
        };
    }
}
