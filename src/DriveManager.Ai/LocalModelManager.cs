using System.Net.Http;

namespace DriveManager.Ai;

/// <summary>
/// File-system backed <see cref="IModelManager"/>. Stores GGUF models in a portable
/// "models" folder beside the executable and remembers the active one in "models/active.txt".
/// </summary>
public sealed class LocalModelManager : IModelManager
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private readonly string _dir;
    private readonly string _activeFile;

    public LocalModelManager(string? modelsDirectory = null)
    {
        _dir = modelsDirectory ?? Path.Combine(AppContext.BaseDirectory, "models");
        Directory.CreateDirectory(_dir);
        _activeFile = Path.Combine(_dir, "active.txt");
    }

    public string ModelsDirectory => _dir;

    public AiModelInfo? ActiveModel
    {
        get
        {
            var active = ReadActiveName();
            var models = ListModels();
            return models.FirstOrDefault(m => m.IsActive)
                   ?? (active is null ? null : models.FirstOrDefault(m => string.Equals(m.Name, active, StringComparison.OrdinalIgnoreCase)));
        }
    }

    public IReadOnlyList<AiModelInfo> ListModels()
    {
        if (!Directory.Exists(_dir)) return [];
        var active = ReadActiveName();
        var list = new List<AiModelInfo>();
        foreach (var path in Directory.EnumerateFiles(_dir, "*.gguf", SearchOption.TopDirectoryOnly))
        {
            var fi = new FileInfo(path);
            var isActive = active is not null && string.Equals(fi.Name, active, StringComparison.OrdinalIgnoreCase);
            list.Add(new AiModelInfo(fi.Name, fi.FullName, fi.Length, isActive));
        }
        // If nothing is explicitly active, treat the only/largest model as active for convenience.
        if (list.Count > 0 && list.All(m => !m.IsActive))
        {
            var pick = list.OrderByDescending(m => m.SizeBytes).First();
            for (var i = 0; i < list.Count; i++)
                if (list[i].FilePath == pick.FilePath)
                    list[i] = pick with { IsActive = true };
        }
        return list.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void SetActive(string filePath)
    {
        var name = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(name)) return;
        File.WriteAllText(_activeFile, name);
    }

    public async Task<AiModelInfo> ImportAsync(
        string sourceFilePath,
        IProgress<ModelTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException("Source model file not found.", sourceFilePath);
        if (!sourceFilePath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only .gguf model files are supported.");

        var name = Path.GetFileName(sourceFilePath);
        var dest = UniqueDestination(name);
        var total = new FileInfo(sourceFilePath).Length;

        await using (var src = File.OpenRead(sourceFilePath))
        await using (var dst = File.Create(dest))
        {
            await CopyWithProgressAsync(src, dst, total, progress, ct).ConfigureAwait(false);
        }

        return Finalize(dest);
    }

    public async Task<AiModelInfo> DownloadAsync(
        Uri url,
        string? fileName = null,
        IProgress<ModelTransferProgress>? progress = null,
        CancellationToken ct = default)
    {
        var name = fileName
                   ?? Path.GetFileName(url.LocalPath);
        if (string.IsNullOrWhiteSpace(name) || !name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            name = string.IsNullOrWhiteSpace(name) ? "model.gguf" : name + ".gguf";

        var dest = UniqueDestination(name);
        var tmp = dest + ".part";

        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength;

        await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var dst = File.Create(tmp))
        {
            await CopyWithProgressAsync(src, dst, total, progress, ct).ConfigureAwait(false);
        }

        if (File.Exists(dest)) File.Delete(dest);
        File.Move(tmp, dest);
        return Finalize(dest);
    }

    public void Delete(string filePath)
    {
        if (File.Exists(filePath)) File.Delete(filePath);
        if (string.Equals(Path.GetFileName(filePath), ReadActiveName(), StringComparison.OrdinalIgnoreCase)
            && File.Exists(_activeFile))
            File.Delete(_activeFile);
    }

    private AiModelInfo Finalize(string dest)
    {
        // Auto-activate the first model so "download/import" is immediately usable.
        if (ReadActiveName() is null)
            SetActive(dest);
        var fi = new FileInfo(dest);
        return new AiModelInfo(fi.Name, fi.FullName, fi.Length, true);
    }

    private string UniqueDestination(string name)
    {
        var dest = Path.Combine(_dir, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        var i = 1;
        while (File.Exists(dest))
            dest = Path.Combine(_dir, $"{stem} ({i++}){ext}");
        return dest;
    }

    private string? ReadActiveName()
    {
        try
        {
            if (!File.Exists(_activeFile)) return null;
            var n = File.ReadAllText(_activeFile).Trim();
            return string.IsNullOrWhiteSpace(n) ? null : n;
        }
        catch { return null; }
    }

    private static async Task CopyWithProgressAsync(
        Stream src, Stream dst, long? total,
        IProgress<ModelTransferProgress>? progress, CancellationToken ct)
    {
        var buffer = new byte[1024 * 1024];
        long done = 0;
        int read;
        var lastReport = DateTime.MinValue;
        while ((read = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            var now = DateTime.UtcNow;
            if (progress is not null && (now - lastReport).TotalMilliseconds > 150)
            {
                progress.Report(new ModelTransferProgress(done, total));
                lastReport = now;
            }
        }
        progress?.Report(new ModelTransferProgress(done, total ?? done));
    }
}
