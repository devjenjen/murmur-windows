using System.Net;
using System.Net.Http;
using Murmur.Core.Updates;
using Shouldly;
using Xunit;

namespace Murmur.Core.Tests;

public sealed class ReleaseUpdateCheckerTests
{
    [Theory]
    [InlineData("v1.2.3", true)]
    [InlineData("V1.2.3", true)]
    [InlineData("1.2.3", false)]
    [InlineData("v1.2", false)]
    [InlineData("v1.2.3.4", false)]
    [InlineData("v1.two.3", false)]
    [InlineData("v1.2.3-beta.1", false)]
    public void TryParseStableTag_OnlyAcceptsStableThreePartTags(
        string tag,
        bool expected)
    {
        ReleaseUpdateChecker.TryParseStableTag(tag, out var version, out var normalizedTag)
            .ShouldBe(expected);

        if (expected)
        {
            version.ShouldBe(new Version(1, 2, 3));
            normalizedTag.ShouldBe("v1.2.3");
        }
    }

    [Fact]
    public void ParseLatestRelease_ConstructsFixedReleaseUri()
    {
        var result = ReleaseUpdateChecker.ParseLatestRelease(
            "{\"tag_name\":\"v1.4.0\",\"draft\":false,\"prerelease\":false}");

        result.ShouldNotBeNull();
        result.Version.ShouldBe(new Version(1, 4, 0));
        result.ReleaseUri.AbsoluteUri.ShouldBe(
            "https://github.com/devjenjen/murmur-windows/releases/tag/v1.4.0");
    }

    [Fact]
    public void ParseLatestRelease_RejectsDraftAndPrerelease()
    {
        ReleaseUpdateChecker.ParseLatestRelease(
            "{\"tag_name\":\"v1.4.0\",\"draft\":true,\"prerelease\":false}")
            .ShouldBeNull();

        ReleaseUpdateChecker.ParseLatestRelease(
            "{\"tag_name\":\"v1.4.0\",\"draft\":false,\"prerelease\":true}")
            .ShouldBeNull();
    }

    [Fact]
    public void ParseLatestRelease_RejectsMalformedOrMissingTag()
    {
        ReleaseUpdateChecker.ParseLatestRelease("{").ShouldBeNull();
        ReleaseUpdateChecker.ParseLatestRelease("{}").ShouldBeNull();
        ReleaseUpdateChecker.ParseLatestRelease(
            "{\"tag_name\":\"v1.4.0-beta.1\"}").ShouldBeNull();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"tag_name\":[]}")]
    [InlineData("{\"tag_name\":\"v1.4.0\",\"draft\":\"false\"}")]
    public void ParseLatestRelease_RejectsUnexpectedResponseShapes(string json)
    {
        ReleaseUpdateChecker.ParseLatestRelease(json).ShouldBeNull();
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("2.0.0", "1.9.9", false)]
    public void IsNewerThan_ComparesStableVersions(
        string current,
        string release,
        bool expected)
    {
        var info = new ReleaseInfo(
            Version.Parse(release),
            $"v{release}",
            new Uri("https://github.com/devjenjen/murmur-windows/releases/tag/v1.0.0"));

        ReleaseUpdateChecker.IsNewerThan(Version.Parse(current), info)
            .ShouldBe(expected);
    }

    [Fact]
    public async Task GetLatestStableRelease_ReturnsNullForHttpFailure()
    {
        using var client = new HttpClient(new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var checker = new ReleaseUpdateChecker(client);

        (await checker.GetLatestStableReleaseAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task GetLatestStableRelease_ParsesResponseWithoutNetwork()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"tag_name\":\"v2.0.0\",\"draft\":false,\"prerelease\":false}")
            }));
        using var checker = new ReleaseUpdateChecker(client);

        var result = await checker.GetLatestStableReleaseAsync();

        result.ShouldNotBeNull();
        result.Version.ShouldBe(new Version(2, 0, 0));
    }

    [Fact]
    public async Task GetLatestStableRelease_UsesFixedUriAndUserAgent()
    {
        Uri? requestUri = null;
        string? userAgent = null;
        using var client = new HttpClient(new StubHandler(request =>
        {
            requestUri = request.RequestUri;
            userAgent = request.Headers.UserAgent.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"tag_name\":\"v2.0.0\"}")
            };
        }));
        using var checker = new ReleaseUpdateChecker(client);

        await checker.GetLatestStableReleaseAsync();

        requestUri.ShouldBe(ReleaseUpdateChecker.LatestReleaseApiUri);
        userAgent.ShouldBe("Murmur");
    }

    [Fact]
    public async Task GetLatestStableRelease_ReturnsNullForMalformedJson()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{")
            }));
        using var checker = new ReleaseUpdateChecker(client);

        (await checker.GetLatestStableReleaseAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task GetLatestStableRelease_ReturnsNullForThrownNetworkFailure()
    {
        using var client = new HttpClient(new StubHandler(_ =>
            throw new HttpRequestException("Network unavailable")));
        using var checker = new ReleaseUpdateChecker(client);

        (await checker.GetLatestStableReleaseAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task GetLatestStableRelease_ReturnsNullWhenRequestTimesOut()
    {
        using var client = new HttpClient(new BlockingHandler());
        using var checker = new ReleaseUpdateChecker(client);

        (await checker.GetLatestStableReleaseAsync()
            .WaitAsync(TimeSpan.FromSeconds(7))).ShouldBeNull();
    }

    [Fact]
    public async Task GetLatestStableRelease_ReturnsNullWhenCancelled()
    {
        using var client = new HttpClient(new BlockingHandler());
        using var checker = new ReleaseUpdateChecker(client);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        (await checker.GetLatestStableReleaseAsync(cancellation.Token)).ShouldBeNull();
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
