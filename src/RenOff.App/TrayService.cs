using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RenOff.Core;
using WinForms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace RenOff.App;

public sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _notifyIcon;
    private readonly DispatcherTimer _nudgeTimer;
    private DateTimeOffset _lastNudgeAt = DateTimeOffset.MinValue;

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

    private void ShowMainWindow()
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

        ProcessDueReminders(vm);
        MaybeTodoNudge(vm);
    }

    private void ProcessDueReminders(MainViewModel vm)
    {
        var due = vm.DequeueDueReminders(DateTimeOffset.UtcNow, limit: 1);
        if (due.Count == 0) return;

        var reminder = due[0];
        ShowReminderPopup(
            reminder,
            snooze: () => vm.SnoozeReminder(reminder.ReminderId, TimeSpan.FromMinutes(10)),
            dismiss: () => vm.DismissReminder(reminder.ReminderId));
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

    private void ShowReminderPopup(ReminderNotification reminder, Action snooze, Action dismiss)
    {
        var text = $"{GetString("ReminderPrefix", "Reminder:")} {reminder.ItemTitle}";
        ShowBalloon("RenOff", text);
        var title = GetString("ReminderPopupTitle", "RenOff");
        ShowPopup(title, text, showSnooze: true, snooze: snooze, dismiss: dismiss);
    }

    private void ShowPopup(string title, string text)
    {
        ShowPopup(title, text, showSnooze: false, snooze: null, dismiss: null);
    }

    private void ShowPopup(string text, bool showSnooze, Action? snooze, Action? dismiss)
    {
        ShowPopup("RenOff", text, showSnooze, snooze, dismiss);
    }

    private void ShowPopup(string title, string text, bool showSnooze, Action? snooze, Action? dismiss)
    {
        var app = WpfApplication.Current;
        if (app is null) return;

        app.Dispatcher.Invoke(() =>
        {
            var workArea = SystemParameters.WorkArea;
            var width = 380.0;
            var height = 110.0;
            var margin = 16.0;

            var window = new Window
            {
                Width = width,
                Height = height,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Topmost = true,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                ShowActivated = false,
                Left = workArea.Right - width - margin,
                Top = workArea.Bottom - height - margin,
            };

            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, 30, 30, 30)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.9,
                Margin = new Thickness(0, 0, 0, 6),
            });
            stack.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.95,
            });

            if (showSnooze)
            {
                var buttons = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Margin = new Thickness(0, 10, 0, 0),
                };

                var snoozeButton = new System.Windows.Controls.Button
                {
                    Content = "Snooze 10m",
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 8, 0),
                };
                snoozeButton.Click += (_, _) =>
                {
                    snooze?.Invoke();
                    window.Close();
                };
                buttons.Children.Add(snoozeButton);

                var doneButton = new System.Windows.Controls.Button
                {
                    Content = GetString("Ok", "Ok"),
                    Padding = new Thickness(10, 6, 10, 6),
                };
                doneButton.Click += (_, _) =>
                {
                    dismiss?.Invoke();
                    window.Close();
                    ShowMainWindow();
                };
                buttons.Children.Add(doneButton);

                stack.Children.Add(buttons);
            }

            border.Child = stack;
            window.Content = border;

            window.MouseLeftButtonUp += (_, _) =>
            {
                window.Close();
                ShowMainWindow();
            };

            var timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(15),
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                window.Close();
            };

            window.Closed += (_, _) => timer.Stop();
            window.Show();
            timer.Start();
        });
    }
}
