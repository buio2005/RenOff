using System.Windows;

namespace RenOff.App;

public partial class RecoveryCodeDialog : Window
{
    public RecoveryCodeDialog(string code)
    {
        InitializeComponent();
        CodeBox.Text = code;
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(CodeBox.Text);
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
