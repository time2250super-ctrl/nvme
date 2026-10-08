using Avalonia.Controls;
using DriveManager.App.ViewModels;

namespace DriveManager.App.Views;

public partial class MainWindow : Window
{
    private DashboardViewModel? _boundVm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not DashboardViewModel vm)
                return;
            if (ReferenceEquals(vm, _boundVm))
                return;

            if (_boundVm is not null)
            {
                _boundVm.PopupRequested -= OnPopupRequested;
                _boundVm.ConfirmRequested -= OnConfirmRequested;
            }

            _boundVm = vm;
            vm.PopupRequested += OnPopupRequested;
            vm.ConfirmRequested += OnConfirmRequested;
        };
    }

    private async void OnPopupRequested(string title, string body)
    {
        var dlg = new SummaryPopup(title, body);
        await dlg.ShowDialog(this);
    }

    private Task<bool> OnConfirmRequested(string title, string message)
        => ConfirmPopup.ShowAsync(this, title, message);
}