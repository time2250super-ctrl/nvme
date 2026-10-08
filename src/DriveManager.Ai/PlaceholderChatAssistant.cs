using System.Runtime.CompilerServices;

namespace DriveManager.Ai;

/// <summary>
/// A no-model fallback so the UI is fully functional before a GGUF model is downloaded.
/// It answers using the supplied drive-state summary instead of a language model.
/// </summary>
public sealed class PlaceholderChatAssistant : IChatAssistant
{
    public bool IsModelLoaded => false;

    public async IAsyncEnumerable<string> AskAsync(
        string prompt,
        ChatContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var reply =
            "The local AI model is not loaded yet, so I'm answering from the current telemetry only.\n\n" +
            (string.IsNullOrWhiteSpace(context.DriveStateSummary)
                ? "No drive is selected."
                : context.DriveStateSummary) +
            "\n\nOnce a GGUF model is placed in the models folder, I'll answer questions like \"" + prompt + "\" conversationally.";

        foreach (var word in reply.Split(' '))
        {
            ct.ThrowIfCancellationRequested();
            yield return word + " ";
            await Task.Delay(8, ct);
        }
    }
}
