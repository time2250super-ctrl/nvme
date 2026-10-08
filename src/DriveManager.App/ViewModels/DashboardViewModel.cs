using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
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
    private readonly IFormatService? _formatService;
    private readonly ISecurityService? _securityService;
    private readonly IModelManager? _models;
    private readonly IChatAssistant? _chat;
    private readonly IPermissionManager? _permissions;

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
    private string _toolsStatus = "Select a volume, then run an action.";

    // --- Danger zone (destructive) ---
    [ObservableProperty]
    private string _selectedFileSystem = "Ntfs";

    [ObservableProperty]
    private string _formatLabel = "DATA";

    [ObservableProperty]
    private string _dangerStatus = "These actions run immediately and erase data.";

    // --- Security / encryption ---
    [ObservableProperty]
    private VolumeSecurityState? _security;

    [ObservableProperty]
    private string _encStatus = "Select a volume to view its encryption status.";

    // --- AI model / local chat ---
    [ObservableProperty]
    private AiModelInfo? _activeModel;

    [ObservableProperty]
    private AiModelInfo? _selectedModel;

    [ObservableProperty]
    private string _modelImportPath = string.Empty;

    [ObservableProperty]
    private string _modelDownloadUrl = string.Empty;

    [ObservableProperty]
    private double _modelProgress;

    [ObservableProperty]
    private bool _isModelBusy;

    [ObservableProperty]
    private string _modelStatus = "No model folder scanned yet.";

    [ObservableProperty]
    private string _chatPrompt = string.Empty;

    [ObservableProperty]
    private string _chatOutput = string.Empty;

    [ObservableProperty]
    private bool _isChatBusy;

    public ObservableCollection<PhysicalDiskInfo> Disks { get; } = [];
    public ObservableCollection<DriveHealthInfo> History { get; } = [];
    public ObservableCollection<FsAdviceRow> FsAdvice { get; } = [];
    public ObservableCollection<AiModelInfo> Models { get; } = [];
    public ObservableCollection<string> FileSystemOptions { get; } = ["Ntfs", "ExFat", "Fat32"];
    public ObservableCollection<PermissionItem> PermissionItems { get; } = [];

    [ObservableProperty]
    private string _permissionSummary = "Permissions not checked yet.";

    [ObservableProperty]
    private bool _isElevated;

    public bool ToolsAvailable => _tools is { IsSupported: true };
    public bool AiAvailable => _models is not null;
    public string ModelsDirectory => _models?.ModelsDirectory ?? "(unavailable)";

    public event Action<string, string>? PopupRequested;

    private void ShowPopup(string title, string body)
        => PopupRequested?.Invoke(title, body);

    public DashboardViewModel(
        IHealthAnalyzer analyzer,
        IDiskService? diskService = null,
        IHealthService? healthService = null,
        ITelemetryStore? telemetry = null,
        IDriveToolsService? tools = null,
        IFormatService? formatService = null,
        ISecurityService? securityService = null,
        IModelManager? models = null,
        IChatAssistant? chat = null,
        IPermissionManager? permissions = null)
    {
        _analyzer = analyzer;
        _diskService = diskService;
        _healthService = healthService;
        _telemetry = telemetry;
        _tools = tools;
        _formatService = formatService;
        _securityService = securityService;
        _models = models;
        _chat = chat;
        _permissions = permissions;
        RefreshModels();
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
            var disks = await _diskService.GetDisksAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Disks.Clear();
                foreach (var d in disks)
                    Disks.Add(d);

                SelectedDisk = Disks.FirstOrDefault();
                Status = $"Found {Disks.Count} disk(s).";
            });
            if (_permissions is not null)
                _ = ApplyPermissionsAsync();
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

    [RelayCommand]
    private async Task ApplyPermissionsAsync()
    {
        if (_permissions is null)
        {
            PermissionSummary = "Permission manager unavailable on this platform.";
            return;
        }

        try
        {
            await _permissions.ApplyAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                PermissionItems.Clear();
                foreach (var item in _permissions.Items)
                    PermissionItems.Add(item);
                PermissionSummary = _permissions.Summary;
                IsElevated = _permissions.IsElevated;
            });
        }
        catch (Exception ex)
        {
            PermissionSummary = "Permission check failed: " + ex.Message;
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

        // Recommend the best filesystem for each volume on this disk.
        FsAdvice.Clear();
        if (value is not null)
        {
            var sysOrBoot = value.IsSystem || value.IsBoot;
            foreach (var vol in value.Volumes)
            {
                var advice = FilesystemAdvisor.Recommend(value.BusType, vol.SizeBytes, sysOrBoot);
                FsAdvice.Add(new FsAdviceRow(
                    vol.DriveLetter, vol.FileSystem, advice.Recommended, advice.Rationale,
                    advice.MatchesCurrent(vol.FileSystem)));
            }
            _ = LoadHealthAsync(value);
        }
    }

    partial void OnSelectedVolumeChanged(VolumeInfo? value)
    {
        Security = null;
        if (value?.DriveLetter is char letter)
            _ = LoadSecurityAsync(letter);
        else
            EncStatus = "This volume has no drive letter to inspect.";
    }

    [RelayCommand]
    private async Task LoadSecurityAsync(char letter)
    {
        if (_securityService is null) { EncStatus = "Security service unavailable."; return; }
        try
        {
            EncStatus = $"Reading encryption status of {letter}:...";
            Security = await _securityService.GetEncryptionStateAsync(letter);
            EncStatus = Security.Summary;
        }
        catch (Exception ex)
        {
            EncStatus = "Error reading encryption status: " + ex.Message;
        }
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

    // --- Drive tools (run immediately) ----------------------------------------

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (!TryGetToolTarget(out var disk, out var letter)) return;
        if (string.IsNullOrWhiteSpace(NewLabel)) { ToolsStatus = "Enter a new label first."; return; }
        await RunToolAsync(_tools!.BuildSetLabel(disk, letter, NewLabel.Trim()));
    }

    [RelayCommand]
    private async Task ChangeLetterAsync()
    {
        if (!TryGetToolTarget(out var disk, out var letter)) return;
        if (string.IsNullOrWhiteSpace(NewDriveLetter)) { ToolsStatus = "Enter a target drive letter first."; return; }
        await RunToolAsync(_tools!.BuildChangeDriveLetter(disk, letter, NewDriveLetter.Trim()[0]));
    }

    [RelayCommand]
    private async Task RemoveLetterAsync()
    {
        if (!TryGetToolTarget(out var disk, out var letter)) return;
        await RunToolAsync(_tools!.BuildRemoveDriveLetter(disk, letter));
    }

    [RelayCommand]
    private async Task OfflineAsync()
    {
        if (SelectedDisk is null || _tools is null) { ToolsStatus = "Select a disk first."; return; }
        await RunToolAsync(_tools.BuildSetDiskOffline(SelectedDisk, offline: true));
    }

    [RelayCommand]
    private async Task OnlineAsync()
    {
        if (SelectedDisk is null || _tools is null) { ToolsStatus = "Select a disk first."; return; }
        await RunToolAsync(_tools.BuildSetDiskOffline(SelectedDisk, offline: false));
    }

    private async Task RunToolAsync(DriveOperation op)
    {
        if (_tools is null) return;
        try
        {
            IsBusy = true;
            ToolsStatus = $"Running: {op.Preview.Title}...";
            await _tools.ExecuteAsync(op);
            ToolsStatus = $"Done: {op.Preview.Title}.";
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

    // --- Danger zone (run immediately) ----------------------------------------

    private FileSystemType ParseFileSystem() => SelectedFileSystem switch
    {
        "ExFat" => FileSystemType.ExFat,
        "Fat32" => FileSystemType.Fat32,
        _ => FileSystemType.Ntfs,
    };

    [RelayCommand]
    private async Task QuickFormatAsync()
    {
        if (SelectedDisk is null || _formatService is null) { DangerStatus = "Select a disk first."; return; }
        if (SelectedVolume?.DriveLetter is not char letter) { DangerStatus = "Select a volume with a drive letter in the Tools tab first."; return; }
        var plan = _formatService.PreviewQuickFormat(SelectedDisk, letter, ParseFileSystem(), FormatLabel ?? string.Empty);
        await RunDestructiveAsync(plan, p => _formatService.ExecuteAsync(p));
    }

    [RelayCommand]
    private async Task RepartitionAsync()
    {
        if (SelectedDisk is null || _formatService is null) { DangerStatus = "Select a disk first."; return; }
        var plan = _formatService.PreviewRepartition(SelectedDisk, [SelectedDisk.SizeBytes], ParseFileSystem());
        await RunDestructiveAsync(plan, p => _formatService.ExecuteAsync(p));
    }

    [RelayCommand]
    private async Task SecureEraseAsync()
    {
        if (SelectedDisk is null || _securityService is null) { DangerStatus = "Select a disk first."; return; }
        var plan = _securityService.PreviewSecureErase(SelectedDisk);
        await RunDestructiveAsync(plan, p => _securityService.ExecuteAsync(p));
    }

    private async Task RunDestructiveAsync(OperationPreview plan, Func<OperationPreview, Task> exec)
    {
        try
        {
            IsBusy = true;
            DangerStatus = $"Running: {plan.Title}...";
            await exec(plan);
            DangerStatus = $"Done: {plan.Title}.";
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            DangerStatus = "Failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- Encryption (run immediately) -----------------------------------------

    [RelayCommand]
    private async Task EnableEncryptionAsync()
    {
        if (_securityService is null || SelectedVolume?.DriveLetter is not char letter)
        { EncStatus = "Select a volume with a drive letter first."; return; }
        await RunEncryptionAsync(_securityService.PreviewEnableEncryption(letter));
    }

    [RelayCommand]
    private async Task DisableEncryptionAsync()
    {
        if (_securityService is null || SelectedVolume?.DriveLetter is not char letter)
        { EncStatus = "Select a volume with a drive letter first."; return; }
        await RunEncryptionAsync(_securityService.PreviewDisableEncryption(letter));
    }

    private async Task RunEncryptionAsync(OperationPreview plan)
    {
        if (_securityService is null) return;
        try
        {
            IsBusy = true;
            EncStatus = $"Running: {plan.Title}...";
            await _securityService.ExecuteEncryptionAsync(plan);
            if (SelectedVolume?.DriveLetter is char letter)
                await LoadSecurityAsync(letter);
            EncStatus = $"Done: {plan.Title}. {(Security?.Summary ?? string.Empty)}";
        }
        catch (Exception ex)
        {
            EncStatus = "Failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- AI model management + local chat -------------------------------------

    [RelayCommand]
    private void RefreshModels()
    {
        if (_models is null) { ModelStatus = "AI model manager unavailable."; return; }
        try
        {
            var list = _models.ListModels();
            Models.Clear();
            foreach (var m in list) Models.Add(m);
            ActiveModel = _models.ActiveModel;
            SelectedModel = ActiveModel ?? Models.FirstOrDefault();
            ModelStatus = Models.Count == 0
                ? $"No models found in {_models.ModelsDirectory}. Download or import a .gguf file to begin."
                : ActiveModel is null
                    ? $"{Models.Count} model(s) found. Select one and set it active."
                    : $"Active model: {ActiveModel.Name} ({ActiveModel.SizeDisplay}). {Models.Count} model(s) available.";
        }
        catch (Exception ex)
        {
            ModelStatus = "Error scanning models: " + ex.Message;
        }
    }

    [RelayCommand]
    private void SetActiveModel()
    {
        if (_models is null || SelectedModel is null) { ModelStatus = "Select a model first."; return; }
        _models.SetActive(SelectedModel.FilePath);
        RefreshModels();
        ModelStatus = $"Active model set to {SelectedModel?.Name ?? "(none)"}.";
    }

    [RelayCommand]
    private async Task ImportModelAsync()
    {
        if (_models is null) { ModelStatus = "AI model manager unavailable."; return; }
        var path = ModelImportPath?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path)) { ModelStatus = "Enter the full path to a .gguf file to import."; return; }
        try
        {
            IsModelBusy = true;
            ModelProgress = 0;
            ModelStatus = $"Importing {System.IO.Path.GetFileName(path)}...";
            var progress = new Progress<ModelTransferProgress>(p => ModelProgress = p.Percent);
            var model = await _models.ImportAsync(path, progress);
            ModelImportPath = string.Empty;
            RefreshModels();
            ModelStatus = $"Imported {model.Name} ({model.SizeDisplay}) and set active.";
        }
        catch (Exception ex)
        {
            ModelStatus = "Import failed: " + ex.Message;
        }
        finally
        {
            IsModelBusy = false;
            ModelProgress = 0;
        }
    }

    [RelayCommand]
    private async Task DownloadModelAsync()
    {
        if (_models is null) { ModelStatus = "AI model manager unavailable."; return; }
        var raw = ModelDownloadUrl?.Trim();
        if (string.IsNullOrWhiteSpace(raw) || !Uri.TryCreate(raw, UriKind.Absolute, out var url)
            || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            ModelStatus = "Enter a valid http(s) URL to a .gguf model.";
            return;
        }
        try
        {
            IsModelBusy = true;
            ModelProgress = 0;
            ModelStatus = "Downloading model... this can take a while for multi-GB files.";
            var progress = new Progress<ModelTransferProgress>(p =>
            {
                ModelProgress = p.Percent;
                if (p.TotalBytes is long t)
                    ModelStatus = $"Downloading... {p.BytesTransferred / 1024d / 1024d:0} MB of {t / 1024d / 1024d:0} MB ({p.Percent:0}%)";
                else
                    ModelStatus = $"Downloading... {p.BytesTransferred / 1024d / 1024d:0} MB";
            });
            var model = await _models.DownloadAsync(url, null, progress);
            ModelDownloadUrl = string.Empty;
            RefreshModels();
            ModelStatus = $"Downloaded {model.Name} ({model.SizeDisplay}) and set active.";
        }
        catch (Exception ex)
        {
            ModelStatus = "Download failed: " + ex.Message;
        }
        finally
        {
            IsModelBusy = false;
            ModelProgress = 0;
        }
    }

    [RelayCommand]
    private void DeleteModel()
    {
        if (_models is null || SelectedModel is null) { ModelStatus = "Select a model to delete."; return; }
        try
        {
            var name = SelectedModel.Name;
            _models.Delete(SelectedModel.FilePath);
            RefreshModels();
            ModelStatus = $"Deleted {name}.";
        }
        catch (Exception ex)
        {
            ModelStatus = "Delete failed: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task AskChatAsync()
    {
        if (_chat is null) { ChatOutput = "Chat assistant unavailable."; return; }
        var prompt = ChatPrompt?.Trim();
        if (string.IsNullOrWhiteSpace(prompt)) return;

        try
        {
            IsChatBusy = true;
            ChatOutput = $"You: {prompt}\n\nAssistant: ";
            var context = new ChatContext { DriveStateSummary = BuildDriveStateSummary() };
            await foreach (var token in _chat.AskAsync(prompt, context))
                ChatOutput += token;
            ChatPrompt = string.Empty;
        }
        catch (Exception ex)
        {
            ChatOutput += "\n\n[error: " + ex.Message + "]";
        }
        finally
        {
            IsChatBusy = false;
        }
    }

    [RelayCommand]
    private void ShowDiskSummary()
        => ShowPopup("Disk summary", FormatDiskSummary());

    [RelayCommand]
    private void ShowHealthSummary()
        => ShowPopup("Health summary", FormatHealthSummary());

    [RelayCommand]
    private void ShowFsSummary()
        => ShowPopup("Filesystem summary", FormatFsSummary());

    [RelayCommand]
    private void ShowHistorySummary()
        => ShowPopup("History summary", string.IsNullOrWhiteSpace(TrendSummary) ? "No history yet. Read health to record a snapshot." : TrendSummary);

    [RelayCommand]
    private void ShowEncryptionSummary()
        => ShowPopup("Encryption summary", Security?.Summary ?? EncStatus);

    private string FormatDiskSummary()
    {
        if (SelectedDisk is null) return "No disk selected.";
        var d = SelectedDisk;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(d.FriendlyName);
        sb.AppendLine($"Disk {d.Number}  {d.BusType}  {d.SizeGb:0.00} GB  {d.PartitionStyle}");
        sb.AppendLine($"Serial: {d.SerialNumber ?? "(none)"}");
        sb.AppendLine($"Boot={d.IsBoot}  System={d.IsSystem}");
        sb.AppendLine();
        foreach (var v in d.Volumes)
        {
            sb.AppendLine($"Volume {v.DriveLetter}: {v.Label} ({v.FileSystem})");
            sb.AppendLine($"  {v.UsedGb:0.00} GB used / {v.FreeGb:0.00} GB free / {v.SizeGb:0.00} GB");
        }
        sb.AppendLine();
        sb.Append(FormatFsSummary());
        return sb.ToString();
    }

    private string FormatHealthSummary()
    {
        if (SelectedHealth is null) return "No health data yet. Click Read health.";
        var h = SelectedHealth;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Source: {h.Source}");
        sb.AppendLine($"Status: {h.HealthStatus}   Predict failure: {h.PredictFailure}");
        sb.AppendLine($"Temperature: {h.TemperatureCelsius} C");
        sb.AppendLine($"Life used: {h.PercentageUsed}%   Spare: {h.AvailableSpare}%");
        sb.AppendLine($"Written: {h.TerabytesWritten} TB   Read: {h.TerabytesRead} TB");
        sb.AppendLine($"Power-on: {h.PowerOnHours} h   Cycles: {h.PowerCycles}");
        sb.AppendLine($"Unsafe shutdowns: {h.UnsafeShutdowns}   Media errors: {h.MediaErrors}");
        sb.AppendLine($"Read errors: {h.ReadErrorsTotal}   Write errors: {h.WriteErrorsTotal}");
        if (Assessment is { } a)
        {
            sb.AppendLine();
            sb.AppendLine($"AI assessment: {a.Level}  risk {a.RiskScore}/100");
            sb.AppendLine(a.Summary);
            if (a.Findings.Count > 0)
            {
                sb.AppendLine("Findings:");
                foreach (var f in a.Findings) sb.AppendLine("  - " + f);
            }
            if (a.Recommendations.Count > 0)
            {
                sb.AppendLine("Recommendations:");
                foreach (var r in a.Recommendations) sb.AppendLine("  - " + r);
            }
        }
        return sb.ToString();
    }

    private string FormatFsSummary()
    {
        if (FsAdvice.Count == 0) return "No filesystem advice yet. Select a disk.";
        var sb = new System.Text.StringBuilder();
        foreach (var row in FsAdvice)
        {
            sb.AppendLine($"{row.Header}  current={row.Current ?? "?"}  {row.Verdict}");
            sb.AppendLine("  " + row.Rationale);
        }
        return sb.ToString();
    }

    private string BuildDriveStateSummary()
    {
        if (SelectedDisk is null) return "No disk selected.";
        var d = SelectedDisk;
        var parts = new System.Text.StringBuilder();
        parts.Append($"Disk {d.Number}: {d.FriendlyName}, bus {d.BusType}, {d.SizeBytes / 1024d / 1024d / 1024d:0.0} GB.");
        if (SelectedHealth is { } h)
        {
            if (h.TemperatureCelsius is double t) parts.Append($" Temp {t:0.#}C.");
            if (h.PercentageUsed is int pu) parts.Append($" Wear {pu}% used.");
            if (h.HealthStatus is string hs) parts.Append($" Status {hs}.");
        }
        if (Assessment is { } a) parts.Append($" Risk {a.RiskScore}/100 ({a.Level}).");
        return parts.ToString();
    }
}

/// <summary>A per-volume filesystem recommendation row for display.</summary>
public sealed record FsAdviceRow(char? Letter, string? Current, string Recommended, string Rationale, bool Matches)
{
    public string Header => Letter is null ? "(no letter)" : $"{Letter}:";
    public string Verdict => Matches ? "OK - already optimal" : $"Suggest: {Recommended}";
}
