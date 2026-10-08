using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DriveManager.App.Views;

public partial class ConfirmPopup : Window
{
    private bool _confirmed;

    public ConfirmPopup() => InitializeComponent();

    public ConfirmPopup(string title, string message) : this()
    {
        Title = title;
        MessageText.Text = message;
    }

    public static async Task<bool> ShowAsync(Window owner, string title, string message)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return await Dispatcher.UIThread.InvokeAsync(() => ShowAsync(owner, title, message));

        var dlg = new ConfirmPopup(title, message);
        await dlg.ShowDialog(owner);
        return dlg._confirmed;
    }

    private void OnOk(object? sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _confirmed = false;
        Close();
    }
}
