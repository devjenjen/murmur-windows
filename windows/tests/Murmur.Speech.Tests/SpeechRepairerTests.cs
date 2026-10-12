namespace Murmur.Speech.Tests;

using System;
using System.IO;
using System.Threading.Tasks;
using Murmur.Core.Abstractions;
using Murmur.Speech;
using Shouldly;
using Xunit;

public class SpeechRepairerTests
{
    private readonly RuleBasedSpeechRepairer _repairer = new();

    [Fact]
    public async Task German_Disfluency_Correction_Dienstag_AchNee_Mittwoch()
    {
        var input = "Wir treffen uns ja Dienstag. Ach nee, Mittwoch.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Wir treffen uns Mittwoch.");
    }

    [Fact]
    public async Task German_Disfluency_Correction_MorgenAn_WarteMal_Uebermorgen()
    {
        var input = "Ich rufe dich morgen an, warte mal, übermorgen.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Ich rufe dich übermorgen an.");
    }

    [Fact]
    public async Task German_Disfluency_Correction_10Uhr_AchNein_11Uhr()
    {
        var input = "Das Meeting ist um 10 Uhr, ach nein, 11 Uhr.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Das Meeting ist um 11 Uhr.");
    }

    [Fact]
    public async Task German_NichtX_SondernY()
    {
        var input = "Wir treffen uns nicht am Dienstag, sondern am Mittwoch.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Wir treffen uns am Mittwoch.");
    }

    [Fact]
    public async Task German_IchMeinte()
    {
        var input = "Das Meeting ist um 10 Uhr, ich meinte 11 Uhr.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Das Meeting ist um 11 Uhr.");
    }

    [Fact]
    public async Task German_OderVielmehr()
    {
        var input = "Wir treffen uns morgen, oder vielmehr übermorgen.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Wir treffen uns übermorgen.");
    }

    [Fact]
    public async Task German_WarteKurz()
    {
        var input = "Ich komme um 14 Uhr, warte kurz, 15 Uhr.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Ich komme um 15 Uhr.");
    }

    [Fact]
    public async Task English_NoWait()
    {
        var input = "Let's meet on Tuesday, no wait, Wednesday.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Let's meet on Wednesday.");
    }

    [Fact]
    public async Task English_ScratchThat()
    {
        var input = "Send this to Alice, scratch that, Bob.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Send this to Bob.");
    }

    [Fact]
    public async Task English_OrRather()
    {
        var input = "Meeting is at 2 PM, or rather, 3 PM.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Meeting is at 3 PM.");
    }

    [Fact]
    public async Task English_IMean()
    {
        var input = "Call Jack, I mean, Jill.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Call Jill.");
    }

    [Fact]
    public async Task Null_And_Empty_Returns_Input_Cleanly()
    {
        var nullResult = await _repairer.RepairAsync(null!);
        nullResult.ShouldBeNull();

        var emptyResult = await _repairer.RepairAsync(string.Empty);
        emptyResult.ShouldBe(string.Empty);

        var whitespaceResult = await _repairer.RepairAsync("   ");
        whitespaceResult.ShouldBe("   ");
    }

    [Fact]
    public async Task String_Without_Correction_Markers_Returns_Unchanged()
    {
        var input = "Das Meeting findet wie gewohnt im großen Konferenzraum statt.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe(input);
    }

    [Fact]
    public async Task LlamaSharp_FallsBack_WhenModelFileDoesNotExist()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gguf");
        using var llamaRepairer = new LlamaSharpSpeechRepairer(nonExistentPath);

        var input = "Wir treffen uns ja Dienstag. Ach nee, Mittwoch.";
        var result = await llamaRepairer.RepairAsync(input);

        // Gracefully falls back to rule-based repair
        result.ShouldBe("Wir treffen uns Mittwoch.");
    }

    [Fact]
    public async Task LlamaSharp_FallsBack_WhenModelPathIsNull()
    {
        using var llamaRepairer = new LlamaSharpSpeechRepairer(null);

        var input = "Das Meeting ist um 10 Uhr, ach nein, 11 Uhr.";
        var result = await llamaRepairer.RepairAsync(input);

        result.ShouldBe("Das Meeting ist um 11 Uhr.");
    }

    [Fact]
    public async Task LlamaSharp_Disposal_IsIdempotent()
    {
        var llamaRepairer = new LlamaSharpSpeechRepairer(null);
        llamaRepairer.Dispose();
        llamaRepairer.Dispose();

        // Should not throw after disposal, fallback handles invocation
        var result = await llamaRepairer.RepairAsync("Hello world");
        result.ShouldBe("Hello world");
    }
}
