using Shouldly;
using System.Buffers;
using Xunit;

namespace Murmur.Speech.Tests;

public class StreamingAudioBoundsTests
{
    private sealed class UnreadableMemory(int length) : MemoryManager<float>
    {
        public Memory<float> Oversized => CreateMemory(length);
        public override Span<float> GetSpan() => throw new InvalidOperationException("must check length before copy");
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();
        public override void Unpin() { }
        protected override void Dispose(bool disposing) { }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutstandingBudgetIncludesBlockedNativeItemAndRejectsNextChunk(bool tinyChunks)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed = 0;
        var first = true;
        using var transcriber = new NemotronStreamingTranscriber(() => new(
            _ => { if (first) { first = false; entered.SetResult(); release.Wait(); } },
            () => false, () => { }, () => "", () => { }, () => disposed++), () => { });
        try
        {
            transcriber.BeginSession(ex => failed.TrySetResult(ex));
            transcriber.AcceptAudio(new float[1]);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (tinyChunks)
                for (var i = 1; i < 1024; i++) transcriber.AcceptAudio(new float[1]);
            else transcriber.AcceptAudio(new float[143999]);
            transcriber.PendingAudio.ShouldBe(tinyChunks ? (1024, 1024) : (144000, 2));
            failed.Task.IsCompleted.ShouldBeFalse();
            transcriber.AcceptAudio(new float[1]);
            (await failed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Message.ShouldBe("StreamingBacklogExceeded");
            transcriber.PendingAudio.ShouldBe(tinyChunks ? (1024, 1024) : (144000, 2));
            var cleanup = transcriber.CancelSessionAsync();
            cleanup.IsCompleted.ShouldBeFalse();
            disposed.ShouldBe(0);
            release.Set();
            await Should.ThrowAsync<InvalidOperationException>(() => cleanup);
            disposed.ShouldBe(1);
            transcriber.PendingAudio.ShouldBe((0, 0));
            transcriber.BeginSession();
            transcriber.AcceptAudio(new float[7]);
            (await transcriber.CompleteSessionAsync()).ShouldBe("");
            transcriber.PendingAudio.ShouldBe((0, 0));
        }
        finally { release.Set(); }
    }

    [Theory]
    [InlineData(144001)]
    [InlineData(int.MaxValue)]
    public async Task OversizedSingleChunkFailsBeforeEnqueueAndDoesNotReturnPartialSuccess(int length)
    {
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepts = 0;
        using var transcriber = new NemotronStreamingTranscriber(() => new(
            _ => accepts++, () => false, () => { }, () => "incomplete", () => { }, () => { }), () => { });
        transcriber.BeginSession(ex => failed.TrySetResult(ex));
        using var samples = new UnreadableMemory(length);
        transcriber.AcceptAudio(samples.Oversized);
        (await failed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Message.ShouldBe("StreamingBacklogExceeded");
        transcriber.PendingAudio.ShouldBe((0, 0));
        await Should.ThrowAsync<InvalidOperationException>(() => transcriber.CompleteSessionAsync());
        accepts.ShouldBe(0);
    }

    [Fact]
    public async Task WorkerFailureSignalsImmediatelyWithoutCompleteAndNextSessionIsClean()
    {
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var throwOnce = true;
        var accepts = 0;
        using var transcriber = new NemotronStreamingTranscriber(() => new(
            _ => { accepts++; if (throwOnce) { throwOnce = false; throw new IOException("native failure"); } },
            () => false, () => { }, () => "next", () => { }, () => { }), () => { });
        transcriber.BeginSession(ex => failed.TrySetResult(ex));
        transcriber.AcceptAudio(new float[3]);
        (await failed.Task.WaitAsync(TimeSpan.FromSeconds(5))).Message.ShouldBe("StreamingRecognitionFailed");
        await Should.ThrowAsync<InvalidOperationException>(() => transcriber.CancelSessionAsync());
        transcriber.BeginSession();
        transcriber.AcceptAudio(new float[1]);
        (await transcriber.CompleteSessionAsync()).ShouldBe("next");
        accepts.ShouldBe(2);
    }
}
