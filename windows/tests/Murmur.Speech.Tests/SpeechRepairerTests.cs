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
    public async Task English_Idiom_IMeanIt_Returns_Untouched()
    {
        var input = "I love you, I mean it.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("I love you, I mean it.");
    }

    [Fact]
    public async Task German_Contrast_NichtTechnikSondernOrganisation_Returns_Untouched()
    {
        var input = "Das Problem ist nicht die Technik, sondern die Organisation.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Das Problem ist nicht die Technik, sondern die Organisation.");
    }

    [Fact]
    public async Task German_Contrast_NichtMuedeSondernHungrig_Returns_Untouched()
    {
        var input = "Ich bin nicht müde, sondern hungrig.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Ich bin nicht müde, sondern hungrig.");
    }

    [Fact]
    public async Task German_Verb_Warten_IchWarteAufDich_Returns_Untouched()
    {
        var input = "Ich warte auf dich.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Ich warte auf dich.");
    }

    [Fact]
    public async Task German_Verb_Warten_BitteWarteEinenMomentHier_Returns_Untouched()
    {
        var input = "Bitte warte einen Moment hier.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Bitte warte einen Moment hier.");
    }

    [Fact]
    public async Task German_Verb_Warten_BitteWarteHier_Returns_Untouched()
    {
        var input = "Bitte warte hier.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Bitte warte hier.");
    }

    [Fact]
    public async Task German_Preposition_AmDienstag_AchNee_AmMittwoch_NoDuplicatePreposition()
    {
        var input = "Wir treffen uns am Dienstag, ach nee, am Mittwoch.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Wir treffen uns am Mittwoch.");
    }

    [Fact]
    public async Task German_WarteEinenMoment_Disfluency_Correction()
    {
        var input = "Ich komme um 14 Uhr, warte einen Moment, 15 Uhr.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Ich komme um 15 Uhr.");
    }

    [Fact]
    public async Task German_Preposition_Preserved_When_Alteration_Has_No_Preposition()
    {
        var input = "Wir treffen uns am Dienstag, ach nee, Mittwoch.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Wir treffen uns am Mittwoch.");
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

    [Fact]
    public async Task German_NichtNurSondernAuch_Returns_Untouched()
    {
        var input = "Das System ist nicht nur schnell, sondern auch sicher.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Das System ist nicht nur schnell, sondern auch sicher.");
    }

    [Fact]
    public async Task German_NichtBloßSondernAuch_Returns_Untouched()
    {
        var input = "Das System ist nicht bloß schnell, sondern auch sicher.";
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe("Das System ist nicht bloß schnell, sondern auch sicher.");
    }

    [Theory]
    [InlineData("\"Das ist ein Test.\"", "Das ist ein Test.")]
    [InlineData("„Das ist ein Test.“", "Das ist ein Test.")]
    [InlineData("“Das ist ein Test.”", "Das ist ein Test.")]
    [InlineData("«Das ist ein Test.»", "Das ist ein Test.")]
    [InlineData("„Das ist ein Test\"", "Das ist ein Test")]
    public void LlamaSharp_CleanOutput_Trims_Typographic_Quotes(string input, string expected)
    {
        var result = LlamaSharpSpeechRepairer.CleanOutput(input);
        result.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Ich brauche drei rote Ordner, ach nee, vier.", "Ich brauche vier rote Ordner.")]
    [InlineData("Wir treffen uns am Dienstag im Büro, ach nee, Mittwoch.", "Wir treffen uns am Mittwoch im Büro.")]
    [InlineData("Ich rufe dich übermorgen an, ach nee, morgen.", "Ich rufe dich morgen an.")]
    [InlineData("Ich rufe dich morgen an, warte mal, übermorgen.", "Ich rufe dich übermorgen an.")]
    [InlineData("Ich habe alles eingepackt. Ach ja, die Schlüssel fehlen noch.", "Ich habe alles eingepackt. Ach ja, die Schlüssel fehlen noch.")]
    [InlineData("Ach ja, bitte bring die Unterlagen mit.", "Ach ja, bitte bring die Unterlagen mit.")]
    [InlineData("Wir treffen uns am Dienstag. Ich bringe Kaffee. Ach nee, Mittwoch.", "Wir treffen uns am Dienstag. Ich bringe Kaffee. Ach nee, Mittwoch.")]
    [InlineData("Nicht öffnen. Erst prüfen, sondern warten.", "Nicht öffnen. Erst prüfen, sondern warten.")]
    [InlineData("Das ist nicht erlaubt. Wir warten, sondern gehen.", "Das ist nicht erlaubt. Wir warten, sondern gehen.")]
    [InlineData("Dienstag ... ach nee, Mittwoch", "Mittwoch")]
    [InlineData("Wir treffen uns ja Dienstag. Ach nee, Mittwoch.", "Wir treffen uns Mittwoch.")]
    [InlineData("Wir fahren nach Amsterdam, ach nee, nach Berlin.", "Wir fahren nach Berlin.")]
    [InlineData("Wir treffen uns am Dienstag im Büro, ach nee, am Mittwoch im Büro.", "Wir treffen uns am Mittwoch im Büro.")]
    [InlineData("Ich brauche drei rote Ordner, ach nee, vier blaue Ordner.", "Ich brauche vier blaue Ordner.")]
    [InlineData("Ich bringe Kaffee. Ach nee, Mittwoch.", "Ich bringe Kaffee. Ach nee, Mittwoch.")]
    public async Task Repair_preserves_unrelated_text(string input, string expected)
    {
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe(expected);
        (await _repairer.RepairAsync(result)).ShouldBe(expected);
    }

    // Ambiguous extra quantities and matches beyond a preposition keep the original text.
    [Theory]
    [InlineData(
        "Ich brauche drei rote Ordner und Etiketten für die Ordner, ach nee, vier blaue Ordner.",
        "Ich brauche vier blaue Ordner und Etiketten für die Ordner.")]
    [InlineData(
        "Ich brauche drei rote Ordner mit Etiketten für die Ordner, ach nee, vier blaue Ordner.",
        "Ich brauche vier blaue Ordner mit Etiketten für die Ordner.")]
    [InlineData(
        "Ich brauche drei rote Ordner, ach nee, vier und zwei blaue Hefter.",
        "Ich brauche drei rote Ordner, ach nee, vier und zwei blaue Hefter.")]
    [InlineData(
        "Wir treffen uns am Dienstag mit Peter im Büro, ach nee, Mittwoch im Büro.",
        "Wir treffen uns am Dienstag mit Peter im Büro, ach nee, Mittwoch im Büro.")]
    [InlineData(
        "I need three red folders and labels for the folders, no wait, four blue folders.",
        "I need four blue folders and labels for the folders.")]
    [InlineData(
        "Ich brauche drei rote Ordner und Etiketten für die Ordner, ach nee, vier.",
        "Ich brauche vier rote Ordner und Etiketten für die Ordner.")]
    [InlineData(
        "Wir treffen uns am Dienstag im Büro und später im Café, ach nee, Mittwoch im Büro.",
        "Wir treffen uns am Mittwoch im Büro und später im Café.")]
    [InlineData(
        "Ich brauche drei Ordner, ach nee, vier, ach nee, fünf.",
        "Ich brauche fünf Ordner.")]
    public async Task Repair_limits_replacement_to_supported_phrase(string input, string expected)
    {
        var result = await _repairer.RepairAsync(input);
        result.ShouldBe(expected);
        (await _repairer.RepairAsync(result)).ShouldBe(expected);
    }
}
