using System.Windows;

namespace LabelPrintClient;

public partial class PrintWindow : Window
{
    public string? PrintText { get; private set; }
    public string SelectedSize { get; private set; } = "m";

    public PrintWindow(string printerDisplayName, string initialSize)
    {
        InitializeComponent();
        PrinterLabel.Text = $"Printer: {printerDisplayName}";

        switch (initialSize)
        {
            case "s": SizeS.IsChecked = true; break;
            case "l": SizeL.IsChecked = true; break;
            default: SizeM.IsChecked = true; break;
        }

        LabelText.Focus();
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var text = LabelText.Text.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        PrintText = text;
        SelectedSize = SizeS.IsChecked == true ? "s"
            : SizeL.IsChecked == true ? "l"
            : "m";
        Close();
    }
}
