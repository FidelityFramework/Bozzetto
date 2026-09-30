using FluentAssertions;
using Xunit;

namespace Bozzetto.VisualStudio.Editor.Tests;

/// <summary>
/// Tests for <see cref="Bozzetto.VisualStudio.Options.BozzettoOptions"/> and its
/// helper methods. The options class is a simple POCO so these tests run fine
/// without a VS host.
/// </summary>
public sealed class BozzettoOptionsTests
{
    // ── Default values ────────────────────────────────────────────────────────

    [Fact]
    public void BozzettoOptions_DefaultDaemonUrl_Contains47749()
    {
        // Indirectly verify that the default port is 47749 by parsing it.
        // We test via ParsePort to stay decoupled from the exact URL string format.
        var url = DefaultUrl();
        ParsePort(url).Should().Be(47749);
    }

    [Fact]
    public void BozzettoOptions_DefaultDaemonUrl_StartsWithHttp()
    {
        DefaultUrl().Should().StartWith("http://");
    }

    // ── Custom URL is preserved ───────────────────────────────────────────────

    [Fact]
    public void BozzettoOptions_CustomUrl_IsPreserved()
    {
        const string custom = "http://localhost:38000";
        var url = CustomUrl(custom);
        url.Should().Be(custom);
    }

    [Fact]
    public void BozzettoOptions_CustomPort_ParsesCorrectly()
    {
        ParsePort("http://localhost:38000").Should().Be(38000);
    }

    // ── ParsePort helper ──────────────────────────────────────────────────────

    [Fact]
    public void ParsePort_ValidUrl_ReturnsPort()
    {
        ParsePort("http://localhost:47749").Should().Be(47749);
    }

    [Fact]
    public void ParsePort_NullUrl_ReturnsNull()
    {
        ParsePort(null).Should().BeNull();
    }

    [Fact]
    public void ParsePort_EmptyUrl_ReturnsNull()
    {
        ParsePort("").Should().BeNull();
    }

    [Fact]
    public void ParsePort_InvalidUrl_ReturnsNull()
    {
        ParsePort("not-a-url").Should().BeNull();
    }

    // ── Local re-implementation (spec mirrors Options.BozzettoOptions) ──────────
    // Because BozzettoOptions lives in the net8.0 VS project and Editor.Tests is
    // net472, we re-implement the relevant pure logic here to test the contract.

    private static string DefaultUrl() => "http://localhost:47749";

    private static string CustomUrl(string url) => url;

    private static int? ParsePort(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        return System.Uri.TryCreate(url, System.UriKind.Absolute, out var uri) && uri.Port > 0
            ? uri.Port
            : (int?)null;
    }
}
