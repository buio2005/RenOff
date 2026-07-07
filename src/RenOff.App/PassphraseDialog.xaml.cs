using System.Windows;

namespace RenOff.App;

public enum PassphraseDialogResult
{
    Cancelled,
    Plain,
    Encrypted,
}

public partial class PassphraseDialog : Window
{
    private readonly bool _requireConfirmation;

    public string Passphrase { get; private set; } = "";
    public PassphraseDialogResult Outcome { get; private set; } = PassphraseDialogResult.Cancelled;

    public PassphraseDialog(
        string prompt,
        bool requireConfirmation,
        bool showPlainOption,
        string confirmButtonText,
        string? titleText = null,
        string? primaryLabelText = null,
        string? confirmLabelText = null)
    {
        InitializeComponent();
        _requireConfirmation = requireConfirmation;

        DialogTitleText.Text = titleText ?? GetString("AppTitle", "RenOff");
        PromptText.Text = prompt;
        PrimaryLabelText.Text = primaryLabelText ?? GetString("PasswordLabel", "Password");
        ConfirmLabelText.Text = confirmLabelText ?? GetString("ConfirmPasswordLabel", "Conferma password");
        ConfirmGroup.Visibility = requireConfirmation ? Visibility.Visible : Visibility.Collapsed;
        PlainButton.Visibility = showPlainOption ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButton.Content = confirmButtonText;

        Loaded += (_, _) => PasswordBox1.Focus();
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox1.Password;
        if (string.IsNullOrEmpty(password))
        {
            ShowError(GetString("PassphraseEmpty", "Inserisci una password."));
            return;
        }

        if (_requireConfirmation && password != PasswordBox2.Password)
        {
            ShowError(GetString("PassphraseMismatch", "Le due password non coincidono."));
            return;
        }

        Passphrase = password;
        Outcome = PassphraseDialogResult.Encrypted;
        DialogResult = true;
    }

    private void OnPlainClick(object sender, RoutedEventArgs e)
    {
        Outcome = PassphraseDialogResult.Plain;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Outcome = PassphraseDialogResult.Cancelled;
        DialogResult = false;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string GetString(string key, string fallback)
        => System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;
}
