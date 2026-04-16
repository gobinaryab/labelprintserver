using System.Windows;

namespace LabelPrintClient;

public partial class PrintWindow : Window
{
    public string? PrintText { get; private set; }

    public PrintWindow(string printerDisplayName)
    {
        InitializeComponent();
        PrinterLabel.Text = $"Printer: {printerDisplayName}";
        LabelText.Focus();
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var text = LabelText.Text.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        PrintText = text;
        Close();
    }
}
