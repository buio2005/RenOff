using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace RenOff.App;

public partial class AppLockWindow : Window
{
    private bool _unlockedSuccessfully;
    private bool _exitRequested;

    public event EventHandler? Unlocked;

    public AppLockWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void OnPasswordKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryUnlock();
        }
    }

    private void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        TryUnlock();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        _exitRequested = true;
        App.IsExiting = true;
        Close();
        app.Shutdown();
    }

    private void TryUnlock()
    {
        if (AppLockService.VerifyPassword(PasswordBox.Password))
        {
            _unlockedSuccessfully = true;
            Unlocked?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            ErrorText.Text = GetString("WrongPassphrase", "Password errata o file corrotto.");
            ErrorText.Visibility = Visibility.Visible;
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
    }

    private void OnForgotClick(object sender, RoutedEventArgs e)
    {
        if (!AppLockService.HasRecoveryCode())
        {
            System.Windows.MessageBox.Show(
                GetString("AppLockNoRecovery", "Nessun codice di recupero disponibile per questa configurazione."),
                GetString("AppLockForgotTitle", "Recupero password"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var dialog = new PassphraseDialog(
            GetString("AppLockRecoveryPrompt", "Inserisci il codice di recupero mostrato quando hai impostato la password."),
            requireConfirmation: false,
            showPlainOption: false,
            confirmButtonText: GetString("AppLockRecoveryButton", "Verifica"),
            titleText: GetString("AppLockForgotTitle", "Recupero password"),
            primaryLabelText: GetString("UnlockCodeLabel", "Codice di sblocco"))
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true || dialog.Outcome != PassphraseDialogResult.Encrypted) return;

        if (!AppLockService.VerifyRecoveryCode(dialog.Passphrase))
        {
            System.Windows.MessageBox.Show(
                GetString("AppLockRecoveryInvalid", "Codice di recupero errato."),
                "RenOff",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        AppLockService.DisableLock();
        _unlockedSuccessfully = true;
        Unlocked?.Invoke(this, EventArgs.Empty);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_unlockedSuccessfully && !_exitRequested)
        {
            e.Cancel = true;
        }
    }

    private static string GetString(string key, string fallback)
        => System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;
}
