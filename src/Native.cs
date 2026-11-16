using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using WinRtBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace LumaTranslate
{
    /// <summary>Owns global shortcuts without taking focus away from the source application.</summary>
    public sealed class HotkeyService : IDisposable
    {
        private sealed class Binding
        {
            public int Id;
            public uint Modifiers;
            public uint Key;
            public Action Callback;
            public string Name;
            public bool Same(Binding other) { return Modifiers == other.Modifiers && Key == other.Key; }
        }

        private readonly HwndSource source;
        private List<Binding> bindings = new List<Binding>();
        private int nextId = 100;
        private bool disposed;

        public HotkeyService()
        {
            HwndSourceParameters parameters = new HwndSourceParameters("LumaTranslate.Shortcuts");
            parameters.ParentWindow = new IntPtr(-3); // HWND_MESSAGE, never visible in the taskbar.
            parameters.WindowStyle = 0;
            source = new HwndSource(parameters);
            source.AddHook(WindowProc);
        }

        public bool Apply(string captureGesture, string selectionGesture, Action capture, Action selection, out string error)
        {
            source.Dispatcher.VerifyAccess();
            error = null;
            if (disposed) { error = "快捷键服务已关闭。"; return false; }
            Binding first;
            Binding second;
            if (!TryParse(captureGesture, capture, "截图翻译", out first, out error) ||
                !TryParse(selectionGesture, selection, "划词翻译", out second, out error)) return false;
            if (first.Same(second)) { error = "截图翻译和划词翻译需要使用不同的快捷键。"; return false; }

            List<Binding> proposed = new List<Binding> { first, second };
            List<Binding> newlyRegistered = new List<Binding>();
            // Keep current registrations alive until every new key is registered. A conflict cannot
            // leave the application without its old shortcuts; swapping the two shortcuts also works.
            foreach (Binding binding in proposed)
            {
                Binding retained = bindings.FirstOrDefault(delegate(Binding old) { return old.Same(binding); });
                if (retained != null) { binding.Id = retained.Id; continue; }
                binding.Id = nextId++;
                if (!NativeMethods.RegisterHotKey(source.Handle, binding.Id, binding.Modifiers | 0x4000, binding.Key))
                {
                    int nativeError = Marshal.GetLastWin32Error();
                    foreach (Binding fresh in newlyRegistered) NativeMethods.UnregisterHotKey(source.Handle, fresh.Id);
                    error = binding.Name + "快捷键无法注册，可能已被其他程序占用。请换一个组合。" +
                        (nativeError == 1409 ? "" : "（Windows 错误 " + nativeError.ToString(CultureInfo.InvariantCulture) + "）");
                    return false;
                }
                newlyRegistered.Add(binding);
            }
            foreach (Binding old in bindings)
                if (!proposed.Any(delegate(Binding next) { return next.Id == old.Id; }))
                    NativeMethods.UnregisterHotKey(source.Handle, old.Id);
            bindings = proposed;
            return true;
        }

        private static bool TryParse(string gesture, Action callback, string name, out Binding binding, out string error)
        {
            binding = null;
            error = null;
            if (callback == null) { error = name + "操作未配置。"; return false; }
            uint modifiers = 0;
            uint virtualKey = 0;
            string[] parts = (gesture ?? "").Split('+');
            foreach (string raw in parts)
            {
                string part = raw.Trim();
                string lower = part.ToLowerInvariant();
                uint modifier = lower == "ctrl" || lower == "control" ? 2u :
                    lower == "alt" ? 1u : lower == "shift" ? 4u :
                    lower == "win" || lower == "windows" ? 8u : 0u;
                if (modifier != 0)
                {
                    if ((modifiers & modifier) != 0) { error = name + "快捷键有重复的修饰键。"; return false; }
                    modifiers |= modifier;
                    continue;
                }
                if (virtualKey != 0 || part.Length == 0) { error = name + "快捷键格式无效，例如 Ctrl+Alt+A。"; return false; }
                if (part.Length == 1 && char.IsLetterOrDigit(part[0]) && part[0] < 128)
                    virtualKey = (uint)char.ToUpperInvariant(part[0]);
                else
                {
                    try
                    {
                        Key key = (Key)new KeyConverter().ConvertFromInvariantString(part);
                        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
                    }
                    catch (Exception) { error = name + "快捷键包含无法识别的按键：" + part; return false; }
                }
            }
            if (virtualKey == 0 || (modifiers & (1u | 2u | 8u)) == 0)
            {
                error = name + "快捷键需要包含 Ctrl、Alt 或 Win，以及一个普通按键，例如 Ctrl+Alt+A。";
                return false;
            }
            if (virtualKey == 0x7B) { error = "F12 是 Windows 保留按键，请使用其他快捷键。"; return false; }
            if (virtualKey == 0x10 || virtualKey == 0x11 || virtualKey == 0x12 || virtualKey == 0x5B || virtualKey == 0x5C)
            { error = name + "快捷键还需要一个普通按键。"; return false; }
            binding = new Binding { Modifiers = modifiers, Key = virtualKey, Callback = callback, Name = name };
            return true;
        }

        private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0312)
            {
                int id = wParam.ToInt32();
                Binding binding = bindings.FirstOrDefault(delegate(Binding value) { return value.Id == id; });
                if (binding != null)
                {
                    handled = true;
                    source.Dispatcher.BeginInvoke(binding.Callback);
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (disposed) return;
            source.Dispatcher.VerifyAccess();
            disposed = true;
            foreach (Binding binding in bindings) NativeMethods.UnregisterHotKey(source.Handle, binding.Id);
            bindings.Clear();
            source.RemoveHook(WindowProc);
            source.Dispose();
        }
    }

    public static class SelectionReader
    {
        private sealed class ClipboardSnapshot
        {
            public IDataObject Data;
            public uint Sequence;
        }

        public static async Task<string> ReadAsync(CancellationToken token)
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("划词翻译需要从桌面界面线程启动。");
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero) throw new InvalidOperationException("请先在其他应用中选中文字。");
            await WaitForReleasedKeysAsync(token);
            EnsureForeground(foreground);

            // Accessibility avoids changing the clipboard and works in modern browser/edit controls.
            // Some applications have unresponsive accessibility providers, so use a bounded wait.
            Task<string> automation = Task.Run(delegate { return ReadAutomationSelection(foreground); });
            Task winner = await Task.WhenAny(automation, Task.Delay(550, token));
            token.ThrowIfCancellationRequested();
            if (winner == automation)
            {
                string selected = await automation;
                if (!String.IsNullOrWhiteSpace(selected)) { EnsureForeground(foreground); return selected.Trim(); }
            }

            EnsureForeground(foreground);
            ClipboardSnapshot original = await SnapshotClipboardAsync(token);
            uint beforeCopy = original.Sequence;
            uint copiedSequence = 0;
            string copied = null;
            try
            {
                token.ThrowIfCancellationRequested();
                EnsureForeground(foreground);
                if (NativeMethods.GetClipboardSequenceNumber() != beforeCopy)
                    throw new InvalidOperationException("剪贴板内容刚刚发生变化，请重新使用划词快捷键。");
                if (!NativeMethods.SendCopy())
                    throw new InvalidOperationException("无法向当前应用发送复制操作。如果目标应用以管理员身份运行，请使用相同权限启动 Luma。也可以先复制文字后粘贴翻译。");
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(1200);
                while (DateTime.UtcNow < deadline)
                {
                    await Task.Delay(40, token);
                    EnsureForeground(foreground);
                    uint sequence = NativeMethods.GetClipboardSequenceNumber();
                    if (sequence == beforeCopy) continue;
                    try
                    {
                        string value = Clipboard.ContainsText(TextDataFormat.UnicodeText) ? Clipboard.GetText(TextDataFormat.UnicodeText) : null;
                        uint afterRead = NativeMethods.GetClipboardSequenceNumber();
                        if (sequence != afterRead) continue;
                        copiedSequence = sequence;
                        if (!String.IsNullOrWhiteSpace(value)) { copied = value; break; }
                    }
                    catch (COMException) { }
                }
                if (String.IsNullOrWhiteSpace(copied))
                    throw new InvalidOperationException("没有读取到选中文字。请先选中文本再按快捷键；图片中的文字请使用截图翻译。");
                return copied.Trim();
            }
            finally
            {
                // Never overwrite clipboard content changed by the user or another program after our copy.
                if (copiedSequence != 0 && NativeMethods.GetClipboardSequenceNumber() == copiedSequence)
                {
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        if (NativeMethods.GetClipboardSequenceNumber() != copiedSequence) break;
                        try
                        {
                            if (original.Data == null) Clipboard.Clear();
                            else Clipboard.SetDataObject(original.Data, true);
                            break;
                        }
                        catch (COMException) { Thread.Sleep(25); }
                    }
                }
            }
        }

        private static void EnsureForeground(IntPtr foreground)
        {
            if (NativeMethods.GetForegroundWindow() != foreground)
                throw new InvalidOperationException("当前窗口已切换，请重新选中文字并使用快捷键。");
        }

        internal static async Task WaitForReleasedKeysAsync(CancellationToken token)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            do
            {
                token.ThrowIfCancellationRequested();
                bool pressed = false;
                for (int key = 8; key < 255; key++)
                    if ((NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0) { pressed = true; break; }
                if (!pressed) return;
                await Task.Delay(25, token);
            } while (DateTime.UtcNow < deadline);
            throw new InvalidOperationException("请松开快捷键后重试。");
        }

        private static string ReadAutomationSelection(IntPtr foreground)
        {
            try
            {
                AutomationElement element = AutomationElement.FocusedElement;
                if (element == null || element.Current.IsPassword) return null;
                uint process;
                NativeMethods.GetWindowThreadProcessId(foreground, out process);
                if (element.Current.ProcessId != (int)process) return null;
                object pattern;
                if (!element.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) return null;
                TextPatternRange[] ranges = ((TextPattern)pattern).GetSelection();
                if (ranges == null || ranges.Length == 0) return null;
                List<string> parts = new List<string>();
                int remaining = 100000;
                foreach (TextPatternRange range in ranges)
                {
                    string text = range.GetText(remaining);
                    if (!String.IsNullOrEmpty(text)) { parts.Add(text); remaining -= text.Length; }
                    if (remaining <= 0) break;
                }
                return String.Join(Environment.NewLine, parts);
            }
            catch (Exception) { return null; } // Provider support is optional; clipboard is the fallback.
        }

        private static async Task<ClipboardSnapshot> SnapshotClipboardAsync(CancellationToken token)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    uint sequence = NativeMethods.GetClipboardSequenceNumber();
                    IDataObject data = Clipboard.GetDataObject();
                    if (data == null)
                    {
                        if (NativeMethods.GetClipboardSequenceNumber() == sequence)
                            return new ClipboardSnapshot { Data = null, Sequence = sequence };
                        continue;
                    }
                    DataObject snapshot = new DataObject();
                    foreach (string format in data.GetFormats(false))
                    {
                        object value = data.GetData(format, false);
                        if (value == null) continue;
                        MemoryStream stream = value as MemoryStream;
                        BitmapSource bitmap = value as BitmapSource;
                        Drawing.Bitmap drawingBitmap = value as Drawing.Bitmap;
                        if (stream != null) value = new MemoryStream(stream.ToArray());
                        else if (bitmap != null) { BitmapSource clone = bitmap.Clone(); clone.Freeze(); value = clone; }
                        else if (drawingBitmap != null) value = drawingBitmap.Clone();
                        else if (value is byte[]) value = ((byte[])value).Clone();
                        else if (value is string[]) value = ((string[])value).Clone();
                        snapshot.SetData(format, value, false);
                    }
                    if (NativeMethods.GetClipboardSequenceNumber() == sequence)
                        return new ClipboardSnapshot { Data = snapshot, Sequence = sequence };
                }
                catch (COMException) { }
                await Task.Delay(35, token);
            }
            throw new InvalidOperationException("剪贴板正被其他程序占用，请稍后再试。");
        }
    }

    public static class ScreenOcr
    {
        private sealed class CachedRecognizer
        {
            internal readonly OcrEngine Engine;
            internal readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
            internal CachedRecognizer(OcrEngine engine) { Engine = engine; }
        }

        private static readonly object recognizerLock = new object();
        private static readonly Dictionary<string, CachedRecognizer> recognizers = new Dictionary<string, CachedRecognizer>(StringComparer.OrdinalIgnoreCase);

        public static string[] AvailableLanguages()
        {
            try { return OcrEngine.AvailableRecognizerLanguages.Select(delegate(Language value) { return value.LanguageTag; }).ToArray(); }
            catch (Exception) { return new string[0]; }
        }

        public static async Task<string> CaptureAsync(string language, CancellationToken token)
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                throw new InvalidOperationException("截图翻译需要从桌面界面线程启动。");
            await SelectionReader.WaitForReleasedKeysAsync(token);
            // Wait for the actual compositor frame after the caller hides its own windows, instead
            // of adding an unconditional delay. No other application's window state is changed.
            NativeMethods.DwmFlush();
            token.ThrowIfCancellationRequested();
            Drawing.Rectangle bounds = NativeMethods.VirtualScreen();
            if (bounds.Width <= 0 || bounds.Height <= 0) throw new InvalidOperationException("无法读取屏幕尺寸。");
            using (Drawing.Bitmap screenshot = new Drawing.Bitmap(bounds.Width, bounds.Height, Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(screenshot))
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, Drawing.CopyPixelOperation.SourceCopy);
                BitmapSource frozen = FreezeScreenshot(screenshot);
                CaptureOverlay overlay = new CaptureOverlay(frozen, bounds);
                Drawing.Rectangle? crop;
                using (token.Register(delegate { overlay.Dispatcher.BeginInvoke(new Action(overlay.Cancel)); }))
                {
                    overlay.Show();
                    crop = await overlay.Completion;
                }
                token.ThrowIfCancellationRequested();
                if (!crop.HasValue) return null;
                // Copy only the selected pixels on a worker. Avoid cropping to another GDI bitmap,
                // encoding PNG, allocating its byte stream, and decoding it back into the same pixels.
                return await Task.Run(delegate { return RecognizeCropAsync(screenshot, crop.Value, language, token); }, token);
            }
        }

        public static async Task<string> RecognizeFileAsync(string path, string language, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("请选择图片文件。", "path");
            FileInfo info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("找不到需要识别的图片。", path);
            if (info.Length > 40L * 1024 * 1024) throw new InvalidOperationException("图片超过 40 MB，请先裁剪后再识别。");
            byte[] bytes = await Task.Run(delegate { token.ThrowIfCancellationRequested(); return File.ReadAllBytes(path); }, token);
            return await Task.Run(delegate { return RecognizeBytesAsync(bytes, language, token); }, token);
        }

        private static BitmapSource FreezeScreenshot(Drawing.Bitmap bitmap)
        {
            Drawing.Imaging.BitmapData data = bitmap.LockBits(new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                // This bitmap is allocated by us with a positive stride. BitmapSource.Create copies
                // the pixels before UnlockBits; Bgr32 intentionally ignores GDI's unused alpha byte.
                BitmapSource frozen = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgr32,
                    null, data.Scan0, checked(data.Stride * bitmap.Height), data.Stride);
                frozen.Freeze();
                return frozen;
            }
            finally { bitmap.UnlockBits(data); }
        }

        private static async Task<string> RecognizeCropAsync(Drawing.Bitmap source, Drawing.Rectangle crop, string language, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (SoftwareBitmap bitmap = CreateOcrBitmap(source, crop, token))
                return await RecognizeSoftwareBitmapAsync(bitmap, language, token).ConfigureAwait(false);
        }

        private static SoftwareBitmap CreateOcrBitmap(Drawing.Bitmap source, Drawing.Rectangle crop, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (crop.Width <= 0 || crop.Height <= 0 || crop.X < 0 || crop.Y < 0 ||
                crop.Right > source.Width || crop.Bottom > source.Height)
                throw new ArgumentOutOfRangeException("crop", "截图区域超出了图片边界。");
            double ratio = Math.Min(1.0, (double)OcrEngine.MaxImageDimension / Math.Max(crop.Width, crop.Height));
            if (ratio < 1.0)
            {
                int width = Math.Max(1, (int)Math.Round(crop.Width * ratio));
                int height = Math.Max(1, (int)Math.Round(crop.Height * ratio));
                using (Drawing.Bitmap scaled = new Drawing.Bitmap(width, height, Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(scaled))
                    {
                        graphics.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceCopy;
                        graphics.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                        graphics.DrawImage(source, new Drawing.Rectangle(0, 0, width, height), crop, Drawing.GraphicsUnit.Pixel);
                    }
                    return CopyOcrPixels(scaled, new Drawing.Rectangle(0, 0, width, height), token);
                }
            }
            return CopyOcrPixels(source, crop, token);
        }

        private static SoftwareBitmap CopyOcrPixels(Drawing.Bitmap source, Drawing.Rectangle crop, CancellationToken token)
        {
            int rowBytes = checked(crop.Width * 4);
            byte[] pixels = new byte[checked(rowBytes * crop.Height)];
            Drawing.Imaging.BitmapData data = source.LockBits(crop, Drawing.Imaging.ImageLockMode.ReadOnly, Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                for (int row = 0; row < crop.Height; row++)
                {
                    if ((row & 63) == 0) token.ThrowIfCancellationRequested();
                    Marshal.Copy(IntPtr.Add(data.Scan0, checked(row * data.Stride)), pixels, row * rowBytes, rowBytes);
                }
            }
            finally { source.UnlockBits(data); }
            token.ThrowIfCancellationRequested();
            return SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, crop.Width, crop.Height, BitmapAlphaMode.Ignore);
        }

        private static CachedRecognizer GetRecognizer(string language)
        {
            string key = String.IsNullOrWhiteSpace(language) ? "auto" : language.Trim().Replace('_', '-');
            lock (recognizerLock)
            {
                CachedRecognizer cached;
                if (!recognizers.TryGetValue(key, out cached))
                {
                    cached = new CachedRecognizer(CreateEngine(key));
                    recognizers.Add(key, cached);
                }
                return cached;
            }
        }

        private static OcrEngine CreateEngine(string requested)
        {
            OcrEngine engine = null;
            if (String.IsNullOrWhiteSpace(requested) || requested.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                engine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (engine == null && OcrEngine.AvailableRecognizerLanguages.Count > 0)
                    engine = OcrEngine.TryCreateFromLanguage(OcrEngine.AvailableRecognizerLanguages[0]);
            }
            else
            {
                string normalized = requested.Replace('_', '-');
                Language exact = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(delegate(Language item) { return item.LanguageTag.Equals(normalized, StringComparison.OrdinalIgnoreCase); });
                string prefix = normalized.Split('-')[0];
                if (exact == null) exact = OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(delegate(Language item) { return item.LanguageTag.Split('-')[0].Equals(prefix, StringComparison.OrdinalIgnoreCase); });
                if (exact != null) engine = OcrEngine.TryCreateFromLanguage(exact);
            }
            if (engine == null)
                throw new InvalidOperationException("Windows 尚未安装所选语言的文字识别组件。请到“设置 → 时间和语言 → 语言和区域 → 语言选项”安装对应的基本输入 / 光学字符识别组件，或将识别语言设为自动。");
            return engine;
        }

        private static async Task<string> RecognizeBytesAsync(byte[] bytes, string language, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (SoftwareBitmap bitmap = await DecodeBitmapAsync(bytes, token).ConfigureAwait(false))
                return await RecognizeSoftwareBitmapAsync(bitmap, language, token).ConfigureAwait(false);
        }

        private static async Task<SoftwareBitmap> DecodeBitmapAsync(byte[] bytes, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                using (DataWriter writer = new DataWriter(stream))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync().AsTask(token).ConfigureAwait(false);
                    writer.DetachStream();
                }
                stream.Seek(0);
                WinRtBitmapDecoder decoder = await WinRtBitmapDecoder.CreateAsync(stream).AsTask(token).ConfigureAwait(false);
                double ratio = Math.Min(1.0, (double)OcrEngine.MaxImageDimension / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
                BitmapTransform transform = new BitmapTransform();
                transform.ScaledWidth = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * ratio));
                transform.ScaledHeight = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * ratio));
                return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    transform, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token).ConfigureAwait(false);
            }
        }

        private static async Task<string> RecognizeSoftwareBitmapAsync(SoftwareBitmap bitmap, string language, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // OcrEngine is agile and may cross apartments, but serialize recognition on each cached
            // instance so an overlapping file/capture request cannot race an in-flight operation.
            CachedRecognizer recognizer = GetRecognizer(language);
            await recognizer.Gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                OcrResult result = await recognizer.Engine.RecognizeAsync(bitmap).AsTask(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                string text = String.Join(Environment.NewLine, result.Lines.Select(delegate(OcrLine line) { return line.Text; })).Trim();
                if (String.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("没有识别到文字。请框选清晰的文字区域，或在设置中更换识别语言。");
                return text;
            }
            finally { recognizer.Gate.Release(); }
        }
    }

    internal sealed class CaptureOverlay : Window
    {
        private readonly Drawing.Rectangle desktop;
        private readonly CaptureCanvas surface;
        private readonly System.Windows.Threading.DispatcherTimer escapeTimer;
        private readonly TaskCompletionSource<Drawing.Rectangle?> completion = new TaskCompletionSource<Drawing.Rectangle?>();
        private bool dragging;
        private Drawing.Point origin;
        private Drawing.Rectangle selection;
        public Task<Drawing.Rectangle?> Completion { get { return completion.Task; } }

        public CaptureOverlay(BitmapSource screenshot, Drawing.Rectangle bounds)
        {
            desktop = bounds;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Background = Brushes.Black;
            Cursor = Cursors.Cross;
            Title = "Luma · 截图翻译";
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            WindowStartupLocation = WindowStartupLocation.Manual;
            surface = new CaptureCanvas(screenshot, bounds);
            Content = surface;
            // Staying out of the foreground is essential: exclusive-fullscreen applications may
            // minimize themselves if a capture overlay takes keyboard activation away from them.
            escapeTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input);
            escapeTimer.Interval = TimeSpan.FromMilliseconds(25);
            escapeTimer.Tick += delegate { if ((NativeMethods.GetAsyncKeyState(0x1B) & 0x8000) != 0) Cancel(); };
            SourceInitialized += delegate
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                long extended = NativeMethods.GetWindowLongPtr(handle, -20).ToInt64();
                NativeMethods.SetWindowLongPtr(handle, -20, new IntPtr(extended | 0x08000000L | 0x00000080L)); // NOACTIVATE | TOOLWINDOW
                HwndSource.FromHwnd(handle).AddHook(PreventMouseActivation);
                NativeMethods.SetWindowPos(handle, new IntPtr(-1), desktop.Left, desktop.Top, desktop.Width, desktop.Height, 0x0010);
            };
            Loaded += delegate
            {
                IntPtr handle = new WindowInteropHelper(this).Handle;
                NativeMethods.SetWindowPos(handle, new IntPtr(-1), desktop.Left, desktop.Top, desktop.Width, desktop.Height, 0x0010);
                escapeTimer.Start();
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key == Key.Escape) { args.Handled = true; Cancel(); }
            };
            MouseRightButtonDown += delegate { Cancel(); };
            MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs args)
            {
                origin = CursorPoint();
                dragging = true;
                selection = new Drawing.Rectangle(origin.X, origin.Y, 0, 0);
                CaptureMouse();
                UpdateSelection();
                args.Handled = true;
            };
            MouseMove += delegate { if (dragging) UpdateSelection(); };
            MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs args)
            {
                if (!dragging) return;
                UpdateSelection();
                dragging = false;
                ReleaseMouseCapture();
                if (selection.Width >= 8 && selection.Height >= 8)
                {
                    Drawing.Rectangle result = selection;
                    Close();
                    completion.TrySetResult(result);
                }
                args.Handled = true;
            };
            Closed += delegate
            {
                escapeTimer.Stop();
                // Queue completion so the successful mouse-up path can publish the crop first.
                Dispatcher.BeginInvoke(new Action(delegate { completion.TrySetResult(null); }));
            };
        }

        private static IntPtr PreventMouseActivation(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0021) // WM_MOUSEACTIVATE
            {
                handled = true;
                return new IntPtr(3); // MA_NOACTIVATE: keep receiving the mouse click, retain source-app focus.
            }
            return IntPtr.Zero;
        }

        public void Cancel()
        {
            if (completion.Task.IsCompleted) return;
            Close();
            completion.TrySetResult(null);
        }

        private Drawing.Point CursorPoint()
        {
            NativeMethods.Point cursor;
            NativeMethods.GetCursorPos(out cursor);
            return new Drawing.Point(Math.Max(0, Math.Min(desktop.Width, cursor.X - desktop.Left)),
                Math.Max(0, Math.Min(desktop.Height, cursor.Y - desktop.Top)));
        }

        private void UpdateSelection()
        {
            Drawing.Point current = CursorPoint();
            selection = Drawing.Rectangle.FromLTRB(Math.Min(origin.X, current.X), Math.Min(origin.Y, current.Y),
                Math.Max(origin.X, current.X), Math.Max(origin.Y, current.Y));
            surface.Selection = selection;
            surface.InvalidateVisual();
        }
    }

    internal sealed class CaptureCanvas : FrameworkElement
    {
        private readonly BitmapSource screenshot;
        private readonly Drawing.Rectangle desktop;
        private readonly Typeface regular = new Typeface("Microsoft YaHei UI");
        public Drawing.Rectangle Selection;

        public CaptureCanvas(BitmapSource image, Drawing.Rectangle bounds) { screenshot = image; desktop = bounds; }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            Rect all = new Rect(0, 0, ActualWidth, ActualHeight);
            context.DrawImage(screenshot, all);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(135, 8, 13, 22)), null, all);
            double scaleX = ActualWidth / desktop.Width;
            double scaleY = ActualHeight / desktop.Height;
            if (Selection.Width > 0 && Selection.Height > 0)
            {
                Rect box = new Rect(Selection.X * scaleX, Selection.Y * scaleY, Selection.Width * scaleX, Selection.Height * scaleY);
                context.PushClip(new RectangleGeometry(box));
                context.DrawImage(screenshot, all);
                context.Pop();
                SolidColorBrush accent = new SolidColorBrush(Color.FromRgb(85, 157, 255));
                context.DrawRectangle(null, new Pen(accent, 1.5), box);
                double length = Math.Min(16, Math.Min(box.Width / 3, box.Height / 3));
                Pen corner = new Pen(Brushes.White, 2.4);
                context.DrawLine(corner, box.TopLeft, new Point(box.Left + length, box.Top));
                context.DrawLine(corner, box.TopLeft, new Point(box.Left, box.Top + length));
                context.DrawLine(corner, box.TopRight, new Point(box.Right - length, box.Top));
                context.DrawLine(corner, box.TopRight, new Point(box.Right, box.Top + length));
                context.DrawLine(corner, box.BottomLeft, new Point(box.Left + length, box.Bottom));
                context.DrawLine(corner, box.BottomLeft, new Point(box.Left, box.Bottom - length));
                context.DrawLine(corner, box.BottomRight, new Point(box.Right - length, box.Bottom));
                context.DrawLine(corner, box.BottomRight, new Point(box.Right, box.Bottom - length));
                string size = Selection.Width.ToString(CultureInfo.InvariantCulture) + " × " + Selection.Height.ToString(CultureInfo.InvariantCulture);
                FormattedText label = Text(size, 12, Brushes.White);
                double labelY = box.Bottom + 10 + 30 < ActualHeight ? box.Bottom + 10 : Math.Max(4, box.Top - 38);
                Rect sizePill = new Rect(Math.Min(box.Left, Math.Max(0, ActualWidth - label.Width - 24)), labelY, label.Width + 24, 30);
                context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(240, 22, 28, 40)), null, sizePill, 9, 9);
                context.DrawText(label, new Point(sizePill.Left + 12, sizePill.Top + 6));
            }

            // Place help on the monitor under the cursor, so a virtual desktop gap cannot hide it.
            NativeMethods.Point cursor;
            NativeMethods.GetCursorPos(out cursor);
            Drawing.Rectangle monitor = Forms.Screen.FromPoint(new Drawing.Point(cursor.X, cursor.Y)).Bounds;
            double center = (monitor.Left - desktop.Left + monitor.Width / 2.0) * scaleX;
            double top = (monitor.Top - desktop.Top) * scaleY + 28;
            FormattedText help = Text("拖动框选文字  ·  松开即可翻译     Esc 取消", 14, Brushes.White);
            Rect pill = new Rect(Math.Max(12, Math.Min(ActualWidth - help.Width - 52, center - (help.Width + 40) / 2)), top, help.Width + 40, 46);
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(245, 23, 29, 40)), new Pen(new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)), 1), pill, 15, 15);
            context.DrawText(help, new Point(pill.Left + 20, pill.Top + 12));
        }

        private FormattedText Text(string value, double size, Brush brush)
        { return new FormattedText(value, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, regular, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip); }
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool UnregisterHotKey(IntPtr handle, int id);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr handle, out uint process);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr handle, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] internal static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] internal static extern IntPtr SetWindowLongPtr(IntPtr handle, int index, IntPtr value);
        [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X; public int Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputData Data; }
        [StructLayout(LayoutKind.Explicit)] private struct InputData
        {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeyboardInput Keyboard;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X; public int Y; public uint MouseData; public uint Flags; public uint Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Key; public ushort Scan; public uint Flags; public uint Time; public UIntPtr Extra; }

        internal static Drawing.Rectangle VirtualScreen()
        { return new Drawing.Rectangle(GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79)); }

        private static Input KeyInput(ushort key, bool up)
        { return new Input { Type = 1, Data = new InputData { Keyboard = new KeyboardInput { Key = key, Flags = up ? 2u : 0u } } }; }

        internal static bool SendCopy()
        {
            Input[] events = new Input[] { KeyInput(0x11, false), KeyInput(0x43, false), KeyInput(0x43, true), KeyInput(0x11, true) };
            uint sent = SendInput((uint)events.Length, events, Marshal.SizeOf(typeof(Input)));
            if (sent == events.Length) return true;
            Input[] release = new Input[] { KeyInput(0x43, true), KeyInput(0x11, true) };
            SendInput((uint)release.Length, release, Marshal.SizeOf(typeof(Input)));
            return false;
        }
    }
}
