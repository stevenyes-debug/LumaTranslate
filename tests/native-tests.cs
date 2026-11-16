using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using LumaTranslate;

// These checks create no visible windows, read no screenshots and never alter the clipboard.
// Ctrl+Alt+Shift+F7..F11 are registered briefly, then released in all normal/error paths.
internal static class NativeTests
{
    private static int passed;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        passed++;
        Console.WriteLine("PASS: " + description);
    }

    [STAThread]
    private static int Main(string[] arguments)
    {
        try
        {
            string evidenceDirectory = arguments.Length > 0 ? Path.GetFullPath(arguments[0]) : AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(evidenceDirectory);
            TestHotkeys();
            TestOcr(evidenceDirectory);
            TestNonActivatingOverlay(arguments.Length > 1 ? arguments[1] : null);
            TestDirectPixelsAndTimings(evidenceDirectory);
            Console.WriteLine("Native tests passed: " + passed + " checks.");
            Console.WriteLine("No live screen selection, mixed-DPI interaction, or clipboard-copy checks were performed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            return 1;
        }
    }

    private static void TestHotkeys()
    {
        Action callback = delegate { };
        string error;
        using (HotkeyService service = new HotkeyService())
        {
            Check(!service.Apply("Ctrl+A", "Ctrl+A", callback, callback, out error), "duplicate gestures rejected");
            Check(!service.Apply("A", "Ctrl+Alt+D", callback, callback, out error), "unmodified key rejected");
            Check(!service.Apply("Ctrl+Ctrl+A", "Ctrl+Alt+D", callback, callback, out error), "duplicate modifier rejected");
            Check(!service.Apply("Ctrl+F12", "Ctrl+Alt+D", callback, callback, out error), "Windows reserved F12 rejected");
            Check(!service.Apply("Ctrl+Alt+A", "Ctrl+Alt+D", null, callback, out error), "missing action rejected");
            Check(service.Apply("Ctrl+Alt+Shift+F9", "Ctrl+Alt+Shift+F10", callback, callback, out error),
                "initial registration (if this fails, close apps using Ctrl+Alt+Shift+F9/F10): " + error);
            Check(service.Apply("Ctrl+Alt+Shift+F10", "Ctrl+Alt+Shift+F9", callback, callback, out error), "shortcut swap succeeds: " + error);

            Check(RegisterHotKey(IntPtr.Zero, 4987, 1 | 2 | 4 | 0x4000, 0x7A), "reserve conflicting Ctrl+Alt+Shift+F11 test key");
            try
            {
                Check(!service.Apply("Ctrl+Alt+Shift+F8", "Ctrl+Alt+Shift+F11", callback, callback, out error), "conflicting registration is rejected");
                Check(!String.IsNullOrWhiteSpace(error), "registration conflict has a user-facing explanation");
                using (HotkeyService probe = new HotkeyService())
                    Check(!probe.Apply("Ctrl+Alt+Shift+F9", "Ctrl+Alt+Shift+F7", callback, callback, out error), "previous shortcut survives failed update");
                bool temporaryReleased = RegisterHotKey(IntPtr.Zero, 4988, 1 | 2 | 4 | 0x4000, 0x77);
                try { Check(temporaryReleased, "partially registered new shortcut is released during rollback"); }
                finally { if (temporaryReleased) UnregisterHotKey(IntPtr.Zero, 4988); }
            }
            finally { UnregisterHotKey(IntPtr.Zero, 4987); }
        }

        bool disposedReleased = RegisterHotKey(IntPtr.Zero, 4989, 1 | 2 | 4 | 0x4000, 0x78);
        try { Check(disposedReleased, "Dispose releases the original shortcut"); }
        finally { if (disposedReleased) UnregisterHotKey(IntPtr.Zero, 4989); }
    }

    private static string Recognize(string path, string language, CancellationToken token)
    {
        // OCR is supported on an MTA worker; this also avoids blocking an STA dispatcher continuation.
        return Task.Run(async delegate { return await ScreenOcr.RecognizeFileAsync(path, language, token); }).GetAwaiter().GetResult();
    }

    private static void TestOcr(string evidenceDirectory)
    {
        string[] languages = ScreenOcr.AvailableLanguages();
        Console.WriteLine("Installed Windows OCR languages: " + String.Join(", ", languages));
        Check(languages.Length > 0, "at least one Windows OCR language pack is installed");
        string recognitionLanguage = "auto";
        foreach (string language in languages)
            if (language.StartsWith("en", StringComparison.OrdinalIgnoreCase)) { recognitionLanguage = language; break; }

        string textImage = Path.Combine(evidenceDirectory, "native-ocr-text.png");
        string blankImage = Path.Combine(evidenceDirectory, "native-ocr-blank.png");
        using (Bitmap image = new Bitmap(1700, 240))
        using (Graphics graphics = Graphics.FromImage(image))
        using (Font font = new Font("Arial", 38, FontStyle.Regular))
        {
            graphics.Clear(Color.White);
            image.Save(blankImage, System.Drawing.Imaging.ImageFormat.Png);
            graphics.DrawString("HELLO WORLD. TRANSLATION MADE SIMPLE.", font, Brushes.Black, 45, 75);
            image.Save(textImage, System.Drawing.Imaging.ImageFormat.Png);
        }

        string recognized = Recognize(textImage, recognitionLanguage, CancellationToken.None);
        Console.WriteLine("Recognized local image: " + recognized);
        File.WriteAllText(Path.Combine(evidenceDirectory, "native-ocr-result.txt"), recognized);
        Check(recognized.IndexOf("HELLO", StringComparison.OrdinalIgnoreCase) >= 0 &&
            recognized.IndexOf("TRANSLATION", StringComparison.OrdinalIgnoreCase) >= 0 &&
            recognized.IndexOf("SIMPLE", StringComparison.OrdinalIgnoreCase) >= 0, "real local OCR recognizes all three separated fixture words");

        bool canceled = false;
        try { Recognize(textImage, recognitionLanguage, new CancellationToken(true)); }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled, "canceled OCR request stops before recognition");

        bool blankRejected = false;
        try { Recognize(blankImage, recognitionLanguage, CancellationToken.None); }
        catch (InvalidOperationException exception) { blankRejected = !String.IsNullOrWhiteSpace(exception.Message); }
        Check(blankRejected, "blank image reports that no text was recognized");

        bool missingRejected = false;
        try { Recognize(Path.Combine(evidenceDirectory, "missing-" + Guid.NewGuid().ToString("N") + ".png"), recognitionLanguage, CancellationToken.None); }
        catch (FileNotFoundException) { missingRejected = true; }
        Check(missingRejected, "missing image file is reported");
    }

    private static void TestNonActivatingOverlay(string sourcePath)
    {
        Assembly assembly = typeof(ScreenOcr).Assembly;
        Type overlayType = assembly.GetType("LumaTranslate.CaptureOverlay", true);
        byte[] pixels = new byte[640 * 480 * 4];
        System.Windows.Media.Imaging.BitmapSource image = System.Windows.Media.Imaging.BitmapSource.Create(640, 480, 96, 96,
            System.Windows.Media.PixelFormats.Bgr32, null, pixels, 640 * 4);
        image.Freeze();
        IntPtr foreground = GetForegroundWindow();
        System.Windows.Window overlay = (System.Windows.Window)Activator.CreateInstance(overlayType, new object[] { image, new Rectangle(0, 0, 640, 480) });
        try
        {
            Check(!overlay.ShowActivated, "capture overlay does not request foreground activation");
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(overlay).EnsureHandle();
            long style = GetWindowLongPtr(handle, -20).ToInt64();
            Check((style & 0x08000000L) != 0, "capture HWND carries WS_EX_NOACTIVATE");
            Check((style & 0x00000080L) != 0, "capture HWND is a tool window");
            Check(!IsWindowVisible(handle), "initializing capture HWND does not show any window");
            Check(GetForegroundWindow() == foreground, "initializing capture HWND preserves the current foreground application");
            MethodInfo mouseActivate = overlayType.GetMethod("PreventMouseActivation", BindingFlags.Static | BindingFlags.NonPublic);
            object[] message = new object[] { handle, 0x0021, IntPtr.Zero, IntPtr.Zero, false };
            IntPtr response = (IntPtr)mouseActivate.Invoke(null, message);
            Check(response.ToInt32() == 3 && (bool)message[4], "mouse click is accepted without activating the overlay");
        }
        finally { overlay.Close(); }

        if (!String.IsNullOrWhiteSpace(sourcePath))
        {
            string source = File.ReadAllText(sourcePath);
            Check(!Regex.IsMatch(source, @"\b(?:ShowWindow|ShowWindowAsync|SetForegroundWindow|BringWindowToTop|Activate|Focus)\s*\("),
                "capture source has no window activation, hiding, or ShowWindow calls");
            Check(source.IndexOf("SW_MINIMIZE", StringComparison.Ordinal) < 0 && source.IndexOf("WindowState.Minimized", StringComparison.Ordinal) < 0,
                "capture source has no minimize operation");
        }
    }

    private static MethodInfo OcrMethod(string name)
    { return typeof(ScreenOcr).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static); }

    private static string RecognizeCrop(Bitmap image, Rectangle crop, string language, CancellationToken token)
    {
        return Task.Run(async delegate
        {
            Task<string> task = (Task<string>)OcrMethod("RecognizeCropAsync").Invoke(null, new object[] { image, crop, language, token });
            return await task;
        }).GetAwaiter().GetResult();
    }

    private static IDisposable PrepareDirect(Bitmap image, Rectangle crop)
    { return (IDisposable)OcrMethod("CreateOcrBitmap").Invoke(null, new object[] { image, crop, CancellationToken.None }); }

    private static IDisposable PrepareLegacy(Bitmap image, Rectangle crop)
    {
        using (Bitmap cropped = image.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (MemoryStream png = new MemoryStream())
        {
            cropped.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            Task task = (Task)OcrMethod("DecodeBitmapAsync").Invoke(null, new object[] { png.ToArray(), CancellationToken.None });
            task.GetAwaiter().GetResult();
            return (IDisposable)task.GetType().GetProperty("Result").GetValue(task, null);
        }
    }

    private static double Median(double[] values)
    { Array.Sort(values); return values[values.Length / 2]; }

    private static void TestDirectPixelsAndTimings(string evidenceDirectory)
    {
        using (Bitmap image = new Bitmap(2200, 1250, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (Graphics graphics = Graphics.FromImage(image))
        using (Font font = new Font("Arial", 38))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("EXCLUDED TEXT", font, Brushes.Black, 70, 70);
            graphics.DrawString("SELECTED TRANSLATION WORKS", font, Brushes.Black, 230, 475);
            Rectangle crop = new Rectangle(180, 420, 1650, 220);
            string result = RecognizeCrop(image, crop, "en", CancellationToken.None);
            Check(result.IndexOf("SELECTED", StringComparison.OrdinalIgnoreCase) >= 0 && result.IndexOf("WORKS", StringComparison.OrdinalIgnoreCase) >= 0,
                "direct BGRA crop recognizes selected text with a nonzero crop origin");
            Check(result.IndexOf("EXCLUDED", StringComparison.OrdinalIgnoreCase) < 0, "direct crop excludes text outside the selected region");
            bool canceled = false;
            try { RecognizeCrop(image, crop, "en", new CancellationToken(true)); }
            catch (OperationCanceledException) { canceled = true; }
            Check(canceled, "direct crop honors cancellation");

            object first = OcrMethod("GetRecognizer").Invoke(null, new object[] { "en" });
            object second = OcrMethod("GetRecognizer").Invoke(null, new object[] { "EN" });
            Check(Object.ReferenceEquals(first, second), "language-normalized requests reuse one recognizer");
            using (Bitmap imageA = (Bitmap)image.Clone())
            using (Bitmap imageB = (Bitmap)image.Clone())
            {
                Task<string> a = Task.Run(delegate { return RecognizeCrop(imageA, crop, "en", CancellationToken.None); });
                Task<string> b = Task.Run(delegate { return RecognizeCrop(imageB, crop, "en", CancellationToken.None); });
                Task.WaitAll(a, b);
                Check(a.Result.IndexOf("SELECTED", StringComparison.OrdinalIgnoreCase) >= 0 && b.Result.IndexOf("SELECTED", StringComparison.OrdinalIgnoreCase) >= 0,
                    "concurrent requests safely share the cached recognizer");
            }

            Rectangle timingCrop = new Rectangle(0, 0, 1920, 1080);
            using (PrepareDirect(image, timingCrop)) { }
            using (PrepareLegacy(image, timingCrop)) { }
            double[] direct = new double[7];
            double[] legacy = new double[7];
            for (int iteration = 0; iteration < 7; iteration++)
            {
                Stopwatch watch = Stopwatch.StartNew();
                using (PrepareDirect(image, timingCrop)) { }
                direct[iteration] = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                using (PrepareLegacy(image, timingCrop)) { }
                legacy[iteration] = watch.Elapsed.TotalMilliseconds;
            }
            string timings = "1920x1080 generated image, seven warm runs, image preparation only (no OCR/network):" + Environment.NewLine +
                "Direct BGRA median ms: " + Median(direct).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine +
                "Legacy crop + PNG encode/decode median ms: " + Median(legacy).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + Environment.NewLine +
                "Unconditional pre-capture waits removed: 35 ms + 90 ms; capture now waits for one DWM flush.";
            Console.WriteLine(timings);
            File.WriteAllText(Path.Combine(evidenceDirectory, "capture-timings.txt"), timings);
        }

        // OCR must still downscale oversized monitor crops before passing them to Windows.
        using (Bitmap wide = new Bitmap(6000, 300, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (Graphics graphics = Graphics.FromImage(wide))
        using (Font font = new Font("Arial", 68))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("LARGE SCREEN CAPTURE", font, Brushes.Black, 120, 85);
            string result = RecognizeCrop(wide, new Rectangle(0, 0, wide.Width, wide.Height), "en", CancellationToken.None);
            Check(result.IndexOf("CAPTURE", StringComparison.OrdinalIgnoreCase) >= 0, "oversized pixel crop is downscaled and recognized");
        }
    }
}
