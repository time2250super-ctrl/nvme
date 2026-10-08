using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveManager.Ai;
using DriveManager.Core.Abstractions;
using DriveManager.Core.Models;

namespace DriveManager.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IDiskService? _diskService;
    private readonly IHealthService? _healthService;
    private readonly IHealthAnalyzer _analyzer;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _status = "Ready.";

    [ObservableProperty]
    private PhysicalDiskInfo? _selectedDisk;

    [ObservableProperty]
    private DriveHealthInfo? _selectedHealth;

    [ObservableProperty]
    private HealthAssessment? _assessment;

    public ObservableCollection<PhysicalDiskInfo> Disks { get; } = [];

    public DashboardViewModel(IHealthAnalyzer analyzer, IDiskService? diskService = null, IHealthService? healthService = null)
    {
        _analyzer = analyzer;
        _diskService = diskService;
        _healthService = healthService;
    }

    /// <summary>Design-time / fallback constructor.</summary>
    public DashboardViewModel() : this(new RuleBasedHealthAnalyzer())
    {
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_diskService is null)
        {
            Status = "Disk service is not available on this platform.";
            return;
        }

        try
        {
            IsBusy = true;
            Status = "Scanning disks...";
            var disks = await _diskService.GetDisksAsync();

            Disks.Clear();
            foreach (var d in disks)
                Disks.Add(d);

            // Prefer the NVMe device (the SPCC SSD) as the default selection.
            SelectedDisk = Disks.FirstOrDefault(d => d.IsNvme) ?? Disks.FirstOrDefault();
            Status = $"Found {Disks.Count} disk(s).";
        }
        catch (Exception ex)
        {
            Status = "Error scanning disks: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedDiskChanged(PhysicalDiskInfo? value)
    {
        SelectedHealth = null;
        Assessment = null;
        if (value is not null)
            _ = LoadHealthAsync(value);
    }

    [RelayCommand]
    private async Task LoadHealthAsync(PhysicalDiskInfo? disk)
    {
        disk ??= SelectedDisk;
        if (disk is null)
            return;

        if (_healthService is null || !_healthService.IsSupported)
        {
            Status = "Health service is not available on this platform.";
            return;
        }

        try
        {
            IsBusy = true;
            Status = $"Reading health from disk {disk.Number} ({disk.BusType})...";
            var health = await _healthService.ReadHealthAsync(disk);
            SelectedHealth = health;

            if (health is null)
            {
                Status = "No health data available for this disk. Run the app as Administrator to access drive diagnostics.";
                Assessment = null;
            }
            else
            {
                Assessment = _analyzer.Assess(health);
                Status = $"Health read OK. Risk score {Assessment.RiskScore}/100 ({Assessment.Level}).";
            }
        }
        catch (Exception ex)
        {
            Status = "Error reading health: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
