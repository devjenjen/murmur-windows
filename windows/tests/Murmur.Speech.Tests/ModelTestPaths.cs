namespace Murmur.Speech.Tests;

using Murmur.Speech;

internal static class ModelTestPaths
{
    public static string GetRoot()
    {
        var root = Environment.GetEnvironmentVariable("MURMUR_TEST_MODELS_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Path.GetTempPath(), "Murmur-release-tests");
        }

        return Path.GetFullPath(root);
    }

    public static string GetDirectory(ModelManifest model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Path.Combine(GetRoot(), model.Id);
    }
}
