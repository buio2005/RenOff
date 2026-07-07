using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using RenOff.Data;
using WpfApplication = System.Windows.Application;

namespace RenOff.App;

public static class AppLockService
{
    private const string TimeoutSettingKey = "applock.timeout";
    private const string HashSettingKey = "applock.hash";
    private const string RecoveryHashSettingKey = "applock.recoveryHash";
    private const string RecoveryCodeChars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    private static DateTimeOffset _lastInteractionAtUtc = DateTimeOffset.UtcNow;
    private static DispatcherTimer? _idleTimer;
    private static AppLockWindow? _lockWindow;

    public static bool IsLocked { get; private set; }

    public static void Start(Window mainWindow)
    {
        RegisterActivitySource(mainWindow);

        if (_idleTimer is null)
        {
            _idleTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(20),
            };
            _idleTimer.Tick += (_, _) => CheckIdle();
            _idleTimer.Start();
        }
    }

    public static void RegisterActivitySource(Window window)
    {
        window.PreviewMouseMove += (_, _) => NotifyActivity();
        window.PreviewMouseDown += (_, _) => NotifyActivity();
        window.PreviewKeyDown += (_, _) => NotifyActivity();
    }

    public static void NotifyActivity()
    {
        _lastInteractionAtUtc = DateTimeOffset.UtcNow;
    }

    public static void LockNow()
    {
        if (!HasPasswordConfigured()) return;

        if (IsLocked)
        {
            ShowLockScreen();
            return;
        }

        TriggerLock();
    }

    public static void RequestShowMainWindow(Action showMainWindow)
    {
        if (IsLocked)
        {
            ShowLockScreen();
            return;
        }

        showMainWindow();
    }

    public static bool HasPasswordConfigured()
        => !string.IsNullOrEmpty(OpenStore().GetSetting(HashSettingKey));

    public static bool VerifyPassword(string password)
    {
        var stored = OpenStore().GetSetting(HashSettingKey);
        return !string.IsNullOrEmpty(stored) && AppLockCredential.Verify(password, stored);
    }

    public static string SetPasswordAndGenerateRecoveryCode(string password)
    {
        var store = OpenStore();
        store.SetSetting(HashSettingKey, AppLockCredential.Hash(password));

        var recoveryCode = GenerateRecoveryCode();
        store.SetSetting(RecoveryHashSettingKey, AppLockCredential.Hash(recoveryCode));
        return recoveryCode;
    }

    public static bool HasRecoveryCode()
        => !string.IsNullOrEmpty(OpenStore().GetSetting(RecoveryHashSettingKey));

    public static bool VerifyRecoveryCode(string code)
    {
        var stored = OpenStore().GetSetting(RecoveryHashSettingKey);
        return !string.IsNullOrEmpty(stored) && AppLockCredential.Verify(code, stored);
    }

    public static string GetTimeoutSetting()
        => NormalizeTimeoutValue(OpenStore().GetSetting(TimeoutSettingKey));

    public static void SetTimeoutSetting(string value)
        => OpenStore().SetSetting(TimeoutSettingKey, NormalizeTimeoutValue(value));

    public static void DisableLock()
    {
        var store = OpenStore();
        store.SetSetting(TimeoutSettingKey, "never");
        store.SetSetting(HashSettingKey, "");
        store.SetSetting(RecoveryHashSettingKey, "");
    }

    private static string GenerateRecoveryCode()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(12);
        var sb = new StringBuilder();
        for (var i = 0; i < randomBytes.Length; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append('-');
            sb.Append(RecoveryCodeChars[randomBytes[i] % RecoveryCodeChars.Length]);
        }
        return sb.ToString();
    }

    private static TimeSpan? GetTimeout()
    {
        return GetTimeoutSetting() switch
        {
            "30m" => TimeSpan.FromMinutes(30),
            "1h" => TimeSpan.FromHours(1),
            _ => null,
        };
    }

    private static string NormalizeTimeoutValue(string? value)
    {
        return (value ?? "never").Trim().ToLowerInvariant() switch
        {
            "30m" => "30m",
            "1h" => "1h",
            _ => "never",
        };
    }

    private static void CheckIdle()
    {
        if (IsLocked) return;
        if (!HasPasswordConfigured()) return;

        var timeout = GetTimeout();
        if (timeout is null) return;

        var window = WpfApplication.Current?.MainWindow;
        if (window is null || window.Visibility != Visibility.Visible) return;

        if (DateTimeOffset.UtcNow - _lastInteractionAtUtc >= timeout.Value)
        {
            TriggerLock();
        }
    }

    private static void TriggerLock()
    {
        if (IsLocked) return;

        IsLocked = true;
        WpfApplication.Current?.MainWindow?.Hide();
        ShowLockScreen();
    }

    private static void ShowLockScreen()
    {
        if (_lockWindow is { IsVisible: true })
        {
            _lockWindow.Activate();
            return;
        }

        _lockWindow = new AppLockWindow();
        _lockWindow.Unlocked += OnUnlocked;
        _lockWindow.Show();
        _lockWindow.Activate();
    }

    private static void OnUnlocked(object? sender, EventArgs e)
    {
        if (_lockWindow is not null)
        {
            _lockWindow.Unlocked -= OnUnlocked;
            _lockWindow.Close();
            _lockWindow = null;
        }

        IsLocked = false;
        NotifyActivity();

        var window = WpfApplication.Current?.MainWindow;
        if (window is null) return;

        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();

        if (window.DataContext is MainViewModel vm)
        {
            vm.RefreshAppLockSettings();
        }
    }

    private static LocalSqliteStore OpenStore()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RenOff",
            "renoff.db");
        return new LocalSqliteStore(dbPath);
    }
}
