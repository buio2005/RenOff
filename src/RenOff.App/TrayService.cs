using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using RenOff.Core;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using WinForms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace RenOff.App;

public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly DispatcherTimer _nudgeTimer;
    private DateTimeOffset _lastNudgeAt = DateTimeOffset.MinValue;
    private readonly Dictionary<Guid, DateTimeOffset> _shownReminders = new();
    private static readonly TimeSpan ReminderReShowAfter = TimeSpan.FromMinutes(10);
    private static readonly List<Window> OpenPopups = new();

    public TrayService()
    {
        _notifyIcon = new WinForms.NotifyIcon
        {
            Text = "RenOff",
            Icon = App.GetAppIcon(),
            Visible = true,
            ContextMenuStrip = BuildContextMenu(),
        };

        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();
        _notifyIcon.BalloonTipClicked += (_, _) => ShowMainWindow();

        _nudgeTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(5),
        };
        _nudgeTimer.Tick += (_, _) => OnTick();
        _nudgeTimer.Start();
    }

    public void Dispose()
    {
        _nudgeTimer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    public void ShowBalloon(string title, string text, int timeoutMs = 5000)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.BalloonTipIcon = WinForms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(timeoutMs);
    }

    public void ShowNudge(string text)
    {
        ShowBalloon("RenOff", text);
        var title = GetString("NudgePopupTitle", "Non ti dimenticare di leggere le note");
        ShowPopup(title, text);
    }

    private WinForms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new WinForms.ContextMenuStrip();

        var open = new WinForms.ToolStripMenuItem(GetString("TrayOpen", "Apri RenOff"));
        open.Click += (_, _) => ShowMainWindow();
        menu.Items.Add(open);

        var lockNow = new WinForms.ToolStripMenuItem(GetString("TrayLockNow", "Blocca RenOff"));
        lockNow.Click += (_, _) =>
        {
            if (!AppLockService.HasPasswordConfigured())
            {
                var app = WpfApplication.Current;
                app?.Dispatcher.Invoke(() => System.Windows.MessageBox.Show(
                    GetString("AppLockNotConfigured", "Imposta prima una password dalle Impostazioni."),
                    "RenOff",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information));
                return;
            }

            AppLockService.LockNow();
        };
        menu.Items.Add(lockNow);

        menu.Items.Add(new WinForms.ToolStripSeparator());

        var exit = new WinForms.ToolStripMenuItem(GetString("TrayExit", "Esci"));
        exit.Click += (_, _) =>
        {
            if (WpfApplication.Current is null) return;
            App.IsExiting = true;
            Dispose();
            WpfApplication.Current.Shutdown();
        };
        menu.Items.Add(exit);

        return menu;
    }

    private static string GetString(string key, string fallback)
    {
        var app = WpfApplication.Current;
        if (app is null) return fallback;
        return app.TryFindResource(key) as string ?? fallback;
    }

    private static Brush GetBrush(string key, Color fallback)
    {
        var app = WpfApplication.Current;
        if (app?.TryFindResource(key) is Brush brush) return brush;
        return new SolidColorBrush(fallback);
    }

    private void ShowMainWindow()
    {
        AppLockService.RequestShowMainWindow(ShowMainWindowCore);
    }

    private void ShowMainWindowCore()
    {
        var app = WpfApplication.Current;
        if (app is null) return;

        var window = app.MainWindow;
        if (window is null) return;

        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();

        if (window.DataContext is MainViewModel vm)
        {
            vm.MarkListViewed();
        }
    }

    private void OnTick()
    {
        var app = WpfApplication.Current;
        if (app?.MainWindow?.DataContext is not MainViewModel vm) return;

        // While locked, don't leak note titles into balloons/pop-ups and don't
        // burn a reminder the user can't act on.
        if (AppLockService.IsLocked) return;

        ProcessDueReminders(vm);
        MaybeTodoNudge(vm);
    }

    private void ProcessDueReminders(MainViewModel vm)
    {
        var now = DateTimeOffset.UtcNow;
        var due = vm.GetDueReminders(now, limit: 5);
        if (due.Count == 0) return;

        // A reminder stays due until the user acts on it, so remember which ones
        // were just shown instead of re-opening the same pop-up every 5 seconds.
        foreach (var id in _shownReminders.Where(p => now - p.Value >= ReminderReShowAfter).Select(p => p.Key).ToList())
        {
            _shownReminders.Remove(id);
        }

        ReminderNotification? next = null;
        foreach (var candidate in due)
        {
            if (_shownReminders.ContainsKey(candidate.ReminderId)) continue;
            next = candidate;
            break;
        }

        if (next is null) return;

        var reminder = next;
        _shownReminders[reminder.ReminderId] = now;

        ShowReminderPopup(
            reminder,
            snooze: duration =>
            {
                _shownReminders.Remove(reminder.ReminderId);
                vm.SnoozeReminder(reminder.ReminderId, duration);
            },
            dismiss: () =>
            {
                _shownReminders.Remove(reminder.ReminderId);
                vm.DismissReminder(reminder.ReminderId);
            },
            acknowledge: () =>
            {
                _shownReminders.Remove(reminder.ReminderId);
                vm.MarkReminderFired(reminder.ReminderId);
            });
    }

    private void MaybeTodoNudge(MainViewModel vm)
    {
        if (!vm.NudgeEnabled) return;
        if (vm.PendingCount <= 0) return;

        var now = DateTimeOffset.UtcNow;
        var minSinceViewed = now - vm.LastListViewedAtUtc;
        var minSinceNudge = now - _lastNudgeAt;

        var interval = TimeSpan.FromHours(Math.Max(1, vm.NudgeIntervalHours));

        if (minSinceViewed < interval) return;
        if (minSinceNudge < interval) return;

        _lastNudgeAt = now;
        var template = GetString("NudgeTodoTemplate", "Hai {0} to-do in sospeso. Apri RenOff per rivederli.");
        ShowNudge(string.Format(template, vm.PendingCount));
    }

    private void ShowReminderPopup(ReminderNotification reminder, Action<TimeSpan> snooze, Action dismiss, Action acknowledge)
    {
        var text = $"{GetString("ReminderPrefix", "Reminder:")} {reminder.ItemTitle}";
        ShowBalloon("RenOff", text);
        var title = GetString("ReminderPopupTitle", "RenOff");
        ShowPopup(title, text, showSnooze: true, snooze: snooze, dismiss: dismiss, acknowledge: acknowledge);
    }

    private void ShowPopup(string title, string text)
    {
        ShowPopup(title, text, showSnooze: false, snooze: null, dismiss: null, acknowledge: null);
    }

    private void ShowPopup(string title, string text, bool showSnooze, Action<TimeSpan>? snooze, Action? dismiss, Action? acknowledge)
    {
        var app = WpfApplication.Current;
        if (app is null) return;

        app.Dispatcher.Invoke(() =>
        {
            var background = GetBrush("SurfaceBackgroundBrush", Color.FromRgb(30, 30, 30));
            var foreground = GetBrush("WindowForegroundBrush", Color.FromRgb(242, 242, 242));
            var muted = GetBrush("MutedForegroundBrush", Color.FromRgb(160, 160, 160));
            var accent = GetBrush("AccentBrush", Color.FromRgb(58, 110, 165));
            var cardBorder = GetBrush("CardBorderBrush", Color.FromArgb(90, 255, 255, 255));

            const double width = 380.0;

            var window = new Window
            {
                Width = width,
                SizeToContent = SizeToContent.Height,
                MaxHeight = 320,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ShowActivated = false,
                Opacity = 0,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };

            var slide = new TranslateTransform(0, 16);

            var border = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = background,
                BorderBrush = cardBorder,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14),
                RenderTransform = slide,
            };

            if (App.UiStyle.Equals("modern", StringComparison.OrdinalIgnoreCase))
            {
                border.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(0, 0, 0),
                    Direction = 270,
                    ShadowDepth = 3,
                    BlurRadius = 12,
                    Opacity = 0.25,
                };
            }

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = accent,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6),
                TextWrapping = TextWrapping.Wrap,
            });

            stack.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = foreground,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 180,
            });

            if (showSnooze)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = GetString("Reminder", "Reminder"),
                    Foreground = muted,
                    FontSize = 11,
                    Margin = new Thickness(0, 8, 0, 0),
                    Opacity = 0.8,
                });

                var buttons = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Margin = new Thickness(0, 12, 0, 0),
                };

                buttons.Children.Add(BuildSnoozeButton(GetString("Snooze10m", "10 min"), TimeSpan.FromMinutes(10), snooze, window));
                buttons.Children.Add(BuildSnoozeButton(GetString("Snooze1h", "1 ora"), TimeSpan.FromHours(1), snooze, window));
                buttons.Children.Add(BuildSnoozeButton(GetString("SnoozeTomorrow", "Domani"), TimeSpanUntilTomorrowMorning(), snooze, window));

                var doneButton = new System.Windows.Controls.Button
                {
                    Content = GetString("Ok", "Fatto"),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(4, 0, 0, 0),
                };
                doneButton.Click += (_, _) =>
                {
                    dismiss?.Invoke();
                    ClosePopup(window);
                    ShowMainWindow();
                };
                buttons.Children.Add(doneButton);

                stack.Children.Add(buttons);
            }

            border.Child = stack;
            window.Content = border;

            window.MouseLeftButtonUp += (_, _) =>
            {
                // Opening the app counts as acknowledging the reminder.
                acknowledge?.Invoke();
                ClosePopup(window);
                ShowMainWindow();
            };

            var timeout = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(showSnooze ? 20 : 15),
            };
            timeout.Tick += (_, _) =>
            {
                timeout.Stop();
                ClosePopup(window);
            };

            window.Loaded += (_, _) =>
            {
                OpenPopups.Add(window);
                RepositionPopups();
                AnimateIn(window, slide);
                timeout.Start();
            };

            window.Closed += (_, _) =>
            {
                timeout.Stop();
                OpenPopups.Remove(window);
                RepositionPopups();
            };

            window.Show();
        });
    }

    private static System.Windows.Controls.Button BuildSnoozeButton(string label, TimeSpan duration, Action<TimeSpan>? snooze, Window window)
    {
        var button = new System.Windows.Controls.Button
        {
            Content = label,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 6, 0),
        };
        button.Click += (_, _) =>
        {
            snooze?.Invoke(duration);
            ClosePopup(window);
        };
        return button;
    }

    private static TimeSpan TimeSpanUntilTomorrowMorning()
    {
        var now = DateTime.Now;
        var tomorrowMorning = now.Date.AddDays(1).AddHours(9);
        var span = tomorrowMorning - now;
        return span > TimeSpan.Zero ? span : TimeSpan.FromHours(12);
    }

    private static void AnimateIn(Window window, TranslateTransform slide)
    {
        var fade = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        var move = new DoubleAnimation(16, 0, new Duration(TimeSpan.FromMilliseconds(260)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };

        window.BeginAnimation(UIElement.OpacityProperty, fade);
        slide.BeginAnimation(TranslateTransform.YProperty, move);
    }

    private static void ClosePopup(Window window)
    {
        if (!window.IsLoaded && !OpenPopups.Contains(window))
        {
            return;
        }
        window.Close();
    }

    private static void RepositionPopups()
    {
        var workArea = SystemParameters.WorkArea;
        const double margin = 16;
        const double gap = 10;

        var y = workArea.Bottom - margin;
        for (var i = OpenPopups.Count - 1; i >= 0; i--)
        {
            var w = OpenPopups[i];
            var h = w.ActualHeight > 0 ? w.ActualHeight : 110;
            y -= h;
            w.Left = workArea.Right - w.Width - margin;
            w.Top = y;
            y -= gap;
        }
    }
}
