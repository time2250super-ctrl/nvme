using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveManager.Ai;
using DriveManager.Core.Abstractions;
using DriveManager.Core.Models;
using DriveManager.Core.Telemetry;

namespace DriveManager.App.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IDiskService? _diskService;
    private readonly IHealthService? _healthService;
    private readonly IHealthAnalyzer _analyzer;
    private readonly ITelemetryStore? _telemetry;
    private readonly IDriveToolsService? _tools;

    private DriveOperation? _pendingOperation;

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

    // --- History / trend ---
    [ObservableProperty]
    private int _historyCount;

    [ObservableProperty]
    private string _trendSummary = string.Empty;

    // --- Drive tools (non-destructive) ---
    [ObservableProperty]
    private VolumeInfo? _selectedVolume;

    [ObservableProperty]
    private string _newLabel = string.Empty;

    [ObservableProperty]
    private string _newDriveLetter = string.Empty;

    [ObservableProperty]
    private string _toolsPreview = string.Empty;

    [ObservableProperty]
    private string _toolsStatus = "Select a volume to manage.";

    [ObservableProperty]
    private string _confirmationHint = string.Empty;

    [ObservableProperty]
    private string _confirmationInput = string.Empty;

    public ObservableCollection<PhysicalDiskInfo> Disks { get; } = [];
    public ObservableCollection<DriveHealthInfo> History { get; } = [];

    public bool ToolsAvailable => _tools is { IsSupported: true };

    public DashboardViewModel(
        IHealthAnalyzer analyzer,
        IDiskService? diskService = null,
        IHealthService? healthService = null,
        ITelemetryStore? telemetry = null,
        IDriveToolsService? tools = null)
    {
        _analyzer = analyzer;
        _diskService = diskService;
        _healthService = healthService;
        _telemetry = telemetry;
        _tools = tools;
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
        History.Clear();
        HistoryCount = 0;
        TrendSummary = string.Empty;
        SelectedVolume = value?.Volumes.FirstOrDefault(v => v.DriveLetter is not null)
                          ?? value?.Volumes.FirstOrDefault();
        ToolsPreview = string.Empty;
        ConfirmationHint = string.Empty;
        _pendingOperation = null;
        if (value is not null)
            _ = LoadHealthAsync(value);
    }

    private static string DiskKey(PhysicalDiskInfo d)
        => string.IsNullOrWhiteSpace(d.SerialNumber) ? $"disk{d.Number}:{d.FriendlyName}" : d.SerialNumber!.Trim();

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
                // Persist a snapshot and refresh history/trend (non-destructive).
                if (_telemetry is not null)
                {
                    try
                    {
                        var key = DiskKey(disk);
                        await _telemetry.SaveAsync(key, health);
                        await LoadHistoryAsync(key);
                    }
                    catch (Exception tex)
                    {
                        TrendSummary = "History unavailable: " + tex.Message;
                    }
                }

                Assessment = _analyzer.Assess(health, History.Count > 0 ? History.ToList() : null);
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

    private async Task LoadHistoryAsync(string key)
    {
        if (_telemetry is null) return;

        var history = await _telemetry.GetHistoryAsync(key, 50);
        History.Clear();
        foreach (var h in history)
            History.Add(h);
        HistoryCount = await _telemetry.CountAsync(key);

        // Simple temperature trend across the two most recent snapshots.
        var temps = history.Where(h => h.TemperatureCelsius is not null).ToList();
        if (temps.Count >= 2)
        {
            var newest = temps[0].TemperatureCelsius!.Value;
            var previous = temps[1].TemperatureCelsius!.Value;
            var delta = newest - previous;
            var arrow = delta > 0.5 ? "up" : delta < -0.5 ? "down" : "steady";
            TrendSummary = $"{HistoryCount} snapshot(s). Temp {newest:0.#} C ({arrow} {delta:+0.#;-0.#;0} C vs previous).";
        }
        else
        {
            TrendSummary = $"{HistoryCount} snapshot(s) recorded. Read again over time to build a trend.";
        }
    }

    // --- Drive tools commands (preview-first) ---------------------------------

    [RelayCommand]
    private void PreviewRename()
    {
        if (!TryGetToolTarget(out var disk, out var letter)) return;
        if (string.IsNullOrWhiteSpace(NewLabel)) { ToolsStatus = "Enter a new label first."; return; }
        SetPending(_tools!.BuildSetLabel(disk, letter, NewLabel.Trim()));
    }

    [RelayCommand]
    private void PreviewChangeLetter()
    {
        if (!TryGetToolTarget(out var disk, out var letter)) return;
        if (string.IsNullOrWhiteSpace(NewDriveLetter)) { ToolsStatus = "Enter a target drive letter first."; return; }
        SetPending(_tools!.BuildChangeDriveLetter(disk, letter, NewDriveLetter.Trim()[0]));
    }

    [RelayCommand]
    private void PreviewRemoveLetter()
    {
        if (!TryGetToolTarget(out var disk, out var letter)) return;
        SetPending(_tools!.BuildRemoveDriveLetter(disk, letter));
    }

    [RelayCommand]
    private void PreviewOffline()
    {
        if (SelectedDisk is null || _tools is null) { ToolsStatus = "Select a disk first."; return; }
        SetPending(_tools.BuildSetDiskOffline(SelectedDisk, offline: true));
    }

    [RelayCommand]
    private void PreviewOnline()
    {
        if (SelectedDisk is null || _tools is null) { ToolsStatus = "Select a disk first."; return; }
        SetPending(_tools.BuildSetDiskOffline(SelectedDisk, offline: false));
    }

    [RelayCommand]
    private async Task ApplyPendingAsync()
    {
        if (_pendingOperation is null || _tools is null)
        {
            ToolsStatus = "Nothing to apply. Build a preview first.";
            return;
        }

        var op = _pendingOperation;
        if (op.Preview.Warnings.Any(w => w.StartsWith("BLOCKED", StringComparison.OrdinalIgnoreCase)))
        {
            ToolsStatus = "This operation is blocked by safety rules and will not run.";
            return;
        }

        try
        {
            IsBusy = true;
            ToolsStatus = $"Applying: {op.Preview.Title}...";
            await _tools.ExecuteAsync(op, ConfirmationInput);
            ToolsStatus = $"Done: {op.Preview.Title}.";
            _pendingOperation = null;
            ToolsPreview = string.Empty;
            ConfirmationHint = string.Empty;
            ConfirmationInput = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ToolsStatus = "Failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool TryGetToolTarget(out PhysicalDiskInfo disk, out char letter)
    {
        disk = SelectedDisk!;
        letter = default;
        if (SelectedDisk is null || _tools is null) { ToolsStatus = "Select a disk first."; return false; }
        if (SelectedVolume?.DriveLetter is not char l) { ToolsStatus = "Select a volume with a drive letter."; return false; }
        letter = l;
        return true;
    }

    private void SetPending(DriveOperation op)
    {
        _pendingOperation = op;
        var p = op.Preview;
        var steps = string.Join(Environment.NewLine, p.Steps);
        var warns = p.Warnings.Count > 0 ? Environment.NewLine + "! " + string.Join(Environment.NewLine + "! ", p.Warnings) : string.Empty;
        ToolsPreview = $"{p.Title}{Environment.NewLine}{p.Description}{Environment.NewLine}{Environment.NewLine}{steps}{warns}";
        ConfirmationHint = string.IsNullOrEmpty(p.ConfirmationPhrase)
            ? string.Empty
            : $"Type to confirm:  {p.ConfirmationPhrase}";
        ToolsStatus = "Preview ready. Review, then Apply.";
    }
}
