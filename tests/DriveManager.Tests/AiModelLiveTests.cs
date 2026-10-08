using System.IO;
using System.Text;
using DriveManager.Ai;
using Xunit;

namespace DriveManager.Tests;

/// <summary>
/// Live, non-simulated tests for the AI model pipeline: a real GGUF model is downloaded
/// over the network, activated, and run through LLamaSharp to produce real tokens.
/// Uses a tiny (~1 MB) real llama model so the test stays fast while exercising the full path.
/// </summary>
public sealed class AiModelLiveTests
{
    private const string TinyModelUrl =
        "https://huggingface.co/ggml-org/models/resolve/main/tinyllamas/stories260K.gguf";

    [Fact]
    public async Task Import_lists_and_activates_model_live()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dm-ai-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Create a real file on disk and import it through the manager.
            var src = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gguf");
            await File.WriteAllBytesAsync(src, new byte[64 * 1024]);

            var mgr = new LocalModelManager(dir);
            Assert.Empty(mgr.ListModels());

            var imported = await mgr.ImportAsync(src);
            Assert.True(File.Exists(imported.FilePath));
            Assert.Single(mgr.ListModels());
            Assert.NotNull(mgr.ActiveModel);
            Assert.Equal(imported.Name, mgr.ActiveModel!.Name);

            File.Delete(src);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public async Task Download_activate_and_infer_live()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dm-ai-infer-" + Guid.NewGuid().ToString("N"));
        try
        {
            var mgr = new LocalModelManager(dir);

            // Real network download of a real GGUF model.
            var model = await mgr.DownloadAsync(new Uri(TinyModelUrl));
            Assert.True(File.Exists(model.FilePath));
            Assert.True(model.SizeBytes > 0);
            Assert.NotNull(mgr.ActiveModel);

            // Real LLamaSharp inference (CPU backend) producing actual tokens.
            using var chat = new LlamaChatAssistant(mgr);
            Assert.True(chat.IsModelLoaded);

            var sb = new StringBuilder();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await foreach (var token in chat.AskAsync("Once upon a time", new ChatContext(), cts.Token))
            {
                sb.Append(token);
                if (sb.Length > 32) break;
            }

            Assert.True(sb.Length > 0, "Model produced no output tokens.");
        }
        finally
        {
            TryDelete(dir);
        }
    }

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch { /* best effort */ }
    }
}
