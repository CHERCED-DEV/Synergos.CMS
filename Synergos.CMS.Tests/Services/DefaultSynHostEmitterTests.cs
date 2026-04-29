using System.Globalization;
using System.Text.Json;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Tests.Proxies;

namespace Synergos.CMS.Tests.Services;

public class DefaultSynHostEmitterTests
{
    private static DefaultSynHostEmitter BuildSut(IBundleRegistryClient registry) =>
        new(registry);

    private static SynHostEmitRequest Request(
        string alias = "avatar",
        IReadOnlyDictionary<string, object?>? props = null,
        string? configOverride = null,
        CultureInfo? culture = null) =>
        new(alias, props, configOverride, culture ?? CultureInfo.GetCultureInfo("es-CO"));

    [Fact]
    public async Task EmitAsync_ResolvedRegistry_ProducesBothScriptAndElement()
    {
        var descriptor = new BundleDescriptor(
            MainEntryUri: new Uri("https://cdn.example.com/synergos/avatar/latest/main.js"),
            Dependencies: Array.Empty<Uri>(),
            Version: "0.1.0");
        var sut = BuildSut(new FakeBundleRegistryClient(descriptor));

        var result = await sut.EmitAsync(Request());

        Assert.True(result.RegistryResolved);
        Assert.Contains("type=\"module\"", result.ScriptHtml);
        Assert.Contains("cdn.example.com/synergos/avatar/latest/main.js", result.ScriptHtml);
        Assert.Contains("<synergos-avatar", result.ElementHtml);
        Assert.Contains("</synergos-avatar>", result.ElementHtml);
    }

    [Fact]
    public async Task EmitAsync_NullRegistryResolution_EmitsCommentPlaceholderButStillElement()
    {
        var sut = BuildSut(new FakeBundleRegistryClient());

        var result = await sut.EmitAsync(Request("badge"));

        Assert.False(result.RegistryResolved);
        Assert.StartsWith("<!--", result.ScriptHtml);
        Assert.Contains("did not resolve", result.ScriptHtml);
        Assert.Contains("<synergos-badge", result.ElementHtml);
        // Cap-280 Batch D — graceful frontend marker.
        Assert.Contains("data-synergos-cdn-offline=\"true\"", result.ElementHtml);
    }

    [Fact]
    public async Task EmitAsync_ResolvedRegistry_DoesNotEmitOfflineAttribute()
    {
        var descriptor = new BundleDescriptor(
            MainEntryUri: new Uri("https://cdn.example.com/synergos/avatar/latest/main.js"),
            Dependencies: Array.Empty<Uri>(),
            Version: "0.1.0");
        var sut = BuildSut(new FakeBundleRegistryClient(descriptor));

        var result = await sut.EmitAsync(Request());

        Assert.DoesNotContain("data-synergos-cdn-offline", result.ElementHtml);
    }

    [Fact]
    public async Task EmitAsync_CamelCaseAlias_BecomesKebabCaseCustomElement()
    {
        var sut = BuildSut(new FakeBundleRegistryClient());

        var result = await sut.EmitAsync(Request("countdownClock"));

        Assert.Contains("<synergos-countdown-clock", result.ElementHtml);
    }

    [Fact]
    public async Task EmitAsync_PropsAndCulture_IncludedInConfigJson()
    {
        var sut = BuildSut(new FakeBundleRegistryClient());
        var props = new Dictionary<string, object?>
        {
            ["avatarSrc"] = "https://cdn.example.com/u/1.jpg",
            ["size"] = "large",
        };

        var result = await sut.EmitAsync(
            Request("avatar", props, null, CultureInfo.GetCultureInfo("es-CO")));

        var configJson = ExtractConfigAttribute(result.ElementHtml);
        using var doc = JsonDocument.Parse(configJson);
        Assert.Equal("es-CO", doc.RootElement.GetProperty("culture").GetString());
        Assert.Equal("https://cdn.example.com/u/1.jpg", doc.RootElement.GetProperty("avatarSrc").GetString());
        Assert.Equal("large", doc.RootElement.GetProperty("size").GetString());
    }

    [Fact]
    public async Task EmitAsync_ValidConfigOverride_MergedOverProps()
    {
        var sut = BuildSut(new FakeBundleRegistryClient());
        var props = new Dictionary<string, object?> { ["size"] = "small" };
        const string overrideJson = """{"size":"xlarge","debug":true}""";

        var result = await sut.EmitAsync(Request("badge", props, overrideJson));

        var configJson = ExtractConfigAttribute(result.ElementHtml);
        using var doc = JsonDocument.Parse(configJson);
        Assert.Equal("xlarge", doc.RootElement.GetProperty("size").GetString());
        Assert.True(doc.RootElement.GetProperty("debug").GetBoolean());
    }

    [Fact]
    public async Task EmitAsync_InvalidConfigOverride_IgnoredGracefully()
    {
        var sut = BuildSut(new FakeBundleRegistryClient());
        var props = new Dictionary<string, object?> { ["size"] = "small" };

        var result = await sut.EmitAsync(
            Request("badge", props, "{this is not valid json}"));

        var configJson = ExtractConfigAttribute(result.ElementHtml);
        using var doc = JsonDocument.Parse(configJson);
        Assert.Equal("small", doc.RootElement.GetProperty("size").GetString());
    }

    [Fact]
    public async Task EmitAsync_EmptyBlockAlias_Throws()
    {
        var sut = BuildSut(new FakeBundleRegistryClient());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.EmitAsync(Request(alias: "")));
    }

    [Fact]
    public async Task EmitAsync_DependenciesListed_EmittedBeforeMainEntry()
    {
        var descriptor = new BundleDescriptor(
            MainEntryUri: new Uri("https://cdn.example.com/main.js"),
            Dependencies: new[]
            {
                new Uri("https://cdn.example.com/vendor/runtime.js"),
                new Uri("https://cdn.example.com/vendor/polyfills.js"),
            },
            Version: "0.1.0");
        var sut = BuildSut(new FakeBundleRegistryClient(descriptor));

        var result = await sut.EmitAsync(Request());

        var runtimeIdx = result.ScriptHtml.IndexOf("runtime.js", StringComparison.Ordinal);
        var mainIdx = result.ScriptHtml.IndexOf("main.js", StringComparison.Ordinal);
        Assert.InRange(runtimeIdx, 0, mainIdx);
    }

    [Fact]
    public async Task EmitAsync_AttributeEncoding_PreventsQuoteInjection()
    {
        var sut = BuildSut(new FakeBundleRegistryClient());
        var props = new Dictionary<string, object?>
        {
            ["label"] = "O'Brien's \"shop\" <script>alert('xss')</script>",
        };

        var result = await sut.EmitAsync(Request("badge", props));

        // Attribute is single-quoted. Raw apostrophes or angle brackets
        // inside would break out. System.Text.Json's default encoder
        // escapes <, >, ', ", & to \uXXXX; the resulting JSON is safe
        // inside a single-quoted HTML attribute without further encoding.
        var attrStart = result.ElementHtml.IndexOf("config='", StringComparison.Ordinal) + "config='".Length;
        var attrEnd = result.ElementHtml.IndexOf("'></", attrStart, StringComparison.Ordinal);
        var attrValue = result.ElementHtml[attrStart..attrEnd];

        Assert.DoesNotContain("'", attrValue);            // no raw apostrophe
        Assert.DoesNotContain("<script>", attrValue);      // no raw tag
        Assert.DoesNotContain("</script>", attrValue);
    }

    [Fact]
    public async Task ToKebabCase_HandlesEdgeCases()
    {
        Assert.Equal("", DefaultSynHostEmitter.ToKebabCase(""));
        Assert.Equal("avatar", DefaultSynHostEmitter.ToKebabCase("avatar"));
        Assert.Equal("countdown-clock", DefaultSynHostEmitter.ToKebabCase("countdownClock"));
        Assert.Equal("a-b-c", DefaultSynHostEmitter.ToKebabCase("aBC"));
        await Task.CompletedTask;
    }

    private static string ExtractConfigAttribute(string elementHtml)
    {
        const string marker = "config='";
        var start = elementHtml.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = elementHtml.IndexOf('\'', start);
        var encoded = elementHtml[start..end];
        // Decode the single-quote-safe entities we wrote in the attribute.
        return encoded
            .Replace("&#39;", "'")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">");
    }
}
