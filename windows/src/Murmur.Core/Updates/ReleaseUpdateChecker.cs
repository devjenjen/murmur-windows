using System.Net.Http;
using System.Text.Json;

namespace Murmur.Core.Updates;

/// <summary>Checks the fixed Murmur GitHub repository for stable releases.</summary>
public sealed class ReleaseUpdateChecker : IDisposable
{
    /// <summary>Fixed GitHub repository used for release checks.</summary>
    public const string Repository = "devjenjen/murmur-windows";

    /// <summary>GitHub API endpoint for the latest release.</summary>
    public static readonly Uri LatestReleaseApiUri =
        new($"https://api.github.com/repos/{Repository}/releases/latest");

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <summary>Initializes an updater using the supplied or a private HTTP client.</summary>
    public ReleaseUpdateChecker(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateHttpClient();
        _ownsHttpClient = httpClient is null;
    }

    /// <summary>Gets the latest stable release without affecting application startup.</summary>
    /// <param name="cancellationToken">Cancellation token for the network request.</param>
    /// <returns>Release metadata, or null when the response is unavailable or invalid.</returns>
    public async Task<ReleaseInfo?> GetLatestStableReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUri);
            request.Headers.UserAgent.ParseAdd("Murmur");
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            return ParseLatestRelease(json);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Checks whether a release version is newer than the running version.</summary>
    /// <param name="currentVersion">Version of the running application.</param>
    /// <param name="release">Release to compare.</param>
    public static bool IsNewerThan(Version currentVersion, ReleaseInfo release)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        ArgumentNullException.ThrowIfNull(release);
        return release.Version > currentVersion;
    }

    /// <summary>Parses a GitHub latest-release response.</summary>
    /// <param name="json">JSON response from the fixed GitHub endpoint.</param>
    /// <returns>Stable release metadata, or null for drafts, prereleases, or invalid data.</returns>
    public static ReleaseInfo? ParseLatestRelease(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                (root.TryGetProperty("draft", out var draft) &&
                    draft.ValueKind is not JsonValueKind.True and not JsonValueKind.False) ||
                (root.TryGetProperty("prerelease", out var prerelease) &&
                    prerelease.ValueKind is not JsonValueKind.True and not JsonValueKind.False))
            {
                return null;
            }

            if (draft.ValueKind == JsonValueKind.True || prerelease.ValueKind == JsonValueKind.True ||
                !root.TryGetProperty("tag_name", out var tagElement) ||
                tagElement.ValueKind != JsonValueKind.String ||
                !TryParseStableTag(tagElement.GetString(), out var version, out var tag))
            {
                return null;
            }

            return new ReleaseInfo(
                version,
                tag,
                new Uri($"https://github.com/{Repository}/releases/tag/{tag}", UriKind.Absolute));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Parses a stable v-prefixed three-part release tag.</summary>
    /// <param name="tag">Candidate Git tag.</param>
    /// <param name="version">Parsed semantic version when successful.</param>
    /// <param name="normalizedTag">Normalized v-prefixed tag when successful.</param>
    /// <returns>True when the tag is a stable three-part version.</returns>
    public static bool TryParseStableTag(
        string? tag,
        out Version version,
        out string normalizedTag)
    {
        version = new Version();
        normalizedTag = string.Empty;

        if (string.IsNullOrWhiteSpace(tag) ||
            !tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var versionText = tag[1..];
        var parts = versionText.Split('.');
        if (parts.Length != 3 ||
            parts.Any(part => part.Length == 0 || part.Any(character => !char.IsAsciiDigit(character))) ||
            !Version.TryParse(versionText, out var parsedVersion))
        {
            return false;
        }

        version = parsedVersion;
        normalizedTag = $"v{versionText}";
        return true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static HttpClient CreateHttpClient() => new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
}
