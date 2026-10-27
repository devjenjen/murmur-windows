namespace Murmur.Speech;

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Murmur.Core.Abstractions;

/// <summary>
/// Speech repair engine powered by LLamaSharp running a quantized local LLM (e.g. Qwen2.5)
/// with graceful fallback to deterministic rule-based repair.
/// </summary>
public sealed class LlamaSharpSpeechRepairer : ISpeechRepairer, IDisposable
{
    /// <summary>
    /// Default system prompt instructing the language model to edit spoken dictations.
    /// </summary>
    public const string DefaultSystemPrompt =
        "Du bist ein präziser Editor für gesprochene Diktate. Deine Aufgabe ist es, Versprecher, Selbstkorrekturen (z. B. 'ach ja...', 'ach nee...', 'oder besser...', 'ich meinte...') und Füllwörter zu entfernen. Behalte den Satzbau und den restlichen Wortlaut exakt bei. Gib ausschließlich das korrigierte Diktat aus, ohne Einleitung oder Anmerkungen.";

    private readonly string? _modelPath;
    private readonly ISpeechRepairer _fallback;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _inferenceGate = new(1, 1);
    private readonly Func<string, int>? _tokenize;
    private readonly Func<string, int, CancellationToken, IAsyncEnumerable<string>>? _infer;
    private const int ContextTokens = 2048;
    private const int ContextMargin = 16;

    private LLamaWeights? _weights;
    private StatelessExecutor? _executor;
    private bool _initAttempted;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of <see cref="LlamaSharpSpeechRepairer"/>.
    /// </summary>
    /// <param name="modelPath">Path to GGUF model file or directory containing GGUF model.</param>
    /// <param name="fallback">Fallback speech repairer if model is missing or fails.</param>
    public LlamaSharpSpeechRepairer(string? modelPath = null, ISpeechRepairer? fallback = null)
    {
        _modelPath = ResolveModelPath(modelPath);
        _fallback = fallback ?? new RuleBasedSpeechRepairer();
    }

    internal LlamaSharpSpeechRepairer(Func<string, int> tokenize,
        Func<string, int, CancellationToken, IAsyncEnumerable<string>> infer)
        : this()
    {
        _tokenize = tokenize;
        _infer = infer;
    }

    private static string CheckedResult(string result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    /// <inheritdoc />
    public async Task<string> RepairAsync(string rawTranscript, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(rawTranscript)) return CheckedResult(rawTranscript, cancellationToken);
        await _inferenceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isDisposed || (_infer == null && (!EnsureLoaded() || _executor == null)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fallback = await _fallback.RepairAsync(rawTranscript, cancellationToken).ConfigureAwait(false);
                return CheckedResult(fallback, cancellationToken);
            }

            try
            {
                var prompt = FormatChatPrompt(DefaultSystemPrompt, rawTranscript);
                // Match StatelessExecutor 0.22 exactly: our ChatML is already formatted, BOS/special enabled.
                int Count(string text) => _tokenize != null ? _tokenize(text)
                    : _weights!.Tokenize(text, true, true, Encoding.UTF8).Length;
                var promptTokens = Count(prompt);
                var rawTokens = Count(rawTranscript);
                var reserve = Math.Max(256L, 2L * rawTokens + 64);
                var context = _executor?.Context.ContextSize ?? ContextTokens;
                if (promptTokens < 0 || rawTokens < 0 || promptTokens + reserve + ContextMargin > context)
                    return CheckedResult(rawTranscript, cancellationToken);
                var maximum = (int)reserve;
                cancellationToken.ThrowIfCancellationRequested();
                var chunks = _infer != null ? _infer(prompt, maximum, cancellationToken)
                    : _executor!.InferAsync(prompt, new InferenceParams
                    {
                        MaxTokens = maximum,
                        AntiPrompts = ["<|im_end|>", "<|im_start|>", "\n<|im_start|>"],
                        SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.0f }
                    }, cancellationToken);
                var output = new StringBuilder();
                var yielded = 0;
                await foreach (var chunk in chunks.ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Only the pinned StatelessExecutor yields once per non-EOS token, including empty UTF8 chunks.
                    if (++yielded >= maximum) return CheckedResult(rawTranscript, cancellationToken);
                    output.Append(chunk);
                }
                cancellationToken.ThrowIfCancellationRequested();
                var result = CleanOutput(output.ToString());
                return CheckedResult(string.IsNullOrWhiteSpace(result) ? rawTranscript : result, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return CheckedResult(rawTranscript, cancellationToken); }
        }
        finally { _inferenceGate.Release(); }
    }

    private bool EnsureLoaded()
    {
        if (_initAttempted)
        {
            return _executor != null;
        }

        lock (_lock)
        {
            if (_initAttempted)
            {
                return _executor != null;
            }

            _initAttempted = true;

            if (string.IsNullOrWhiteSpace(_modelPath) || !File.Exists(_modelPath))
            {
                return false;
            }

            try
            {
                var parameters = new ModelParams(_modelPath)
                {
                    ContextSize = ContextTokens,
                    GpuLayerCount = 0
                };

                _weights = LLamaWeights.LoadFromFile(parameters);
                _executor = new StatelessExecutor(_weights, parameters) { ApplyTemplate = false };
                return true;
            }
            catch (Exception)
            {
                _weights?.Dispose();
                _weights = null;
                _executor = null;
                return false;
            }
        }
    }

    private static string? ResolveModelPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (File.Exists(path))
        {
            return path;
        }

        if (Directory.Exists(path))
        {
            var files = Directory.GetFiles(path, "*.gguf");
            if (files.Length > 0)
            {
                return files[0];
            }
        }

        return path;
    }

    private static string FormatChatPrompt(string systemPrompt, string rawTranscript)
    {
        return $"<|im_start|>system\n{systemPrompt}<|im_end|>\n<|im_start|>user\n{rawTranscript}<|im_end|>\n<|im_start|>assistant\n";
    }

    private static readonly char[] QuotesToTrim = ['"', '„', '“', '”', '«', '»'];

    internal static string CleanOutput(string output)
    {
        string cleaned = output.Replace("<|im_end|>", "").Replace("<|im_start|>", "").Trim();
        cleaned = cleaned.Trim(QuotesToTrim).Trim();
        return cleaned;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _inferenceGate.Wait();
        try
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                _executor = null;
                _weights?.Dispose();
                _weights = null;
            }
        }
        finally { _inferenceGate.Release(); }
    }
}
