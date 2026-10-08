using Avalonia.Controls;
using DriveManager.App.ViewModels;

namespace DriveManager.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is DashboardViewModel vm)
                vm.PopupRequested += OnPopupRequested;
        };
    }

    private async void OnPopupRequested(string title, string body)
    {
        var dlg = new SummaryPopup(title, body);
        await dlg.ShowDialog(this);
    }
}