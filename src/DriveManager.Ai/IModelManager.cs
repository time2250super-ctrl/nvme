namespace DriveManager.Ai;

/// <summary>A GGUF model file that lives in the portable models folder next to the app.</summary>
public sealed record AiModelInfo(string Name, string FilePath, long SizeBytes, bool IsActive)
{
    public double SizeGb => SizeBytes / 1024d / 1024d / 1024d;
    public string SizeDisplay => SizeBytes >= 1L << 30
        ? $"{SizeGb:0.0} GB"
        : $"{SizeBytes / 1024d / 1024d:0.0} MB";
}

/// <summary>Progress for a long-running import/download, in a UI-friendly shape.</summary>
public sealed record ModelTransferProgress(long BytesTransferred, long? TotalBytes)
{
    public double? Fraction => TotalBytes is > 0 ? (double)BytesTransferred / TotalBytes.Value : null;
    public double Percent => (Fraction ?? 0) * 100d;
}

/// <summary>
/// Manages local GGUF models: lists what is present, imports a local file, downloads from a URL,
/// and tracks which model is "active" (the one the chat assistant loads). Fully offline-capable:
/// models are stored in a portable folder beside the executable so the app can run from its own
/// NVMe partition with no external dependencies.
/// </summary>
public interface IModelManager
{
    /// <summary>Absolute path of the portable models directory (created on demand).</summary>
    string ModelsDirectory { get; }

    /// <summary>The currently selected model, or null if none is present/selected.</summary>
    AiModelInfo? ActiveModel { get; }

    /// <summary>Enumerate all *.gguf models currently in the models folder.</summary>
    IReadOnlyList<AiModelInfo> ListModels();

    /// <summary>Mark an existing model file as active (persisted across restarts).</summary>
    void SetActive(string filePath);

    /// <summary>Copy a local .gguf file into the models folder.</summary>
    Task<AiModelInfo> ImportAsync(
        string sourceFilePath,
        IProgress<ModelTransferProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Download a .gguf model from a URL into the models folder.</summary>
    Task<AiModelInfo> DownloadAsync(
        Uri url,
        string? fileName = null,
        IProgress<ModelTransferProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Delete a model file from the folder.</summary>
    void Delete(string filePath);
}
