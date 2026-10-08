namespace DriveManager.Ai;

public sealed record ChatContext
{
    /// <summary>A compact, human-readable snapshot of the current drive/health state the model can reference.</summary>
    public string? DriveStateSummary { get; init; }
}

/// <summary>
/// A local, offline chat assistant. The production implementation will load a small quantized
/// GGUF model via LLamaSharp; <see cref="PlaceholderChatAssistant"/> is used until a model is present.
/// </summary>
public interface IChatAssistant
{
    bool IsModelLoaded { get; }

    IAsyncEnumerable<string> AskAsync(string prompt, ChatContext context, CancellationToken ct = default);
}
