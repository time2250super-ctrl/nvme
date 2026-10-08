using System.Runtime.CompilerServices;
using LLama;
using LLama.Common;

namespace DriveManager.Ai;

/// <summary>
/// Offline chat assistant backed by a local GGUF model via LLamaSharp (CPU backend).
/// The model is loaded lazily on first use and reloaded when the active model changes,
/// so the UI stays responsive and the app has no external/online dependency at startup.
/// </summary>
public sealed class LlamaChatAssistant : IChatAssistant, IDisposable
{
    private const string SystemPrompt =
        "You are the built-in assistant for a Windows NVMe drive-management app. " +
        "Answer concisely and accurately about drive health, SMART data, storage, partitions, " +
        "encryption and the app itself. If drive state is provided, use it.";

    private readonly IModelManager _models;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _loadedPath;
    private LLamaWeights? _weights;
    private StatelessExecutor? _executor;

    public LlamaChatAssistant(IModelManager models) => _models = models;

    public bool IsModelLoaded => _models.ActiveModel is not null;

    public async IAsyncEnumerable<string> AskAsync(
        string prompt,
        ChatContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = _models.ActiveModel;
        if (model is null)
        {
            yield return "No AI model is loaded yet. Open the AI tab to download or import a .gguf model, then set it active.";
            yield break;
        }

        StatelessExecutor? executor = null;
        string? loadError = null;
        try
        {
            executor = await EnsureLoadedAsync(model.FilePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            loadError = ex.Message;
        }

        if (executor is null)
        {
            yield return $"Failed to load model '{model.Name}': {loadError}";
            yield break;
        }

        var full = BuildPrompt(prompt, context);
        var inference = new InferenceParams
        {
            MaxTokens = 512,
            AntiPrompts = new List<string> { "<|user|>", "User:", "\n\n\n" },
        };

        await foreach (var token in executor.InferAsync(full, inference, ct).ConfigureAwait(false))
            yield return token;
    }

    private static string BuildPrompt(string prompt, ChatContext context)
    {
        var state = string.IsNullOrWhiteSpace(context.DriveStateSummary)
            ? string.Empty
            : $"\nCurrent drive state:\n{context.DriveStateSummary}\n";
        return $"<|system|>\n{SystemPrompt}{state}\n<|user|>\n{prompt}\n<|assistant|>\n";
    }

    private async Task<StatelessExecutor> EnsureLoadedAsync(string path, CancellationToken ct)
    {
        if (_loadedPath == path && _executor is not null)
            return _executor;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_loadedPath == path && _executor is not null)
                return _executor;

            Unload();

            var parameters = new ModelParams(path)
            {
                ContextSize = 4096,
                GpuLayerCount = 0, // CPU backend; runs anywhere with no GPU dependency.
            };

            var weights = await LLamaWeights.LoadFromFileAsync(parameters, ct).ConfigureAwait(false);
            var executor = new StatelessExecutor(weights, parameters);

            _weights = weights;
            _executor = executor;
            _loadedPath = path;
            return executor;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Unload()
    {
        _executor = null;
        _weights?.Dispose();
        _weights = null;
        _loadedPath = null;
    }

    public void Dispose()
    {
        Unload();
        _gate.Dispose();
    }
}
