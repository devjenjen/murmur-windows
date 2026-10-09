using System.Reflection;
using Murmur.App.Localization;
using Murmur.Core.Abstractions;
using Murmur.Core.Formatting;
using Murmur.Core.Settings;
using Shouldly;
using Xunit;

namespace Murmur.App.Tests;

public class MathProfileTests
{
    [Theory]
    [InlineData(UiLanguage.German)]
    [InlineData(UiLanguage.English)]
    public void Math_Profile_Formats_Both_Dictation_Languages_Regardless_Of_Interface(string language)
    {
        try
        {
            UiText.SetLanguage(language);

            var unicodeFormatter = CreateFormatter(new DictationProfile
            {
                Id = "math",
                Name = "Math",
                FormatterKind = ProfileFormatterKind.Math,
                MathMode = MathFormatMode.Unicode
            });
            unicodeFormatter.Format("five times three equals fifteen").ShouldBe("5 × 3 = 15.");
            unicodeFormatter.Format("fünf mal drei gleich fünfzehn").ShouldBe("5 × 3 = 15.");

            var latexFormatter = CreateFormatter(new DictationProfile
            {
                Id = "math",
                Name = "Math",
                FormatterKind = ProfileFormatterKind.Math,
                MathMode = MathFormatMode.Latex
            });
            latexFormatter.Format("x equals five point zero five").ShouldBe("x = 5.05.");
            latexFormatter.Format("x gleich fünf komma null fünf").ShouldBe("x = 5.05.");

            var standardFormatter = CreateFormatter(new DictationProfile
            {
                Id = "standard",
                Name = "Standard",
                FormatterKind = ProfileFormatterKind.Standard
            });
            standardFormatter.Format("five times three equals fifteen")
                .ShouldBe("Five times three equals fifteen.");
        }
        finally
        {
            UiText.SetLanguage(UiLanguage.English);
        }
    }

    private static ITextFormatter CreateFormatter(DictationProfile profile)
    {
        var factory = typeof(Murmur.App.App).GetMethod(
            "CreateFormatter",
            BindingFlags.NonPublic | BindingFlags.Static);
        factory.ShouldNotBeNull();
        return (ITextFormatter)factory!.Invoke(null, new object?[] { profile })!;
    }
}
