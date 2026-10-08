using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DriveManager.App.Views;

public partial class SummaryPopup : Window
{
    public SummaryPopup() => InitializeComponent();

    public SummaryPopup(string title, string body) : this()
    {
        Title = title;
        BodyText.Text = body;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
