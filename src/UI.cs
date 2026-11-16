using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Markup;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace LumaTranslate
{
    public static class Design
    {
        public static readonly Brush Ink = Brush("#242629"), Muted = Brush("#8B8E94"), Blue = Brush("#316AF4"), Line = Brush("#E9E9E7"), Paper = Brush("#FAFAF8");
        public static SolidColorBrush Brush(string color) { return (SolidColorBrush)new BrushConverter().ConvertFromString(color); }
        public static TextBlock Text(string text, double size = 14, Brush color = null, bool bold = false)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = color ?? Ink, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        }
        public static Border Card(UIElement child, string background = "#FFFFFF", double radius = 16, Thickness? padding = null)
        {
            return new Border { Background = Brush(background), CornerRadius = new CornerRadius(radius), BorderBrush = Line, BorderThickness = new Thickness(1), Padding = padding ?? new Thickness(20), Child = child };
        }
        public static Button Button(string text, Action click, bool primary = false)
        {
            Button b = new Button { Content = text, Padding = new Thickness(16, 10, 16, 10), Background = primary ? Ink : Brushes.Transparent, Foreground = primary ? Brushes.White : Ink, Cursor = Cursors.Hand, MinHeight = 38 };
            if (click != null) b.Click += delegate { click(); };
            return b;
        }
        public static void Initialize()
        {
            const string xaml = @"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
<Style TargetType='Window'><Setter Property='FontFamily' Value='Segoe UI, Microsoft YaHei UI'/><Setter Property='Foreground' Value='#242629'/><Setter Property='Background' Value='#FAFAF8'/><Setter Property='TextOptions.TextFormattingMode' Value='Display'/><Setter Property='TextOptions.TextRenderingMode' Value='ClearType'/></Style>
<Style TargetType='Button'><Setter Property='FontSize' Value='13'/><Setter Property='BorderThickness' Value='0'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='Chrome' CornerRadius='9' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}' BorderThickness='1' BorderBrush='Transparent'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Chrome' Property='Opacity' Value='0.72'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='#316AF4'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='Chrome' Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType='TextBox'><Setter Property='FontSize' Value='15'/><Setter Property='Foreground' Value='#242629'/><Setter Property='Background' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='0'/><Setter Property='SelectionBrush' Value='#B5CBFF'/><Setter Property='VerticalContentAlignment' Value='Top'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/></Style>
<Style TargetType='ComboBox'><Setter Property='FontSize' Value='13'/><Setter Property='MinWidth' Value='100'/><Setter Property='Height' Value='36'/><Setter Property='Foreground' Value='#242629'/><Setter Property='Background' Value='Transparent'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'><Grid><ToggleButton Focusable='False' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='#F6F6F4' CornerRadius='7' Padding='12,7'><DockPanel><TextBlock DockPanel.Dock='Right' Text='⌄' Margin='16,0,0,0' Foreground='#8B8E94'/><ContentPresenter Content='{Binding SelectionBoxItem, RelativeSource={RelativeSource AncestorType=ComboBox}}' ContentTemplate='{Binding SelectionBoxItemTemplate, RelativeSource={RelativeSource AncestorType=ComboBox}}'/></DockPanel></Border></ControlTemplate></ToggleButton.Template></ToggleButton><Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'><Border Background='White' BorderBrush='#E5E5E5' BorderThickness='1' CornerRadius='8' Padding='5' MinWidth='140' Margin='0,4,0,4'><ScrollViewer MaxHeight='320'><ItemsPresenter/></ScrollViewer></Border></Popup></Grid></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType='ComboBoxItem'><Setter Property='Padding' Value='12,8'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'><Border x:Name='Item' Background='Transparent' CornerRadius='5' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='Item' Property='Background' Value='#EEF3FF'/></Trigger><Trigger Property='IsSelected' Value='True'><Setter Property='Foreground' Value='#316AF4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType='CheckBox'><Setter Property='FontSize' Value='13'/><Setter Property='Margin' Value='0,8,0,8'/></Style>
<Style TargetType='ScrollBar'><Setter Property='Width' Value='7'/></Style>
</ResourceDictionary>";
            Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
        }
        public static void RoundNative(Window window)
        {
            window.SourceInitialized += delegate { try { int corner = 2; DwmSetWindowAttribute(new WindowInteropHelper(window).Handle, 33, ref corner, sizeof(int)); } catch { } };
        }
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        public static Grid Rows(params GridLength[] heights) { Grid g = new Grid(); foreach (var h in heights) g.RowDefinitions.Add(new RowDefinition { Height = h }); return g; }
        public static void At(Grid g, UIElement child, int row, int col = 0) { Grid.SetRow(child, row); Grid.SetColumn(child, col); g.Children.Add(child); }
        public static readonly GridLength Auto = GridLength.Auto, Star = new GridLength(1, GridUnitType.Star);
        public static void Copy(string text, Action<string> status)
        {
            if (String.IsNullOrEmpty(text)) { status("还没有可以复制的内容"); return; }
            try { Clipboard.SetText(text); status("已复制到剪贴板"); } catch { status("剪贴板正忙，请稍后重试"); }
        }
        public static Border Brand(double size)
        {
            Grid mark = new Grid { Width = size, Height = size };
            mark.Children.Add(Text("A", size * .53, Brushes.White, true));
            ((TextBlock)mark.Children[0]).HorizontalAlignment = HorizontalAlignment.Center;
            return new Border { Background = Ink, CornerRadius = new CornerRadius(size * .27), Child = mark };
        }
    }

    public class LanguageItem
    {
        public string Code; public string Name;
        public LanguageItem(string code, string name) { Code = code; Name = name; }
        public override string ToString() { return Name; }
        public static readonly LanguageItem[] All = { new LanguageItem("auto", "自动识别"), new LanguageItem("zh-CN", "简体中文"), new LanguageItem("en", "英语"), new LanguageItem("ja", "日语"), new LanguageItem("ko", "韩语"), new LanguageItem("fr", "法语"), new LanguageItem("de", "德语"), new LanguageItem("es", "西班牙语"), new LanguageItem("ru", "俄语") };
        public static string Label(string code) { var x = All.FirstOrDefault(l => l.Code == code); return x == null ? code : x.Name; }
    }

    public class MainWindow : Window
    {
        public Action TranslateRequested, CaptureRequested, SelectionRequested, QuitRequested, ClearHistoryRequested;
        public Action<AppSettings> SaveRequested;
        public Action<HistoryEntry> HistorySelected;
        public TextBox SourceBox, ResultBox;
        public ComboBox SourceLanguageBox, TargetLanguageBox;
        public AppSettings Settings;
        public bool AllowClose;
        public bool IsBusy;
        public List<HistoryEntry> History = new List<HistoryEntry>();
        Grid body; Border content; TextBlock status, count, provider; Button translateButton; StackPanel menu;
        string activePage = "translate";
        public MainWindow(AppSettings settings)
        {
            Settings = settings; Title = "映译 Luma · 翻译，自在一点"; Width = 1080; Height = 730; MinWidth = 890; MinHeight = 650; WindowStartupLocation = WindowStartupLocation.CenterScreen; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize;
            var chrome = new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(14) };
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome); Design.RoundNative(this);
            Grid shell = Design.Rows(new GridLength(49), Design.Star); shell.Background = Design.Paper;
            Border titlebar = new Border { Background = Design.Paper, BorderBrush = Design.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
            Grid title = new Grid { Margin = new Thickness(22, 0, 10, 0) };
            title.Children.Add(Design.Text("LUMA    /    映译", 11, Design.Muted, true));
            var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            controls.Children.Add(Design.Button("—", delegate { WindowState = WindowState.Minimized; }));
            controls.Children.Add(Design.Button("□", delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }));
            var close = Design.Button("✕", Close); close.ToolTip = "关闭窗口，继续在托盘运行"; controls.Children.Add(close); title.Children.Add(controls);
            titlebar.Child = title; titlebar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.OriginalSource is TextBlock || e.OriginalSource is Border || e.OriginalSource is Grid) { if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; else DragMove(); } };
            Design.At(shell, titlebar, 0);
            body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(192) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = Design.Star });
            Grid sidebar = Design.Rows(Design.Auto, Design.Auto, Design.Star, Design.Auto); sidebar.Margin = new Thickness(20, 28, 16, 24);
            StackPanel branding = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 34) }; branding.Children.Add(Design.Brand(36));
            StackPanel name = new StackPanel { Margin = new Thickness(10, 0, 0, 0) }; name.Children.Add(Design.Text("映译", 19, null, true)); name.Children.Add(Design.Text("Less noise. More meaning.", 7.5, Design.Muted)); branding.Children.Add(name); Design.At(sidebar, branding, 0);
            menu = new StackPanel(); Design.At(sidebar, menu, 1);
            StackPanel foot = new StackPanel { Margin = new Thickness(9, 0, 0, 0) }; foot.Children.Add(Design.Text("●  随时，待命", 11, Design.Brush("#56816A"))); foot.Children.Add(new TextBlock { Text = "关闭窗口后仍可使用快捷键", FontSize = 10, Foreground = Design.Muted, Margin = new Thickness(0, 9, 0, 0) }); Design.At(sidebar, foot, 3);
            Border sideBackground = new Border { Background = Design.Brush("#F3F3F0"), BorderBrush = Design.Line, BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar }; Design.At(body, sideBackground, 0, 0);
            content = new Border { Padding = new Thickness(34, 30, 34, 20) }; Design.At(body, content, 0, 1); Design.At(shell, body, 1); Content = shell;
            BuildTranslation(); Navigate("translate");
            PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { if (TranslateRequested != null && activePage == "translate") TranslateRequested(); e.Handled = true; } };
            Closing += delegate(object s, System.ComponentModel.CancelEventArgs e) { if (!AllowClose && Settings.CloseToTray) { e.Cancel = true; Hide(); } else if (!AllowClose && QuitRequested != null) { e.Cancel = true; Dispatcher.BeginInvoke(QuitRequested); } };
        }
        Grid translationPage;
        void BuildTranslation()
        {
            translationPage = Design.Rows(Design.Auto, new GridLength(22), Design.Star, Design.Auto, Design.Auto, Design.Auto);
            StackPanel intro = new StackPanel(); intro.Children.Add(Design.Text("让理解，没有边界。", 29, null, true)); intro.Children.Add(new TextBlock { Text = "翻译文字，或捕捉屏幕上的灵感。", FontSize = 13, Foreground = Design.Muted, Margin = new Thickness(0, 10, 0, 0) }); Design.At(translationPage, intro, 0);
            Grid cards = new Grid(); cards.ColumnDefinitions.Add(new ColumnDefinition { Width = Design.Star }); cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); cards.ColumnDefinitions.Add(new ColumnDefinition { Width = Design.Star });
            SourceLanguageBox = MakeLanguage(true, Settings.SourceLanguage); TargetLanguageBox = MakeLanguage(false, Settings.TargetLanguage);
            SourceBox = MakeText(false); SourceBox.MaxLength = 5000; ResultBox = MakeText(true);
            count = Design.Text("0 / 5,000", 10, Design.Muted); provider = Design.Text("译文将在这里呈现", 10, Design.Muted);
            var sourcePanel = TextPanel(SourceLanguageBox, SourceBox, "输入文字，让语言更近一步。", count, false);
            var targetPanel = TextPanel(TargetLanguageBox, ResultBox, "每一种语言，都值得被理解。", provider, true);
            Design.At(cards, sourcePanel, 0, 0); Design.At(cards, targetPanel, 0, 2); Design.At(translationPage, cards, 2);
            SourceBox.TextChanged += delegate { count.Text = SourceBox.Text.Length.ToString("N0") + " / 5,000"; };
            Grid action = new Grid { Margin = new Thickness(0, 16, 0, 0) }; StackPanel left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(Design.Button("⇄  交换语言", Swap)); left.Children.Add(Design.Button("清空", delegate { SourceBox.Clear(); ResultBox.Clear(); provider.Text = "译文将在这里呈现"; SetStatus("准备就绪"); })); action.Children.Add(left);
            translateButton = Design.Button("翻译文字  ↗", delegate { if (TranslateRequested != null) TranslateRequested(); }, true); translateButton.Width = 142; translateButton.HorizontalAlignment = HorizontalAlignment.Right; action.Children.Add(translateButton); Design.At(translationPage, action, 3);
            Grid quick = new Grid { Margin = new Thickness(0, 25, 0, 0) }; quick.ColumnDefinitions.Add(new ColumnDefinition { Width = Design.Star }); quick.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); quick.ColumnDefinitions.Add(new ColumnDefinition { Width = Design.Star });
            Design.At(quick, QuickCard("▣", "截图翻译", "框选屏幕，识别并翻译", delegate { if (CaptureRequested != null) CaptureRequested(); }), 0, 0);
            Design.At(quick, QuickCard("Aa", "划词翻译", "选中文字，按下快捷键", delegate { if (SelectionRequested != null) SelectionRequested(); }), 0, 2); Design.At(translationPage, quick, 4);
            Grid footer = new Grid { Margin = new Thickness(1, 18, 0, 0) }; status = Design.Text("准备就绪", 10, Design.Muted); footer.Children.Add(status);
            var hint = Design.Text("Ctrl + Enter  翻译", 10, Design.Muted); hint.HorizontalAlignment = HorizontalAlignment.Right; footer.Children.Add(hint); Design.At(translationPage, footer, 5);
        }
        Border TextPanel(ComboBox language, TextBox box, string placeholder, TextBlock info, bool output)
        {
            Grid panel = Design.Rows(Design.Auto, new GridLength(17), Design.Star, Design.Auto);
            Grid head = new Grid(); language.HorizontalAlignment = HorizontalAlignment.Left; head.Children.Add(language);
            var kind = Design.Text(output ? "译文" : "原文", 10, Design.Muted); kind.HorizontalAlignment = HorizontalAlignment.Right; head.Children.Add(kind); Design.At(panel, head, 0);
            Grid input = new Grid(); TextBlock prompt = Design.Text(placeholder, 19, Design.Brush("#B4B5B7")); prompt.VerticalAlignment = VerticalAlignment.Top; prompt.Margin = new Thickness(0, 3, 4, 0); prompt.IsHitTestVisible = false; input.Children.Add(prompt); input.Children.Add(box); box.TextChanged += delegate { prompt.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; }; Design.At(panel, input, 2);
            Grid foot = new Grid { Margin = new Thickness(0, 16, 0, 0) }; foot.Children.Add(info); StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (!output) actions.Children.Add(Design.Button("粘贴", delegate { try { if (Clipboard.ContainsText()) { SourceBox.Text = Clipboard.GetText(); SetStatus("已粘贴"); } else SetStatus("剪贴板里没有文字"); } catch { SetStatus("剪贴板正忙，请稍后重试"); } }));
            actions.Children.Add(Design.Button("复制", delegate { Design.Copy(box.Text, SetStatus); })); foot.Children.Add(actions); Design.At(panel, foot, 3);
            return Design.Card(panel, output ? "#FDFDFC" : "#FFFFFF", 16, new Thickness(21, 18, 21, 13));
        }
        Button QuickCard(string icon, string title, string subtitle, Action action)
        {
            Grid row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = Design.Star }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = Design.Auto });
            Design.At(row, Design.Text(icon, 23, Design.Muted), 0, 0); StackPanel texts = new StackPanel(); texts.Children.Add(Design.Text(title, 13, null, true)); texts.Children.Add(new TextBlock { Text = subtitle, FontSize = 10, Foreground = Design.Muted, Margin = new Thickness(0, 5, 0, 0) }); Design.At(row, texts, 0, 1); Design.At(row, Design.Text("↗", 18, Design.Muted), 0, 2);
            Button b = Design.Button("", action); b.Content = Design.Card(row, "#F5F5F2", 12, new Thickness(14, 15, 14, 15)); b.Padding = new Thickness(0); b.HorizontalContentAlignment = HorizontalAlignment.Stretch; return b;
        }
        public static ComboBox MakeLanguage(bool auto, string selected)
        {
            ComboBox combo = new ComboBox(); foreach (var l in LanguageItem.All) if (auto || l.Code != "auto") combo.Items.Add(l); combo.SelectedItem = combo.Items.Cast<LanguageItem>().FirstOrDefault(l => l.Code == selected) ?? combo.Items[0]; return combo;
        }
        public static TextBox MakeText(bool readOnly) { return new TextBox { IsReadOnly = readOnly, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 19, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MinHeight = 80 }; }
        public string SourceLanguage { get { return ((LanguageItem)SourceLanguageBox.SelectedItem).Code; } }
        public string TargetLanguage { get { return ((LanguageItem)TargetLanguageBox.SelectedItem).Code; } }
        void Swap()
        {
            string originalText = SourceBox.Text, resultText = ResultBox.Text;
            string source = SourceLanguage == "auto" ? TranslationService.DetectLanguage(originalText) : SourceLanguage; string target = TargetLanguage;
            SourceLanguageBox.SelectedItem = LanguageItem.All.FirstOrDefault(l => l.Code == target); TargetLanguageBox.SelectedItem = LanguageItem.All.FirstOrDefault(l => l.Code == source && l.Code != "auto") ?? LanguageItem.All[2];
            SourceBox.Text = resultText; ResultBox.Text = originalText; SetStatus("已交换语言与文字");
        }
        public void SetStatus(string text) { status.Text = text; status.ToolTip = text; }
        public void SetBusy(bool busy)
        {
            IsBusy = busy; translateButton.Content = busy ? "取消翻译  ×" : "翻译文字  ↗"; if (busy) SetStatus("正在翻译…");
        }
        public void SetResult(TranslationResult result) { ResultBox.Text = result.Text; provider.Text = LanguageItem.Label(result.SourceLanguage) + " → " + LanguageItem.Label(result.TargetLanguage); SetStatus("翻译完成 · " + result.Provider); }
        public void Navigate(string page)
        {
            activePage = page; menu.Children.Clear(); AddNav("translate", "◎", "文字翻译"); AddNav("history", "◷", "最近翻译"); AddNav("settings", "⚙", "偏好设置");
            if (page == "translate") content.Child = translationPage; else if (page == "history") BuildHistory(); else BuildSettings();
        }
        void AddNav(string page, string icon, string label)
        {
            Button b = Design.Button(icon + "    " + label, delegate { Navigate(page); }); b.Margin = new Thickness(0, 0, 0, 6); b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Background = activePage == page ? Brushes.White : Brushes.Transparent; b.Foreground = activePage == page ? Design.Ink : Design.Muted; menu.Children.Add(b);
        }
        void BuildHistory()
        {
            Grid page = Design.Rows(Design.Auto, Design.Auto, Design.Star); page.Children.Add(Design.Text("每一次理解，都有迹可循。", 25, null, true));
            Grid toolbar = new Grid { Margin = new Thickness(0, 12, 0, 22) }; toolbar.Children.Add(Design.Text("仅保存在这台电脑 · 最近 50 条", 12, Design.Muted)); Button clear = Design.Button("清空记录", delegate { if (ClearHistoryRequested != null) ClearHistoryRequested(); BuildHistory(); }); clear.HorizontalAlignment = HorizontalAlignment.Right; toolbar.Children.Add(clear); Design.At(page, toolbar, 1);
            StackPanel list = new StackPanel();
            if (History.Count == 0) { var empty = Design.Text("还没有翻译记录。\n下一次理解，从一句话开始。", 19, Design.Muted); empty.Margin = new Thickness(25, 70, 25, 0); list.Children.Add(empty); }
            foreach (HistoryEntry entry in History)
            {
                HistoryEntry current = entry; StackPanel text = new StackPanel(); text.Children.Add(Design.Text(LanguageItem.Label(entry.SourceLanguage) + "  →  " + LanguageItem.Label(entry.TargetLanguage) + "    ·    " + entry.CreatedAt.ToLocalTime().ToString("MM/dd  HH:mm"), 10, Design.Muted));
                text.Children.Add(new TextBlock { Text = entry.SourceText.Replace('\n', ' '), FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 10, 0, 6) }); text.Children.Add(new TextBlock { Text = entry.TranslatedText.Replace('\n', ' '), FontSize = 13, Foreground = Design.Muted, TextTrimming = TextTrimming.CharacterEllipsis });
                Button item = Design.Button("", delegate { Navigate("translate"); if (HistorySelected != null) HistorySelected(current); }); item.Content = Design.Card(text, "#FFFFFF", 12, new Thickness(18)); item.Padding = new Thickness(0); item.HorizontalContentAlignment = HorizontalAlignment.Stretch; item.Margin = new Thickness(0, 0, 0, 12); list.Children.Add(item);
            }
            Design.At(page, new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 2); content.Child = page;
        }
        void BuildSettings()
        {
            StackPanel page = new StackPanel(); page.Children.Add(Design.Text("以你习惯的方式。", 28, null, true)); page.Children.Add(new TextBlock { Text = "把翻译，融入每一天的工作。", FontSize = 13, Foreground = Design.Muted, Margin = new Thickness(0, 10, 0, 24) });
            StackPanel shortcuts = new StackPanel(); shortcuts.Children.Add(Design.Text("全局快捷键", 15, null, true)); shortcuts.Children.Add(new TextBlock { Text = "点击输入框，按下新的组合键。", FontSize = 11, Foreground = Design.Muted, Margin = new Thickness(0, 7, 0, 14) });
            TextBox capture = HotkeyBox(Settings.CaptureHotkey), selection = HotkeyBox(Settings.SelectionHotkey); shortcuts.Children.Add(SettingRow("截图翻译", "框选任意屏幕区域", capture)); shortcuts.Children.Add(SettingRow("划词翻译", "读取当前选中的文字", selection)); page.Children.Add(Design.Card(shortcuts));
            StackPanel general = new StackPanel(); general.Children.Add(Design.Text("使用偏好", 15, null, true)); CheckBox tray = new CheckBox { Content = "关闭主窗口后，继续在系统托盘运行", IsChecked = Settings.CloseToTray }; CheckBox minimized = new CheckBox { Content = "启动应用时直接进入托盘", IsChecked = Settings.StartMinimized }; general.Children.Add(tray); general.Children.Add(minimized);
            var langs = MakeLanguage(false, Settings.TargetLanguage); general.Children.Add(SettingRow("默认目标语言", "截图与划词翻译使用此语言", langs)); Border gc = Design.Card(general); gc.Margin = new Thickness(0, 14, 0, 0); page.Children.Add(gc);
            page.Children.Add(new TextBlock { Text = "在线翻译由 MyMemory 提供，无需密钥，有每日免费额度。截图在本机识别，仅文字会发送给翻译服务。自动识别按文字特征判断，法语、德语等请手动选择原文语言。", FontSize = 11, Foreground = Design.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 16, 2, 8), LineHeight = 19 });
            TextBlock saved = Design.Text("", 12, Design.Blue); saved.Margin = new Thickness(0, 8, 0, 0);
            Button save = Design.Button("保存偏好  ✓", delegate { try { AppSettings next = new AppSettings { SourceLanguage = SourceLanguage, TargetLanguage = ((LanguageItem)langs.SelectedItem).Code, CaptureHotkey = capture.Text, SelectionHotkey = selection.Text, CloseToTray = tray.IsChecked == true, StartMinimized = minimized.IsChecked == true }; if (SaveRequested != null) SaveRequested(next); Settings = next; TargetLanguageBox.SelectedItem = LanguageItem.All.First(l => l.Code == next.TargetLanguage); saved.Text = "已保存，快捷键立即生效。"; saved.Foreground = Design.Blue; } catch (Exception ex) { saved.Text = ex.Message; saved.Foreground = Design.Brush("#B25146"); } }, true); save.HorizontalAlignment = HorizontalAlignment.Right; page.Children.Add(save); page.Children.Add(saved);
            content.Child = new ScrollViewer { Content = page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }
        static Grid SettingRow(string title, string caption, FrameworkElement control)
        {
            Grid row = new Grid { Margin = new Thickness(0, 10, 0, 7) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = Design.Star }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) }); StackPanel copy = new StackPanel(); copy.Children.Add(Design.Text(title, 13)); copy.Children.Add(new TextBlock { Text = caption, FontSize = 10, Foreground = Design.Muted, Margin = new Thickness(0, 5, 0, 0) }); Design.At(row, copy, 0); Design.At(row, control, 0, 1); return row;
        }
        static TextBox HotkeyBox(string value)
        {
            TextBox box = new TextBox { Text = value, IsReadOnly = true, FontSize = 13, Padding = new Thickness(12, 9, 12, 9), Background = Design.Brush("#F5F5F2"), VerticalContentAlignment = VerticalAlignment.Center };
            box.PreviewKeyDown += delegate(object s, KeyEventArgs e) { e.Handled = true; Key key = e.Key == Key.System ? e.SystemKey : e.Key; if (key == Key.Tab) { e.Handled = false; return; } if (key == Key.Escape) { Keyboard.ClearFocus(); return; } if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin) return; ModifierKeys mods = Keyboard.Modifiers; if (mods == ModifierKeys.None || mods == ModifierKeys.Shift) return; string gesture = ""; if ((mods & ModifierKeys.Control) != 0) gesture += "Ctrl+"; if ((mods & ModifierKeys.Alt) != 0) gesture += "Alt+"; if ((mods & ModifierKeys.Shift) != 0) gesture += "Shift+"; if ((mods & ModifierKeys.Windows) != 0) gesture += "Win+"; box.Text = gesture + key.ToString(); }; return box;
        }
        public void PreviewContent()
        {
            SourceBox.Text = "Stay curious.\n\nThe world is full of things we haven't understood yet. A new language is another way to see it.";
            SetResult(new TranslationResult { Text = "保持好奇。\n\n这个世界，还有许多我们尚未理解的事物。一门新的语言，让我们多一种看见世界的方式。", SourceLanguage = "en", TargetLanguage = "zh-CN", Provider = "界面预览 · 示例内容" });
        }
    }

    public class ResultWindow : Window
    {
        TextBox original, translated; TextBlock status; ComboBox sourceLanguage, targetLanguage; Button translate, pin; bool pinned;
        public Action TranslateRequested, InputChanged;
        public string SourceText { get { return original.Text; } set { original.Text = value ?? String.Empty; } }
        public string TranslatedText { get { return translated.Text; } }
        public bool SourceIsEditable { get { return !original.IsReadOnly; } }
        public string SourceLanguage { get { return ((LanguageItem)sourceLanguage.SelectedItem).Code; } }
        public string TargetLanguage { get { return ((LanguageItem)targetLanguage.SelectedItem).Code; } }
        public bool IsBusy { get; private set; }
        public ResultWindow(string kind, string text, string target) : this(kind, text, "auto", target) { }
        public ResultWindow(string kind, string text, string source, string target)
        {
            Title = "映译 · " + kind; Width = 660; Height = 540; MinWidth = 520; MinHeight = 430; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResize; WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(5), GlassFrameThickness = new Thickness(0) }); Design.RoundNative(this);
            Grid root = Design.Rows(Design.Auto, Design.Star, Design.Auto); root.Margin = new Thickness(25, 19, 25, 18);
            Grid head = new Grid { Margin = new Thickness(0, 0, 0, 18), Background = Brushes.Transparent }; StackPanel identity = new StackPanel { Orientation = Orientation.Horizontal, Cursor = Cursors.SizeAll }; identity.Children.Add(Design.Brand(28)); identity.Children.Add(new TextBlock { Text = "  映译  /  " + kind + "    ·    拖动这里移动", VerticalAlignment = VerticalAlignment.Center, FontSize = 11, Foreground = Design.Muted }); head.Children.Add(identity);
            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; pin = Design.Button("置顶", delegate { pinned = !pinned; Topmost = pinned; pin.Content = pinned ? "已置顶" : "置顶"; }); pin.ToolTip = "让翻译浮窗保持在其他窗口上方"; buttons.Children.Add(pin); buttons.Children.Add(Design.Button("✕", Close)); head.Children.Add(buttons);
            head.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && !InsideButton(e.OriginalSource as DependencyObject, head)) { DragMove(); e.Handled = true; } }; Design.At(root, head, 0);
            Grid panes = Design.Rows(Design.Star, new GridLength(12), Design.Star); original = MainWindow.MakeText(false); original.FontSize = 16; original.MaxLength = 5000; original.Text = text; original.ToolTip = "可直接修正识别结果或输入新文字"; translated = MainWindow.MakeText(false); translated.FontSize = 18; translated.ToolTip = "译文也可以手动修改后复制";
            sourceLanguage = MainWindow.MakeLanguage(true, source); targetLanguage = MainWindow.MakeLanguage(false, target);
            Design.At(panes, Pane(sourceLanguage, original, false), 0); Design.At(panes, Pane(targetLanguage, translated, true), 2); Design.At(root, panes, 1);
            Grid foot = new Grid { Margin = new Thickness(0, 15, 0, 0) }; status = Design.Text("正在翻译…", 11, Design.Muted); status.MaxWidth = 400; foot.Children.Add(status);
            translate = Design.Button("翻译  ↗", delegate { if (TranslateRequested != null) TranslateRequested(); }, true); translate.Width = 116; translate.HorizontalAlignment = HorizontalAlignment.Right; foot.Children.Add(translate); Design.At(root, foot, 2); Content = Design.Card(root, "#FAFAF8", 13, new Thickness(0));
            bool firstSourceFocus = true; original.GotKeyboardFocus += delegate { if (firstSourceFocus) { firstSourceFocus = false; original.SelectAll(); } };
            original.TextChanged += delegate { OnInputChanged(); };
            sourceLanguage.SelectionChanged += delegate { OnInputChanged(); };
            targetLanguage.SelectionChanged += delegate { OnInputChanged(); };
            PreviewKeyDown += delegate(object s, KeyEventArgs e) { if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { if (!e.IsRepeat && TranslateRequested != null) TranslateRequested(); e.Handled = true; } else if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
            Loaded += delegate { Topmost = pinned; };
        }
        Border Pane(ComboBox language, TextBox box, bool target)
        {
            Grid pane = Design.Rows(Design.Auto, Design.Star); Grid header = new Grid { Margin = new Thickness(0, 0, 0, 9) }; language.HorizontalAlignment = HorizontalAlignment.Left; header.Children.Add(language);
            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            actions.Children.Add(Design.Text(target ? "译文 · 可编辑" : "原文 · 可编辑", 10, target ? Design.Blue : Design.Muted, true));
            var copy = Design.Button(target ? "复制译文" : "复制原文", delegate { Design.Copy(box.Text, SetStatus); }); copy.Padding = new Thickness(9, 3, 9, 3); copy.MinHeight = 24; copy.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(copy); header.Children.Add(actions); Design.At(pane, header, 0); Design.At(pane, box, 1); return Design.Card(pane, target ? "#F0F4FE" : "#FFFFFF", 12, new Thickness(16, 12, 16, 13));
        }
        void OnInputChanged()
        {
            if (translated.Text.Length != 0) translated.Clear();
            SetIdle("内容或语言已修改，按 Ctrl + Enter 翻译");
            if (InputChanged != null) InputChanged();
        }
        static bool InsideButton(DependencyObject origin, DependencyObject root)
        {
            DependencyObject current = origin;
            while (current != null && current != root)
            {
                if (current is ButtonBase) return true;
                try { current = VisualTreeHelper.GetParent(current); } catch { return true; }
            }
            return false;
        }
        public void SetResult(TranslationResult result)
        {
            translated.Text = result.Text;
            translated.IsReadOnly = false; IsBusy = false; translate.Content = "重新翻译  ↗"; translate.IsEnabled = true;
            SetStatus("翻译完成  ·  " + LanguageItem.Label(result.SourceLanguage) + " → " + LanguageItem.Label(result.TargetLanguage) + "  ·  可继续修改");
        }
        public void SetError(string error) { translated.IsReadOnly = false; IsBusy = false; translate.Content = "重试  ↻"; translate.IsEnabled = true; SetStatus(error, true); }
        public void SetLoading() { IsBusy = true; translate.Content = "取消  ×"; translate.IsEnabled = true; translated.Clear(); translated.IsReadOnly = true; SetStatus("正在翻译…"); }
        public void SetIdle(string message) { translated.IsReadOnly = false; IsBusy = false; translate.Content = "翻译  ↗"; translate.IsEnabled = true; SetStatus(message); }
        public void SetStatus(string message) { SetStatus(message, false); }
        void SetStatus(string message, bool error) { status.Text = message; status.Foreground = error ? Design.Brush("#B25146") : Design.Muted; status.ToolTip = message; }
    }
}
