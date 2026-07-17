using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Tests para <see cref="SynergosBridgeController"/> (Cap-260 Batch A,
/// Ola 253). Endpoint <c>/synergos-bridge.js</c> sirve el host bridge
/// como external script para sites con CSP estricto.
/// </summary>
public sealed class SynergosBridgeControllerTests
{
    private readonly IHostBridgeContextBuilder _builder = Substitute.For<IHostBridgeContextBuilder>();

    private SynergosBridgeController BuildSut()
    {
        var sut = new SynergosBridgeController(
            _builder,
            NullLogger<SynergosBridgeController>.Instance);
        sut.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return sut;
    }

    private static HostBridgeContext SampleContext() =>
        new(
            Version: "1.0.0",
            I18n: new HostBridgeI18n(
                Culture: "es-CO",
                DefaultCulture: "es-CO",
                Keys: new Dictionary<string, string> { ["Form.Submit"] = "Enviar" }),
            Theme: new HostBridgeTheme(Variant: "light", Available: new[] { "light", "dark" }),
            Brand: new HostBridgeBrand(Key: "default", DisplayName: "Synergos"),
            Member: null,
            Page: new HostBridgePage(Id: 42, DocType: "pageStandard",
                CanonicalUrl: "https://synergos.test/p/42",
                Cultures: new[] { "es-CO" }));

    [Fact]
    public void Get_HappyPath_ReturnsJsContentType()
    {
        _builder.Build().Returns(SampleContext());
        var sut = BuildSut();

        var result = sut.Get();

        var content = Assert.IsType<ContentResult>(result);
        Assert.Equal("application/javascript", content.ContentType?.Split(';')[0].Trim());
    }

    [Fact]
    public void Get_HappyPath_BodyAssignsWindowSynergos()
    {
        _builder.Build().Returns(SampleContext());
        var sut = BuildSut();

        var result = (ContentResult)sut.Get();

        Assert.Contains("window.synergos = ", result.Content);
        Assert.Contains("\"version\":\"1.0.0\"", result.Content);
        Assert.Contains("\"culture\":\"es-CO\"", result.Content);
        Assert.Contains("\"Form.Submit\":\"Enviar\"", result.Content);
        Assert.Contains("window.synergos.i18n.t = function(k, f)", result.Content);
    }

    [Fact]
    public void Get_HappyPath_SetsCacheControlPrivateNoStore()
    {
        _builder.Build().Returns(SampleContext());
        var sut = BuildSut();

        sut.Get();

        var cacheControl = sut.Response.Headers.CacheControl.ToString();
        Assert.Contains("private", cacheControl);
        Assert.Contains("no-store", cacheControl);
    }

    [Fact]
    public void Get_BuilderThrows_ReturnsFallbackPayload()
    {
        _builder.Build().Returns(_ => throw new InvalidOperationException("seam-failed"));
        var sut = BuildSut();

        var result = (ContentResult)sut.Get();

        // Aún sirve la JS válida — UI no rompe. Fallback shape mínimo.
        Assert.Contains("window.synergos = ", result.Content);
        Assert.Contains("\"keys\":{}", result.Content);
        Assert.Contains("\"member\":null", result.Content);
        Assert.Equal("application/javascript", result.ContentType?.Split(';')[0].Trim());
    }

    [Fact]
    public void Get_SerializedKeysPreserveExoticCharacters()
    {
        // Edge: keys con caracteres especiales que deberían escaparse en JSON
        // (no romper el JS evaluado por el browser).
        _builder.Build().Returns(new HostBridgeContext(
            Version: "1.0.0",
            I18n: new HostBridgeI18n("es-CO", "es-CO",
                new Dictionary<string, string>
                {
                    ["Form.Submit"] = "Enviar \"con\" comillas",
                    ["Common.Loading"] = "Cargando…</script>",
                }),
            Theme: new HostBridgeTheme("light", new[] { "light" }),
            Brand: new HostBridgeBrand("default", "Synergos"),
            Member: null,
            Page: new HostBridgePage(0, "", "", Array.Empty<string>())));

        var sut = BuildSut();
        var result = (ContentResult)sut.Get();

        // El </script> dentro de un <script> tag rompería el HTML — pero
        // este endpoint sirve application/javascript no HTML, así que el
        // navegador NO interpreta </script> como cierre. La preocupación
        // es solo válida en el modo inline del partial.
        // El default JsonSerializer usa JavaScriptEncoder.Default que
        // escapa comillas internas como " (no \"). Verifica que el
        // raw "con" comillas no aparezca sin escape.
        Assert.Contains("Enviar", result.Content);
        Assert.DoesNotContain("Enviar \"con\"", result.Content); // raw NO presente
        Assert.Contains("\\u0022con\\u0022", result.Content);    // escapado a unicode
    }
}
