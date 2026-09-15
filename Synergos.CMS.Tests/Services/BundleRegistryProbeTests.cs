using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="BundleRegistryProbe"/> (Cap-280 Batch C, Olas
/// 287-288). Verifica que el verdict del probe matchea el Mode actual:
/// Stub healthy informativo, FileSystem healthy si resuelve, FileSystem
/// unhealthy si registry no está, Unknown unhealthy.
/// </summary>
public sealed class BundleRegistryProbeTests
{
    private static IOptionsMonitor<BundleRegistrySettings> MonitorFor(BundleRegistrySettings s)
    {
        var monitor = Substitute.For<IOptionsMonitor<BundleRegistrySettings>>();
        monitor.CurrentValue.Returns(s);
        return monitor;
    }

    /// <summary>
    /// Un import map con algo dentro — lo que el CDN publica cuando está bien.
    /// </summary>
    /// <remarks>
    /// <b>Hace falta en TODO caso sano desde el defecto #126.</b> Resolver un descriptor ya no
    /// alcanza para reportar salud: sin mapa, el navegador no resuelve los bare specifiers y
    /// ningún <c>&lt;synergos-*&gt;</c> se registra, con la página en 200. Los cuatro tests que
    /// afirmaban salud se pusieron rojos al cablearlo, que es como se supo que el probe de verdad
    /// lo mira.
    /// </remarks>
    private static ImportMap MapaSano() => new(new Dictionary<string, string>
    {
        ["@angular/core"] = "/cdn-bundles/synergos/runtime/angular/latest/core.js",
    });

    [Fact]
    public async Task StubMode_ReportsHealthy_WithStubMessage()
    {
        var client = Substitute.For<IBundleRegistryClient>();
        var settings = new BundleRegistrySettings { Mode = "Stub" };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.Equal("bundle_registry", result.Name);
        Assert.True(result.IsHealthy);
        Assert.Contains("stub", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        // No debe llamar al client en Stub mode — basta con el setting.
        await client.DidNotReceive().TryResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        // Cap-290 Batch A — Details emitido también en Stub.
        Assert.NotNull(result.Details);
        Assert.Equal("Stub", result.Details!["mode"]);
    }

    [Fact]
    public async Task FileSystemMode_DescriptorResolved_ReportsHealthy()
    {
        var descriptor = new BundleDescriptor(
            MainEntryUri: new Uri("https://cdn/synergos-column/angular/latest/main.js"),
            Dependencies: Array.Empty<Uri>(),
            Version: "0.1.0",
            Tag: "synergos-column",
            Alias: "elementStructColumn",
            Tier: "primitive",
            Integrity: "sha384-abc123",
            Framework: "angular");
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAnyAsync(Arg.Any<CancellationToken>()).Returns(descriptor);
        client.TryGetImportMapAsync(Arg.Any<CancellationToken>()).Returns(MapaSano());
        var settings = new BundleRegistrySettings { Mode = "FileSystem", LocalPath = @"C:\LOCAL_CDN" };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.True(result.IsHealthy);
        Assert.Contains("angular", result.Message ?? string.Empty);
        Assert.Contains("0.1.0", result.Message ?? string.Empty);
        Assert.Contains("present", result.Message ?? string.Empty);
        // Cap-290 Batch A — Details estructurados.
        Assert.NotNull(result.Details);
        Assert.Equal("FileSystem", result.Details!["mode"]);
        Assert.Equal("(cualquiera)", result.Details["probeTag"]);
        Assert.Equal("angular", result.Details["framework"]);
        Assert.Equal("0.1.0", result.Details["version"]);
        Assert.Equal("present", result.Details["integrity"]);
        Assert.Equal(true, result.Details["resolved"]);
    }

    [Fact]
    public async Task FileSystemMode_ProbeTagOverride_UsesConfiguredTag()
    {
        var descriptor = new BundleDescriptor(
            MainEntryUri: new Uri("https://cdn/custom-block/react/latest/main.js"),
            Dependencies: Array.Empty<Uri>(),
            Version: "1.2.3",
            Framework: "react");
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAsync("custom-block", Arg.Any<CancellationToken>())
              .Returns(descriptor);
        client.TryGetImportMapAsync(Arg.Any<CancellationToken>()).Returns(MapaSano());
        var settings = new BundleRegistrySettings
        {
            Mode = "FileSystem",
            LocalPath = @"C:\CUSTOM_CDN",
            ProbeTag = "custom-block",
        };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.True(result.IsHealthy);
        Assert.Contains("custom-block", result.Message ?? string.Empty);
        await client.Received(1).TryResolveAsync("custom-block", Arg.Any<CancellationToken>());
        await client.DidNotReceive().TryResolveAnyAsync(Arg.Any<CancellationToken>());
        Assert.Equal("custom-block", result.Details!["probeTag"]);
    }

    [Fact]
    public async Task FileSystemMode_DescriptorMissingIntegrity_ReportsHealthyWithMissingMarker()
    {
        var descriptor = new BundleDescriptor(
            MainEntryUri: new Uri("https://cdn/synergos-column/angular/latest/main.js"),
            Dependencies: Array.Empty<Uri>(),
            Version: "0.1.0",
            Framework: "angular",
            Integrity: null);
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAnyAsync(Arg.Any<CancellationToken>()).Returns(descriptor);
        client.TryGetImportMapAsync(Arg.Any<CancellationToken>()).Returns(MapaSano());
        var settings = new BundleRegistrySettings { Mode = "FileSystem", LocalPath = @"C:\LOCAL_CDN" };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.True(result.IsHealthy);
        Assert.Contains("missing", result.Message ?? string.Empty);
    }

    [Fact]
    public async Task FileSystemMode_NullDescriptor_ReportsUnhealthy()
    {
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAnyAsync(Arg.Any<CancellationToken>()).Returns((BundleDescriptor?)null);
        var settings = new BundleRegistrySettings
        {
            Mode = "FileSystem",
            LocalPath = @"C:\LOCAL_CDN",
            BundlesNamespace = "synergos",
        };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.Contains("cualquier elemento", result.Message ?? string.Empty);
        Assert.Contains(@"C:\LOCAL_CDN", result.Message ?? string.Empty);
    }

    [Fact]
    public async Task FileSystemMode_ClientThrows_ReportsUnhealthyWithExceptionMessage()
    {
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAnyAsync(Arg.Any<CancellationToken>())
              .Returns<Task<BundleDescriptor?>>(_ => throw new InvalidOperationException("boom"));
        var settings = new BundleRegistrySettings { Mode = "FileSystem", LocalPath = @"C:\LOCAL_CDN" };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.Contains("boom", result.Message ?? string.Empty);
    }

    // ── El modo Http, que faltaba desde que existe (defecto #39) ────────────

    [Fact]
    public async Task HttpMode_ConElCdnRespondiendo_ReportaSANO()
    {
        // EL DEFECTO. `Http` no estaba contemplado: caía al «modo desconocido» y respondía
        // «Unknown mode 'Http'. Valid: Stub | FileSystem | Http» — contradiciéndose en la misma
        // frase. Y como /_health devuelve 503 ante cualquier probe roja, el sitio quedaba
        // permanentemente en 503 desde el momento en que se encendiera el CDN.
        var descriptor = new BundleDescriptor(
            MainEntryUri: new Uri("https://cdn.ejemplo.co/synergos/badge/angular/0.1.0/main.js"),
            Dependencies: Array.Empty<Uri>(),
            Version: "0.1.0",
            Tag: "synergos-badge",
            Integrity: "sha256-abc",
            Framework: "angular");
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAnyAsync(Arg.Any<CancellationToken>()).Returns(descriptor);
        client.TryGetImportMapAsync(Arg.Any<CancellationToken>()).Returns(MapaSano());
        var settings = new BundleRegistrySettings
        {
            Mode = "Http",
            PublicBaseUrl = "https://cdn.ejemplo.co",
        };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.True(result.IsHealthy);
        Assert.Equal("Http", result.Details!["mode"]);
        Assert.Equal("synergos-badge", result.Details["resuelto"]);
        Assert.DoesNotContain("desconocido", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unknown", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HttpMode_ConElCdnCaido_ReportaNoSano_YDiceContraQueUrl()
    {
        // El rojo que SÍ hay que poder distinguir del anterior. Sin la URL en el mensaje, un
        // operador no sabe si el CDN se cayó o si está mal configurada la dirección.
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAnyAsync(Arg.Any<CancellationToken>()).Returns((BundleDescriptor?)null);
        var settings = new BundleRegistrySettings
        {
            Mode = "Http",
            PublicBaseUrl = "https://cdn.ejemplo.co",
        };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.Contains("https://cdn.ejemplo.co", result.Message ?? string.Empty);
    }

    [Fact]
    public async Task ConProbeTagPuesto_ElMensajeAVISA_deQuePuedeSerElTagYNoElCdn()
    {
        // La trampa que el default vacío evita, y que sigue existiendo para quien elija el
        // override: el elemento se retira del CDN, el probe se pone rojo, y el rojo parece un CDN
        // caído. Si el operador lo eligió, al menos el mensaje se lo recuerda.
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAsync("synergos-column", Arg.Any<CancellationToken>())
              .Returns((BundleDescriptor?)null);
        var settings = new BundleRegistrySettings
        {
            Mode = "Http",
            PublicBaseUrl = "https://cdn.ejemplo.co",
            ProbeTag = "synergos-column",
        };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.Contains("synergos-column", result.Message ?? string.Empty);
        Assert.Contains("dejó de publicarse", result.Message ?? string.Empty);
    }

    [Fact]
    public async Task ElDefaultYaNoSondeaUnTagQueElCDN_puede_retirar()
    {
        // La causa raíz del #39: `ProbeTag` venía con "synergos-column" de fábrica, el CDN lo
        // retiró a propósito junto con otros ocho, y el probe se puso rojo con el registry
        // perfecto. Un chequeo atado a un tag no vigila el registry — vigila que ESE elemento
        // siga publicado, y qué se publica no lo decide el CMS.
        Assert.Equal(string.Empty, new BundleRegistrySettings().ProbeTag);
    }

    [Fact]
    public async Task UnknownMode_ReportsUnhealthy()
    {
        var client = Substitute.For<IBundleRegistryClient>();
        var settings = new BundleRegistrySettings { Mode = "Quantum" };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.Contains("Quantum", result.Message ?? string.Empty);
    }

    [Fact]
    public async Task NullMode_DefaultsToStub_ReportsHealthy()
    {
        var client = Substitute.For<IBundleRegistryClient>();
        var settings = new BundleRegistrySettings { Mode = null! };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.True(result.IsHealthy);
        Assert.Contains("stub", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    // ── El import map (defecto #126) ─────────────────────────────────────────

    [Theory]
    [InlineData("FileSystem")]
    [InlineData("Http")]
    public async Task Con_el_registry_resolviendo_pero_SIN_import_map_reporta_NO_SANO(string modo)
    {
        // Éste es el defecto exacto. El registry contesta perfecto —resuelve su descriptor,
        // con su versión y su integrity— y la página igual sale sin hidratar, porque lo que
        // falta es el mapa. Antes de #126 esto reportaba SANO.
        var descriptor = new BundleDescriptor(
            MainEntryUri: new Uri("https://cdn/synergos-column/angular/latest/main.js"),
            Dependencies: Array.Empty<Uri>(),
            Version: "0.1.0",
            Tag: "synergos-column",
            Integrity: "sha384-abc123",
            Framework: "angular");
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAnyAsync(Arg.Any<CancellationToken>()).Returns(descriptor);
        client.TryGetImportMapAsync(Arg.Any<CancellationToken>()).Returns((ImportMap?)null);

        var settings = new BundleRegistrySettings
        {
            Mode = modo,
            LocalPath = @"C:\LOCAL_CDN",
            PublicBaseUrl = "https://cdn.example.com",
        };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.Contains("import map", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        // El mensaje tiene que decir la CONSECUENCIA y no sólo el hecho: quien lea esto a las
        // 3 AM necesita saber que el sitio se ve bien y no funciona.
        Assert.Contains("hidrata", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ausente", result.Details!["importMap"]);
        // Y deja escrito que el registry SÍ resolvía: si no, alguien va a buscar un CDN caído.
        Assert.Equal(true, result.Details["resolved"]);
    }

    [Fact]
    public async Task Un_import_map_VACIO_no_es_lo_mismo_que_uno_ausente_y_los_dos_son_no_sanos()
    {
        // Las dos causas son distintas —«no se pudo preguntar» y «el registry contestó sin
        // importaciones»— y las dos dejan el sitio sin hidratar. El probe las separa en los
        // detalles porque llevan a sitios distintos: una es el CDN, la otra es la publicación.
        var descriptor = new BundleDescriptor(
            MainEntryUri: new Uri("https://cdn/x/angular/latest/main.js"),
            Dependencies: Array.Empty<Uri>(),
            Version: "1.0.0");
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAnyAsync(Arg.Any<CancellationToken>()).Returns(descriptor);
        client.TryGetImportMapAsync(Arg.Any<CancellationToken>())
              .Returns(new ImportMap(new Dictionary<string, string>()));

        var probe = new BundleRegistryProbe(
            client,
            MonitorFor(new BundleRegistrySettings { Mode = "Http", PublicBaseUrl = "https://cdn.example.com" }));

        var result = await probe.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.Equal("vacío", result.Details!["importMap"]);
    }

    [Fact]
    public async Task En_modo_Stub_no_se_pregunta_por_el_mapa_y_se_sigue_reportando_SANO()
    {
        // Sin CDN no se esperan elementos, así que la ausencia de mapa no es un fallo — la misma
        // razón por la que el modo Stub nunca sondeó el registry. Confundir «no lo montamos» con
        // «se cayó» arruina el único indicador que hay.
        var client = Substitute.For<IBundleRegistryClient>();
        var probe = new BundleRegistryProbe(client, MonitorFor(new BundleRegistrySettings { Mode = "Stub" }));

        var result = await probe.CheckAsync();

        Assert.True(result.IsHealthy);
        await client.DidNotReceive().TryGetImportMapAsync(Arg.Any<CancellationToken>());
    }
}
