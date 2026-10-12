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

    /// <inheritdoc />
    public async Task<string> RepairAsync(string rawTranscript, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(rawTranscript))
        {
            return rawTranscript;
        }

        if (string.IsNullOrWhiteSpace(rawTranscript))
        {
            return rawTranscript;
        }

        if (_isDisposed || !EnsureLoaded() || _executor == null)
        {
            return await _fallback.RepairAsync(rawTranscript, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var prompt = FormatChatPrompt(DefaultSystemPrompt, rawTranscript);

            var inferenceParams = new InferenceParams
            {
                MaxTokens = 256,
                AntiPrompts = new[] { "<|im_end|>", "<|im_start|>", "\n<|im_start|>" },
                SamplingPipeline = new DefaultSamplingPipeline
                {
                    Temperature = 0.0f
                }
            };

            var sb = new StringBuilder();
            await foreach (var token in _executor.InferAsync(prompt, inferenceParams, cancellationToken).ConfigureAwait(false))
            {
                sb.Append(token);
            }

            var result = CleanOutput(sb.ToString());
            if (string.IsNullOrWhiteSpace(result))
            {
                return await _fallback.RepairAsync(rawTranscript, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return await _fallback.RepairAsync(rawTranscript, cancellationToken).ConfigureAwait(false);
        }
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
                    ContextSize = 1024,
                    GpuLayerCount = 0
                };

                _weights = LLamaWeights.LoadFromFile(parameters);
                _executor = new StatelessExecutor(_weights, parameters);
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
        if (_isDisposed)
        {
            return;
        }

        lock (_lock)
        {
            _isDisposed = true;
            _executor = null;
            _weights?.Dispose();
            _weights = null;
        }
    }
}
