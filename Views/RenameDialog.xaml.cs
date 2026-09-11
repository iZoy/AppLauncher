using System.Windows;
using AppLauncher.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Key = System.Windows.Input.Key;

namespace AppLauncher.Views;

public partial class RenameDialog : Window
{
    public string ResultName { get; private set; } = string.Empty;

    public RenameDialog(string currentName)
    {
        InitializeComponent();

        Title = LocalizationService.Get("Rename_Title");
        TitleText.Text = LocalizationService.Get("Rename_Header");
        CancelBtn.Content = LocalizationService.Get("Rename_Cancel");
        OkBtn.Content = LocalizationService.Get("Rename_Confirm");

        NameInput.Text = currentName;
        NameInput.SelectAll();
        Loaded += (s, e) => NameInput.Focus();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        ResultName = NameInput.Text.Trim();
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void NameInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OkButton_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            CancelButton_Click(sender, e);
        }
    }
}
