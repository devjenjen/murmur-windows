
using Murmur.Core.Abstractions;
using Shouldly;
using Xunit;

namespace Murmur.Speech.Tests;

public class SpeechRepairBudgetTests
{
    private static async IAsyncEnumerable<string> Chunks(IEnumerable<string> chunks, Action? after = null)
    {
        await Task.CompletedTask;
        foreach (var chunk in chunks) yield return chunk;
        after?.Invoke();
    }

    [Theory]
    [InlineData(1568, true)]
    [InlineData(1569, false)]
    [InlineData(2048, false)]
    [InlineData(int.MaxValue, false)]
    public async Task ExactFormattedPromptAndEntireReserveMustFit(int promptTokens, bool fits)
    {
        const string raw = "  Original ohne Veraenderung  ";
        var prompts = new List<string>();
        var calls = 0;
        using var repairer = new LlamaSharpSpeechRepairer(text =>
        {
            prompts.Add(text);
            return text == raw ? 200 : promptTokens;
        }, (prompt, maximum, ct) =>
        {
            calls++;
            maximum.ShouldBe(464);
            prompt.ShouldStartWith("<|im_start|>system\n");
            prompt.ShouldEndWith("<|im_start|>assistant\n");
            prompt.ShouldContain(raw);
            return Chunks(["kurz"]);
        });
        (await repairer.RepairAsync(raw)).ShouldBe(fits ? "kurz" : raw);
        calls.ShouldBe(fits ? 1 : 0);
        prompts.Count.ShouldBe(2);
    }

    [Fact]
    public async Task LongOutputBeyondOld256LimitIsAcceptedBeforeDynamicBudget()
    {
        using var repairer = new LlamaSharpSpeechRepairer(_ => 200,
            (_, maximum, _) => Chunks(Enumerable.Repeat("wort ", 300)));
        (await repairer.RepairAsync("Original")).ShouldBe(string.Join(' ', Enumerable.Repeat("wort", 300)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FullBudgetIncludingEmptyUtf8YieldsKeepsExactOriginal(bool empty)
    {
        const string raw = " Original mit Rest ";
        using var repairer = new LlamaSharpSpeechRepairer(_ => 200,
            (_, maximum, _) => Chunks(Enumerable.Repeat(empty ? "" : "Teil", maximum)));
        (await repairer.RepairAsync(raw)).ShouldBe(raw);
    }

    [Theory]
    [InlineData("Mittwoch")]
    [InlineData("am Mittwoch um")]
    public async Task LegitimateStrongCorrectionAndFragmentNeedNoLengthOrPunctuationHeuristic(string corrected)
    {
        using var repairer = new LlamaSharpSpeechRepairer(_ => 200, (_, _, _) => Chunks([corrected]));
        (await repairer.RepairAsync(new string('x', 500))).ShouldBe(corrected);
    }

    [Fact]
    public async Task InferenceFailureOrEmptyOutputKeepsOriginal()
    {
        foreach (var fails in new[] { true, false })
        {
            using var repairer = new LlamaSharpSpeechRepairer(_ => 200,
                (_, _, _) => fails ? throw new InvalidOperationException("decoder failed") : Chunks([" "]));
            (await repairer.RepairAsync(" original ")).ShouldBe(" original ");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("lang")]
    public async Task CancellationWinsOverEarlyReturnsAndMissingModelFallback(string raw)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var repairer = new LlamaSharpSpeechRepairer(null);
        await Should.ThrowAsync<OperationCanceledException>(() => repairer.RepairAsync(raw, cancellation.Token));
    }

    [Fact]
    public async Task SilentExecutorCancellationAfterEnumerationCannotReturnPartial()
    {
        using var cancellation = new CancellationTokenSource();
        using var repairer = new LlamaSharpSpeechRepairer(_ => 200,
            (_, _, _) => Chunks(["Teil"], cancellation.Cancel));
        await Should.ThrowAsync<OperationCanceledException>(() => repairer.RepairAsync("Original", cancellation.Token));
    }

    [Fact]
    public async Task CancellationDuringTokenizationWinsOverContextFallback()
    {
        using var cancellation = new CancellationTokenSource();
        using var repairer = new LlamaSharpSpeechRepairer(_ => { cancellation.Cancel(); return int.MaxValue; },
            (_, _, _) => throw new InvalidOperationException("Must not infer"));
        await Should.ThrowAsync<OperationCanceledException>(() => repairer.RepairAsync("Original", cancellation.Token));
    }
}
