using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RenOff.Data;

namespace RenOff.App;

public partial class App : System.Windows.Application
{
    private TrayService? _tray;

    public static bool IsExiting { get; set; }
    public static bool IsRecreatingWindow { get; private set; }
    public static string UiStyle { get; private set; } = "modern";

    private const string StringsMarker = "Resources/Strings.";
    private const string ThemesMarker = "Themes/";
    private const string StylesMarker = "Styles/";

    private static Icon? _cachedAppIcon;
    private static ImageSource? _cachedAppIconImageSource;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyStartupSettings();
        MainWindow = new MainWindow();
        MainWindow.Show();
        AppLockService.Start(MainWindow);
        _tray = new TrayService();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    public static void ShowTrayBalloon(string title, string text, int timeoutMs = 3500)
    {
        if (Current is not App app) return;
        app._tray?.ShowBalloon(title, text, timeoutMs);
    }

    public static Icon GetAppIcon()
    {
        if (_cachedAppIcon is not null) return _cachedAppIcon;

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "RenOff.ico"),
            Path.Combine(baseDir, "Assets", "RenOff.ico"),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var loaded = new Icon(stream);
            _cachedAppIcon = (Icon)loaded.Clone();
            return _cachedAppIcon;
        }

        _cachedAppIcon = SystemIcons.Information;
        return _cachedAppIcon;
    }

    public static ImageSource? GetAppIconImageSource()
    {
        if (_cachedAppIconImageSource is not null) return _cachedAppIconImageSource;

        var icon = GetAppIcon();
        var source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());

        source.Freeze();
        _cachedAppIconImageSource = source;
        return _cachedAppIconImageSource;
    }

    public static void ApplyLanguage(string languageCode)
    {
        var source = languageCode.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? new Uri("Resources/Strings.en.xaml", UriKind.Relative)
            : new Uri("Resources/Strings.it.xaml", UriKind.Relative);

        ReplaceMergedDictionary(StringsMarker, source);
        NormalizeMergedDictionariesOrder();
    }

    public static void ApplyTheme(string theme)
    {
        if (UiStyle.Equals("classic", StringComparison.OrdinalIgnoreCase))
        {
            RemoveMergedDictionaries(ThemesMarker);
            NormalizeMergedDictionariesOrder();
            return;
        }

        var source = theme.Equals("dark", StringComparison.OrdinalIgnoreCase)
            ? new Uri("Themes/Dark.xaml", UriKind.Relative)
            : new Uri("Themes/Light.xaml", UriKind.Relative);

        ReplaceMergedDictionary(ThemesMarker, source);
        NormalizeMergedDictionariesOrder();
    }

    public static void ApplyStyle(string style)
    {
        UiStyle = style.Equals("classic", StringComparison.OrdinalIgnoreCase) ? "classic" : "modern";
        var source = UiStyle == "classic"
            ? new Uri("Styles/Classic.xaml", UriKind.Relative)
            : new Uri("Styles/Modern.xaml", UriKind.Relative);

        ReplaceMergedDictionary(StylesMarker, source);
        if (UiStyle.Equals("classic", StringComparison.OrdinalIgnoreCase))
        {
            RemoveMergedDictionaries(ThemesMarker);
        }
        NormalizeMergedDictionariesOrder();
    }

    public static void RecreateMainWindow()
    {
        var app = Current;
        if (app is null) return;

        var old = app.MainWindow;
        if (old is null) return;

        var wasVisible = old.Visibility == Visibility.Visible;
        var oldLeft = old.Left;
        var oldTop = old.Top;
        var oldWidth = old.Width;
        var oldHeight = old.Height;
        var oldState = old.WindowState;

        IsRecreatingWindow = true;
        var next = new MainWindow
        {
            Left = oldLeft,
            Top = oldTop,
            Width = oldWidth,
            Height = oldHeight,
            WindowState = oldState,
        };
        app.MainWindow = next;
        AppLockService.RegisterActivitySource(next);
        if (wasVisible)
        {
            next.Show();
        }
        else
        {
            next.Hide();
        }
        old.Close();
        IsRecreatingWindow = false;
    }

    private static void ReplaceMergedDictionary(string sourceMarker, Uri newSource)
    {
        var app = Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;
        for (var i = 0; i < merged.Count; i++)
        {
            var existing = merged[i].Source;
            var existingText = existing?.ToString() ?? "";
            if (!existingText.Contains(sourceMarker, StringComparison.OrdinalIgnoreCase)) continue;

            merged[i] = new ResourceDictionary { Source = newSource };
            return;
        }

        merged.Add(new ResourceDictionary { Source = newSource });
    }

    private static void RemoveMergedDictionaries(string sourceMarker)
    {
        var app = Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var existing = merged[i].Source;
            var existingText = existing?.ToString() ?? "";
            if (!existingText.Contains(sourceMarker, StringComparison.OrdinalIgnoreCase)) continue;
            merged.RemoveAt(i);
        }
    }

    private static void NormalizeMergedDictionariesOrder()
    {
        var app = Current;
        if (app is null) return;

        var merged = app.Resources.MergedDictionaries;

        ResourceDictionary? strings = null;
        ResourceDictionary? themes = null;
        ResourceDictionary? styles = null;

        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var srcText = merged[i].Source?.ToString() ?? "";
            if (srcText.Contains(StringsMarker, StringComparison.OrdinalIgnoreCase))
            {
                strings ??= merged[i];
                merged.RemoveAt(i);
                continue;
            }

            if (srcText.Contains(ThemesMarker, StringComparison.OrdinalIgnoreCase))
            {
                themes ??= merged[i];
                merged.RemoveAt(i);
                continue;
            }

            if (srcText.Contains(StylesMarker, StringComparison.OrdinalIgnoreCase))
            {
                styles ??= merged[i];
                merged.RemoveAt(i);
            }
        }

        if (strings is not null) merged.Insert(0, strings);
        if (!UiStyle.Equals("classic", StringComparison.OrdinalIgnoreCase) && themes is not null)
        {
            merged.Add(themes);
        }
        if (styles is not null) merged.Add(styles);
    }

    private static void ApplyStartupSettings()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RenOff",
            "renoff.db");

        var store = new LocalSqliteStore(dbPath);
        var lang = store.GetSetting("ui.language") ?? "it";
        var style = store.GetSetting("ui.style") ?? "modern";
        var theme = store.GetSetting("ui.theme") ?? "light";

        ApplyLanguage(lang);
        UiStyle = style.Equals("classic", StringComparison.OrdinalIgnoreCase) ? "classic" : "modern";
        ApplyTheme(theme);
        ApplyStyle(UiStyle);
    }
}
