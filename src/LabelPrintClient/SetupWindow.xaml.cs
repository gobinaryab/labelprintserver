using System.Windows;

namespace LabelPrintClient;

public partial class SetupWindow : Window
{
    public string ServerAddress { get; private set; } = "";

    public SetupWindow(string currentAddress = "")
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(currentAddress))
            AddressBox.Text = currentAddress;
        AddressBox.SelectAll();
        AddressBox.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var address = AddressBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            MessageBox.Show("Please enter a server address.", "Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ServerAddress = address;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
