using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace LumaTranslate
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--render-preview") return Preview(args[1]);
            if (args.Length >= 2 && args[0] == "--self-test") return SelfTest(args[1]);
            string dataDirectory = args.Length >= 2 && args[0] == "--data-dir" ? args[1] : null;
            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\LumaTranslate.Desktop.v1", out first))
            {
                if (!first) { try { using (EventWaitHandle signal = EventWaitHandle.OpenExisting("Local\\LumaTranslate.Show.v1")) signal.Set(); } catch { } return 0; }
                Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Design.Initialize();
                AppController controller = null;
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { e.Handled = true; if (controller != null) controller.ReportError("操作未完成：" + e.Exception.Message); else MessageBox.Show(e.Exception.Message, "映译"); };
                try
                {
                    controller = new AppController(dataDirectory);
                    using (EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\LumaTranslate.Show.v1"))
                    {
                        var wait = ThreadPool.RegisterWaitForSingleObject(show, delegate { if (!app.Dispatcher.HasShutdownStarted) app.Dispatcher.BeginInvoke(new Action(controller.ShowMain)); }, null, Timeout.Infinite, false);
                        app.Exit += delegate { wait.Unregister(null); controller.Dispose(); };
                        if (!controller.Settings.StartMinimized) controller.ShowMain();
                        app.Run();
                    }
                    return 0;
                }
                catch (Exception ex) { if (controller != null) controller.Dispose(); MessageBox.Show("映译未能启动。\n" + ex.Message, "映译", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
            }
        }
        static int Preview(string directory)
        {
            Directory.CreateDirectory(directory); Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Design.Initialize();
            MainWindow main = new MainWindow(new AppSettings()); main.PreviewContent(); Render(main, Path.Combine(directory, "main-window.png"), 1080, 730);
            main.Navigate("settings"); Render(main, Path.Combine(directory, "settings-window.png"), 1080, 730);
            ResultWindow popup = new ResultWindow("截图翻译", "Good design is as little design as possible.\nLess, but better.", "en", "zh-CN"); popup.SetResult(new TranslationResult { Text = "好的设计，是尽可能少的设计。\n更少，却更好。", SourceLanguage = "en", TargetLanguage = "zh-CN", Provider = "界面预览 · 示例内容" }); Render(popup, Path.Combine(directory, "translation-popup.png"), 660, 540);
            main.AllowClose = true; main.Close(); popup.Close(); app.Shutdown(); return 0;
        }
        public static void Render(Window window, string path, int width, int height)
        {
            FrameworkElement element = (FrameworkElement)window.Content;
            element.Width = width; element.Height = height;
            element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
            RenderTargetBitmap bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bmp.Render(element); PngBitmapEncoder png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using (FileStream stream = File.Create(path)) png.Save(stream);
        }
        static int SelfTest(string reportPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)));
            Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Design.Initialize(); int exitCode = 0; var report = new Dictionary<string, object>();
            app.Startup += async delegate
            {
                try
                {
                    MainWindow main = new MainWindow(new AppSettings()); main.PreviewContent(); main.Navigate("history"); main.Navigate("settings"); main.Navigate("translate"); report["ui"] = "PASS: main, history, settings created";
                    using (HotkeyService hotkeys = new HotkeyService()) { string error; bool ok = hotkeys.Apply("Ctrl+Alt+Shift+F9", "Ctrl+Alt+Shift+F10", delegate { }, delegate { }, out error); report["hotkeys"] = ok ? "PASS: registered and released" : error; if (!ok) exitCode = 1; }
                    string[] languages = ScreenOcr.AvailableLanguages(); report["ocrLanguages"] = languages;
                    string fixture = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)), "ocr-fixture.png");
                    using (Drawing.Bitmap bitmap = new Drawing.Bitmap(1050, 230)) using (Drawing.Graphics g = Drawing.Graphics.FromImage(bitmap)) using (Drawing.Font font = new Drawing.Font("Arial", 40)) { g.Clear(Drawing.Color.White); g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAliasGridFit; g.DrawString("HELLO WORLD\nWINDOWS TRANSLATION", font, Drawing.Brushes.Black, 24, 20); bitmap.Save(fixture, Drawing.Imaging.ImageFormat.Png); }
                    string recognized = await ScreenOcr.RecognizeFileAsync(fixture, "en", CancellationToken.None); report["ocrText"] = recognized; bool ocrPass = recognized != null && recognized.IndexOf("HELLO WORLD", StringComparison.OrdinalIgnoreCase) >= 0 && recognized.IndexOf("WINDOWS TRANSLATION", StringComparison.OrdinalIgnoreCase) >= 0; report["ocr"] = ocrPass ? "PASS" : "FAIL"; if (!ocrPass) exitCode = 1;
                    var popup = new ResultWindow("截图翻译", recognized, "en", "zh-CN"); popup.SetError("测试错误显示"); popup.SetLoading(); popup.SetResult(new TranslationResult { Text = "你好，世界", SourceLanguage = "en", TargetLanguage = "zh-CN" });
                    int popupEdits = 0; popup.InputChanged = delegate { popupEdits++; }; popup.SourceText = "Corrected OCR text";
                    if (!popup.SourceIsEditable || popup.TranslatedText.Length != 0 || popupEdits != 1) throw new Exception("Editable popup invalidation failed.");
                    report["popup"] = "PASS: editable source, error, loading, result and edit invalidation states";
                    main.AllowClose = true; main.Close(); popup.Close();
                    bool quitCalled = false;
                    var closingProbe = new MainWindow(new AppSettings { CloseToTray = false });
                    closingProbe.QuitRequested = delegate { quitCalled = true; closingProbe.AllowClose = true; closingProbe.Close(); };
                    closingProbe.Close(); await Dispatcher.Yield(DispatcherPriority.Background);
                    report["windowClose"] = quitCalled ? "PASS: non-tray close is deferred and finishes without reentrancy" : "FAIL"; if (!quitCalled) exitCode = 1;
                    string appData = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)), "controller-data");
                    new SettingsStore(appData).Save(new AppSettings { CaptureHotkey = "Ctrl+Alt+Shift+F9", SelectionHotkey = "Ctrl+Alt+Shift+F10" });
                    using (var controller = new AppController(appData))
                    {
                        MainWindow controlled = (MainWindow)typeof(AppController).GetField("main", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller);
                        controlled.SourceBox.Text = "测试中文"; controlled.TranslateRequested();
                        if (controlled.ResultBox.Text != "测试中文" || controlled.History.Count != 1) throw new Exception("Same-language controller pipeline failed.");
                        controlled.TargetLanguageBox.SelectedItem = LanguageItem.All.First(x => x.Code == "en");
                        if (controlled.ResultBox.Text.Length != 0) throw new Exception("Changing target language left stale translation visible.");
                        controlled.SetResult(new TranslationResult { Text = "Test", SourceLanguage = "zh-CN", TargetLanguage = "en", Provider = "test" });
                        controlled.SourceBox.Text = "修改原文";
                        if (controlled.ResultBox.Text.Length != 0) throw new Exception("Editing source left stale translation visible.");
                        report["controllerPipeline"] = "PASS: same-language result and history, stale result invalidation after language/text changes";
                        controller.Dispose(); controller.Dispose();
                    }
                    report["trayLifecycle"] = "PASS: controller, tray icon and shortcuts created and disposed twice safely";
                }
                catch (Exception ex) { report["error"] = ex.ToString(); exitCode = 1; }
                finally { report["exitCode"] = exitCode; File.WriteAllText(reportPath, new JavaScriptSerializer().Serialize(report), System.Text.Encoding.UTF8); app.Shutdown(exitCode); }
            };
            return app.Run();
        }
    }

    public sealed class AppController : IDisposable
    {
        public AppSettings Settings { get; private set; }
        readonly SettingsStore store; readonly TranslationService translator; readonly HotkeyService hotkeys; readonly MainWindow main;
        Forms.NotifyIcon tray; Drawing.Icon trayIcon; CancellationTokenSource mainCancellation, externalCancellation; ResultWindow popup; List<HistoryEntry> history; bool externalBusy, disposed;
        public AppController(string dataDirectory)
        {
            store = new SettingsStore(dataDirectory); Settings = store.Load(); string loadWarning = store.LastWarning; history = store.LoadHistory(); loadWarning = String.Join(" ", new[] { loadWarning, store.LastWarning }.Where(x => !String.IsNullOrEmpty(x))); translator = new TranslationService(); hotkeys = new HotkeyService();
            main = new MainWindow(Settings); main.History = history;
            main.TranslateRequested = TranslateMain; main.CaptureRequested = delegate { Capture(); }; main.SelectionRequested = SelectAfterDelay; main.QuitRequested = Quit;
            main.ClearHistoryRequested = delegate { store.SaveHistory(new List<HistoryEntry>()); history.Clear(); };
            main.SaveRequested = Save; main.HistorySelected = delegate(HistoryEntry item) { CancelMain(); main.SourceBox.Text = item.SourceText; main.SourceLanguageBox.SelectedItem = LanguageItem.All.FirstOrDefault(x => x.Code == item.SourceLanguage) ?? LanguageItem.All[0]; main.TargetLanguageBox.SelectedItem = LanguageItem.All.FirstOrDefault(x => x.Code == item.TargetLanguage) ?? LanguageItem.All[1]; main.SetResult(new TranslationResult { Text = item.TranslatedText, SourceLanguage = item.SourceLanguage, TargetLanguage = item.TargetLanguage, Provider = "本地记录" }); };
            main.SourceBox.TextChanged += delegate { InvalidateMainResult(); }; main.SourceLanguageBox.SelectionChanged += delegate { InvalidateMainResult(); }; main.TargetLanguageBox.SelectionChanged += delegate { InvalidateMainResult(); };
            string error; bool registered = hotkeys.Apply(Settings.CaptureHotkey, Settings.SelectionHotkey, Capture, Select, out error);
            CreateTray(); if (!String.IsNullOrEmpty(loadWarning)) ReportError(loadWarning); if (!registered) ReportError("快捷键未启用：" + error + "。请在偏好设置中更换。");
        }
        void CreateTray()
        {
            tray = new Forms.NotifyIcon(); string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "luma.ico");
            trayIcon = File.Exists(iconPath) ? new Drawing.Icon(iconPath) : (Drawing.Icon)Drawing.SystemIcons.Application.Clone(); tray.Icon = trayIcon; tray.Text = "映译 Luma · 随时，待命";
            var menu = new Forms.ContextMenuStrip(); menu.Items.Add("打开映译", null, delegate { ShowMain(); }); menu.Items.Add("截图翻译", null, delegate { Capture(); }); var selectionHint = menu.Items.Add("划词翻译  " + Settings.SelectionHotkey); selectionHint.Enabled = false; menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("偏好设置", null, delegate { ShowMain(); main.Navigate("settings"); }); menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add("退出映译", null, delegate { Quit(); }); tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { ShowMain(); }; tray.Visible = true;
        }
        public void ShowMain() { if (disposed) return; main.Show(); if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal; main.Activate(); }
        public void ReportError(string message) { if (disposed) return; main.SetStatus(message); if (!main.IsVisible && tray != null) { tray.BalloonTipTitle = "映译"; tray.BalloonTipText = message.Length > 220 ? message.Substring(0, 220) : message; tray.ShowBalloonTip(4500); } }
        void Save(AppSettings settings)
        {
            string error; if (!hotkeys.Apply(settings.CaptureHotkey, settings.SelectionHotkey, Capture, Select, out error)) throw new InvalidOperationException(error);
            try { store.Save(settings); } catch { string ignored; hotkeys.Apply(Settings.CaptureHotkey, Settings.SelectionHotkey, Capture, Select, out ignored); throw; }
            Settings = settings; main.Settings = settings; if (tray != null && tray.ContextMenuStrip != null) tray.ContextMenuStrip.Items[2].Text = "划词翻译  " + Settings.SelectionHotkey;
        }
        void CancelMain()
        {
            if (mainCancellation != null) { mainCancellation.Cancel(); mainCancellation = null; main.SetBusy(false); main.SetStatus("已取消翻译"); }
        }
        void InvalidateMainResult()
        {
            if (mainCancellation != null) CancelMain();
            if (main.ResultBox.Text.Length != 0) { main.ResultBox.Clear(); main.SetStatus("原文或语言已变更，请重新翻译"); }
        }
        async void TranslateMain()
        {
            if (mainCancellation != null) { CancelMain(); return; }
            string source = main.SourceBox.Text; if (String.IsNullOrWhiteSpace(source)) { main.SetStatus("先输入或粘贴一段文字"); main.SourceBox.Focus(); return; }
            CancellationTokenSource request = new CancellationTokenSource(); mainCancellation = request; main.SetBusy(true); main.ResultBox.Clear();
            string from = main.SourceLanguage, to = main.TargetLanguage;
            try
            {
                TranslationResult result = await translator.TranslateAsync(source, from, to, request.Token);
                if (mainCancellation != request || request.IsCancellationRequested || disposed) return;
                main.SetResult(result); AddHistory(source, result);
                Settings.SourceLanguage = from; Settings.TargetLanguage = to; try { store.Save(Settings); } catch { main.SetStatus("翻译完成，但偏好未能保存"); }
            }
            catch (OperationCanceledException) { if (mainCancellation == request) main.SetStatus("已取消翻译"); }
            catch (Exception ex) { if (mainCancellation == request) ReportError(ex.Message); }
            finally { if (mainCancellation == request) { mainCancellation = null; main.SetBusy(false); } request.Dispose(); }
        }
        void AddHistory(string source, TranslationResult result)
        {
            if (history.Count > 0 && history[0].SourceText == source && history[0].TranslatedText == result.Text) return;
            history.Insert(0, new HistoryEntry { SourceText = source, TranslatedText = result.Text, SourceLanguage = result.SourceLanguage, TargetLanguage = result.TargetLanguage, CreatedAt = DateTime.UtcNow }); if (history.Count > 50) history.RemoveRange(50, history.Count - 50);
            try { store.SaveHistory(history); } catch { ReportError("翻译完成，但本地记录未能保存。"); }
        }
        async void SelectAfterDelay()
        {
            if (externalBusy) return; externalBusy = true; main.Hide(); if (tray != null) { tray.BalloonTipTitle = "划词翻译"; tray.BalloonTipText = "请在 3 秒内选中文字，映译随后读取。平时可直接使用 " + Settings.SelectionHotkey; tray.ShowBalloonTip(3000); } await Task.Delay(3000); externalBusy = false; if (!disposed) Select();
        }
        async void Select()
        {
            if (externalBusy || disposed) return; externalBusy = true; CancellationTokenSource request = new CancellationTokenSource();
            try { string text = await SelectionReader.ReadAsync(request.Token); if (String.IsNullOrWhiteSpace(text)) { ReportError("没有读取到选中文字。请先选中文字，或使用截图翻译。"); return; } OpenResult("划词翻译", text); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ReportError(ex.Message); }
            finally { externalBusy = false; request.Dispose(); }
        }
        async void Capture()
        {
            if (externalBusy || disposed) return; externalBusy = true;
            bool wasVisible = main.IsVisible, mainWasActive = main.IsActive;
            ResultWindow previousPopup = popup; bool popupVisible = previousPopup != null && previousPopup.IsVisible, popupWasActive = previousPopup != null && previousPopup.IsActive;
            main.Hide(); if (popupVisible) previousPopup.Hide(); CancellationTokenSource request = new CancellationTokenSource();
            try
            {
                // Only Luma's own windows are hidden. No external HWND is minimized, hidden, resized or moved.
                await Dispatcher.Yield(DispatcherPriority.Background);
                string text = await ScreenOcr.CaptureAsync(main.SourceLanguage, request.Token);
                if (String.IsNullOrWhiteSpace(text)) { RestoreAfterCapture(wasVisible, mainWasActive, previousPopup, popupVisible, popupWasActive); return; }
                OpenResult("截图翻译", text);
            }
            catch (OperationCanceledException) { RestoreAfterCapture(wasVisible, mainWasActive, previousPopup, popupVisible, popupWasActive); }
            catch (Exception ex) { RestoreAfterCapture(wasVisible, mainWasActive, previousPopup, popupVisible, popupWasActive); ReportError(ex.Message); }
            finally { externalBusy = false; request.Dispose(); }
        }
        void RestoreAfterCapture(bool mainVisible, bool mainActive, ResultWindow previousPopup, bool popupVisible, bool popupActive)
        {
            if (disposed) return;
            if (mainVisible)
            {
                main.ShowActivated = mainActive; main.Show(); main.ShowActivated = true;
            }
            if (popupVisible && previousPopup != null && popup == previousPopup)
            {
                previousPopup.ShowActivated = popupActive; previousPopup.Show(); previousPopup.ShowActivated = true;
            }
            if (mainActive) main.Activate(); else if (popupActive && previousPopup != null && popup == previousPopup) previousPopup.Activate();
        }
        void OpenResult(string kind, string text)
        {
            if (disposed) return; if (popup != null) popup.Close();
            ResultWindow next = new ResultWindow(kind, text, main.SourceLanguage, main.TargetLanguage); popup = next;
            next.Closed += delegate
            {
                if (popup == next)
                {
                    if (externalCancellation != null) externalCancellation.Cancel();
                    externalCancellation = null; popup = null;
                }
            };
            next.InputChanged = delegate
            {
                if (popup != next || externalCancellation == null) return;
                externalCancellation.Cancel(); externalCancellation = null;
            };
            next.TranslateRequested = delegate
            {
                if (popup != next) return;
                if (next.IsBusy)
                {
                    if (externalCancellation != null) externalCancellation.Cancel();
                    externalCancellation = null; next.SetIdle("已取消翻译，可修改后重新翻译");
                    return;
                }
                TranslatePopup(next);
            };
            next.Show(); TranslatePopup(next);
        }
        async void TranslatePopup(ResultWindow window)
        {
            string text = window.SourceText, from = window.SourceLanguage, to = window.TargetLanguage;
            if (String.IsNullOrWhiteSpace(text)) { window.SetIdle("请先输入或粘贴要翻译的文字"); return; }
            if (externalCancellation != null) externalCancellation.Cancel();
            CancellationTokenSource request = new CancellationTokenSource(); externalCancellation = request; window.SetLoading();
            try
            {
                TranslationResult result = await translator.TranslateAsync(text, from, to, request.Token);
                if (externalCancellation != request || request.IsCancellationRequested || popup != window || disposed) return;
                window.SetResult(result); AddHistory(text, result);
                Settings.SourceLanguage = from; Settings.TargetLanguage = to;
                LanguageItem sourceChoice = LanguageItem.All.FirstOrDefault(x => x.Code == from); if (sourceChoice != null) main.SourceLanguageBox.SelectedItem = sourceChoice;
                LanguageItem targetChoice = LanguageItem.All.FirstOrDefault(x => x.Code == to); if (targetChoice != null) main.TargetLanguageBox.SelectedItem = targetChoice;
                try { store.Save(Settings); } catch { window.SetStatus("翻译完成，但偏好未能保存"); }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (popup == window && externalCancellation == request && !request.IsCancellationRequested) window.SetError(ex.Message); }
            finally { if (externalCancellation == request) externalCancellation = null; request.Dispose(); }
        }
        public void Quit() { if (disposed) return; Dispose(); Application.Current.Shutdown(); }
        public void Dispose()
        {
            if (disposed) return; disposed = true; CancelMain(); if (externalCancellation != null) externalCancellation.Cancel(); if (popup != null) popup.Close(); hotkeys.Dispose(); translator.Dispose(); if (tray != null) { tray.Visible = false; if (tray.ContextMenuStrip != null) tray.ContextMenuStrip.Dispose(); tray.Dispose(); } if (trayIcon != null) trayIcon.Dispose(); main.AllowClose = true; main.Close();
        }
    }
}
