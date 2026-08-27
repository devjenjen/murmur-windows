namespace Murmur.Speech.Tests;

using System;
using System.IO;
using Murmur.Speech;
using Shouldly;
using Xunit;

public class NemotronStreamingTranscriberTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"murmur-nemotron-tests-{Guid.NewGuid():N}");

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public void Constructor_Rejects_Incomplete_Model_Directory()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(Path.Combine(temp.Path, "encoder.int8.onnx"), [1]);
        File.WriteAllBytes(Path.Combine(temp.Path, "decoder.int8.onnx"), [1]);
        File.WriteAllBytes(Path.Combine(temp.Path, "joiner.int8.onnx"), [1]);

        Should.Throw<DirectoryNotFoundException>(() =>
            new NemotronStreamingTranscriber(temp.Path));
    }
}
