using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Tests.Services.SynHost;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La configuración de negocio de Eventos (ADR 0137, piloto #194): la sección y su fusión por sitio,
/// su validación al arrancar, la recarga en caliente, y que lo que se muestra es lo que se cobra.
/// </summary>
/// <remarks>
/// <para><b>Qué había antes.</b> La comisión era <c>DEFAULT_FEE_PERCENT = 12</c> compilada en el
/// bundle; ningún camino del servidor la cobraba. El carrito sumaba un 12 % que el checkout no
/// cobraba: el comprador veía un total y se le cobraba otro.</para>
/// </remarks>
public sealed class NegocioDeEventosTests
{
    private static readonly Guid Bogota = Guid.Parse("8b0e2f6a-1c3d-4e5f-9a7b-6c5d4e3f2a10");
    private static readonly Guid Medellin = Guid.Parse("2d4f6a8c-0e1b-4c3d-8e7f-9a0b1c2d3e4f");

    // ── La sección y la fusión por sitio ─────────────────────────────────────────────────────

    [Fact]
    public void Sin_sitio_rigen_los_valores_base_que_traia_el_bundle()
    {
        var negocio = new EventosFeatureSettings().Para(null);

        // El 12 % y el 10 % eran las constantes compiladas: con los valores base nada cambia.
        Assert.Equal(new NegocioDeEventos("/api/eventos", 12m, 10m), negocio);
    }

    [Fact]
    public void Un_sitio_cambia_una_clave_y_hereda_las_demas()
    {
        var seccion = Seccion(new()
        {
            [$"Sitios:{Bogota}:FeePercent"] = "8",
        });

        Assert.Equal(new NegocioDeEventos("/api/eventos", 8m, 10m), seccion.Para(Bogota));
    }

    [Fact]
    public void Dos_sitios_con_valores_distintos_y_un_tercero_que_hereda_todo()
    {
        var seccion = Seccion(new()
        {
            [$"Sitios:{Bogota}:FeePercent"] = "8",
            [$"Sitios:{Medellin}:FeePercent"] = "15",
            [$"Sitios:{Medellin}:ApiBase"] = "https://api.ejemplo.co/eventos",
        });

        Assert.Equal(8m, seccion.Para(Bogota).FeePercent);
        Assert.Equal(15m, seccion.Para(Medellin).FeePercent);
        Assert.Equal("https://api.ejemplo.co/eventos", seccion.Para(Medellin).ApiBase);
        Assert.Equal(new EventosFeatureSettings().Para(null), seccion.Para(Guid.NewGuid()));
    }

    [Fact]
    public void La_Key_del_sitio_se_compara_como_GUID_y_no_como_texto()
    {
        var seccion = Seccion(new()
        {
            [$"Sitios:{{{Bogota.ToString().ToUpperInvariant()}}}:FeePercent"] = "8",
        });

        Assert.Equal(8m, seccion.Para(Bogota).FeePercent);
    }

    // ── La regla de la comisión: los vectores de oro compartidos ─────────────────────────────

    private sealed record Vector(string Nombre, decimal Subtotal, decimal FeePercent, decimal Fee);

    private static IReadOnlyList<Vector> Vectores()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var ruta = Path.Combine(dir!.FullName, "Synergos.CMS.Web", "docs", "contracts", "service-fee-vectors.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(ruta));
        return doc.RootElement.GetProperty("vectores").EnumerateArray()
            .Select(v => new Vector(
                v.GetProperty("nombre").GetString()!,
                v.GetProperty("subtotal").GetDecimal(),
                v.GetProperty("feePercent").GetDecimal(),
                v.GetProperty("fee").GetDecimal()))
            .ToList();
    }

    [Fact]
    public void La_comision_del_CMS_da_los_vectores_de_oro()
    {
        var vectores = Vectores();

        // Los vectores DECIDEN el redondeo: con mitad-arriba alguno tiene que dar otro número, o
        // un cambio de regla pasaría en verde.
        Assert.True(vectores.Count >= 10);
        Assert.Contains(vectores, v =>
            Math.Round(v.Subtotal * v.FeePercent / 100m, 2, MidpointRounding.AwayFromZero) != v.Fee);

        var fallan = vectores
            .Where(v => new NegocioDeEventos("/api/eventos", v.FeePercent, 10m).ComisionSobre(v.Subtotal) != v.Fee)
            .Select(v => v.Nombre)
            .ToList();

        Assert.True(fallan.Count == 0, "La comisión del CMS no da estos vectores: " + string.Join("; ", fallan));
    }

    // ── El validador, por la tubería de opciones real ────────────────────────────────────────

    [Fact]
    public void Una_seccion_bien_escrita_arranca()
    {
        using var sp = Opciones(new()
        {
            ["ApiBase"] = "https://api.ejemplo.co/eventos",
            ["FeePercent"] = "12.5",
            [$"Sitios:{Bogota}:FeePercent"] = "8",
            [$"Sitios:{Bogota}:PlatformFeePercent"] = "0",
        });

        sp.GetRequiredService<IStartupValidator>().Validate();
        Assert.Equal(8m, sp.GetRequiredService<IOptionsMonitor<EventosFeatureSettings>>().CurrentValue.Para(Bogota).FeePercent);
    }

    [Theory]
    [InlineData("FeePercnt", "8", "FeePercnt")]
    [InlineData("Currency", "USD", "Currency")]
    [InlineData("Sitios:8b0e2f6a-1c3d-4e5f-9a7b-6c5d4e3f2a10:Fee", "8", "Fee")]
    [InlineData("Sitios:bogota:FeePercent", "8", "Key (GUID)")]
    [InlineData("FeePercent", "101", "0 a 100")]
    [InlineData("FeePercent", "-1", "0 a 100")]
    [InlineData("FeePercent", "12.345", "dos decimales")]
    [InlineData("Sitios:8b0e2f6a-1c3d-4e5f-9a7b-6c5d4e3f2a10:PlatformFeePercent", "150", "0 a 100")]
    [InlineData("ApiBase", "//evil.example/api", "ApiBase")]
    [InlineData("ApiBase", "/\\evil.example/api", "ApiBase")]
    [InlineData("ApiBase", "ftp://ejemplo.co/eventos", "ApiBase")]
    [InlineData("ApiBase", "api/eventos", "ApiBase")]
    public void Una_clave_mal_escrita_o_fuera_de_rango_no_deja_arrancar(string clave, string valor, string seDice)
    {
        using var sp = Opciones(new() { [clave] = valor });

        var error = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(seDice, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Un porcentaje escrito como en es-CO —«1,5»— tampoco deja arrancar: lo rechaza el binder al
    /// convertirlo. Medido: no lo lee como 15, que era el riesgo de la cultura invariante.
    /// </summary>
    [Fact]
    public void Un_porcentaje_con_coma_decimal_no_deja_arrancar()
    {
        using var sp = Opciones(new() { ["FeePercent"] = "1,5" });

        var error = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("FeePercent", error.Message, StringComparison.Ordinal);
    }

    // ── El lector por sitio: recarga sin reinicio y el último valor válido ───────────────────

    [Fact]
    public void Un_cambio_en_la_configuracion_rige_desde_la_peticion_siguiente()
    {
        var (config, sp) = Recargable(new() { [$"Sitios:{Bogota}:FeePercent"] = "8" });
        using var _ = sp;
        var lector = Lector(sp, () => Bogota);
        Assert.Equal(8m, lector.Actual().FeePercent);

        config[$"{EventosFeatureSettings.Seccion}:Sitios:{Bogota}:FeePercent"] = "9.5";
        config.Reload();

        Assert.Equal(9.5m, lector.Actual().FeePercent);
    }

    [Fact]
    public void Una_recarga_invalida_sigue_con_el_ultimo_valor_valido_y_lo_dice_una_vez()
    {
        var (config, sp) = Recargable(new() { ["FeePercent"] = "12" });
        using var _ = sp;
        var log = new LogQueCuenta();
        var lector = Lector(sp, () => null, log);

        config[$"{EventosFeatureSettings.Seccion}:FeePercent"] = "250";
        // El propio monitor valida al recibir el aviso, y lanza ahí: en producción ese aviso corre
        // en la tarea del observador de ficheros y la excepción se pierde; acá sale por Reload().
        var aviso = Assert.Throws<AggregateException>(config.Reload);
        Assert.IsType<OptionsValidationException>(Assert.Single(aviso.InnerExceptions));

        Assert.Equal(12m, lector.Actual().FeePercent);
        Assert.Equal(12m, lector.Actual().FeePercent);
        Assert.Equal(1, log.Errores);

        config[$"{EventosFeatureSettings.Seccion}:FeePercent"] = "7";
        config.Reload();

        Assert.Equal(7m, lector.Actual().FeePercent);
    }

    [Fact]
    public void Cada_peticion_lee_la_configuracion_de_su_sitio()
    {
        var (_, sp) = Recargable(new() { [$"Sitios:{Bogota}:FeePercent"] = "8" });
        using var __ = sp;
        Guid? sitio = Bogota;
        var lector = Lector(sp, () => sitio);

        Assert.Equal(8m, lector.Actual().FeePercent);
        sitio = Medellin;
        Assert.Equal(12m, lector.Actual().FeePercent);
        sitio = null;
        Assert.Equal(12m, lector.Actual().FeePercent);
    }

    // ── Lo que se muestra: el resolver ───────────────────────────────────────────────────────

    [Fact]
    public void El_resolver_copia_lo_del_editor_y_la_configuracion_del_sitio()
    {
        var resolutor = new EventosResolutor(ElementoFalso.Fallback, Fijo(8m), NullLogger<EventosResolutor>.Instance);

        var props = resolutor.Resolver(ElementoFalso.Con(
            ("heading", "Cartelera"), ("subheading", "Lo que viene"), ("role", "organizer"),
            // Lo que el editor podía escribir antes: ya no viaja.
            ("apiBase", "/otra/api"), ("config", """{"feePercent":0}"""))).Props;

        Assert.Equal(new EventosProps("Cartelera", "Lo que viene", "organizer", "/api/eventos", 8m, 10m), props);
    }

    // ── Lo que se cobra: el motor en proceso ─────────────────────────────────────────────────

    [Fact]
    public async Task El_motor_en_proceso_cobra_la_comision_como_una_linea_mas()
    {
        var pagos = new PagosQueRecuerdan();
        var svc = Motor(pagos, Fijo(12m));

        var compra = await svc.CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 2) },
            new[] { Asistente("1"), Asistente("2") });

        // 2 × 180.000 = 360.000, más el 12 %: lo mismo que suma el carrito.
        Assert.Equal(403_200m, compra.Amount);
        Assert.Equal(403_200m, pagos.Ultima!.Amount);
        var cargo = Assert.Single(pagos.Ultima.Items, l => l.Sku == "fees");
        Assert.Equal(43_200m, cargo.UnitPrice);
        Assert.Equal(pagos.Ultima.Amount, pagos.Ultima.Items.Sum(l => l.UnitPrice * l.Quantity));
    }

    [Fact]
    public async Task Dos_sitios_cobran_cada_uno_su_comision()
    {
        Guid? sitio = Bogota;
        var (_, sp) = Recargable(new() { [$"Sitios:{Bogota}:FeePercent"] = "8" });
        using var __ = sp;
        var svc = Motor(new PagosQueRecuerdan(), Lector(sp, () => sitio));

        var enBogota = await svc.CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 1) }, new[] { Asistente("1") });
        sitio = Medellin;
        var enMedellin = await svc.CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 1) }, new[] { Asistente("2") });

        Assert.Equal(194_400m, enBogota.Amount);   // 180.000 + 8 %
        Assert.Equal(201_600m, enMedellin.Amount); // 180.000 + 12 %, heredado
    }

    [Fact]
    public async Task Sin_configuracion_de_negocio_no_hay_comision()
    {
        var pagos = new PagosQueRecuerdan();

        var compra = await Motor(pagos, negocio: null).CheckoutAsync("evt-festival-estereo",
            new[] { new EventCheckoutItem("GEN", null, 1) }, new[] { Asistente("1") });

        Assert.Equal(180_000m, compra.Amount);
        Assert.DoesNotContain(pagos.Ultima!.Items, l => l.Sku == "fees");
    }

    // ── Montaje ──────────────────────────────────────────────────────────────────────────────

    private static EventosFeatureSettings Seccion(Dictionary<string, string?> claves)
        => new ConfigurationBuilder().AddInMemoryCollection(claves).Build().Get<EventosFeatureSettings>()!;

    private static Dictionary<string, string?> EnLaSeccion(Dictionary<string, string?> claves)
        => claves.ToDictionary(c => $"{EventosFeatureSettings.Seccion}:{c.Key}", c => c.Value);

    /// <summary>Como lo registra el composer: enlazada, validada al arrancar y con su validador.</summary>
    private static ServiceProvider Proveedor(IConfiguration config)
    {
        var servicios = new ServiceCollection().AddSingleton(config);
        servicios.AddOptions<EventosFeatureSettings>()
            .Bind(config.GetSection(EventosFeatureSettings.Seccion))
            .ValidateOnStart();
        servicios.AddSingleton<IValidateOptions<EventosFeatureSettings>, ValidadorDeNegocioDeEventos>();
        return servicios.BuildServiceProvider();
    }

    private static ServiceProvider Opciones(Dictionary<string, string?> claves)
        => Proveedor(new ConfigurationBuilder().AddInMemoryCollection(EnLaSeccion(claves)).Build());

    private static (IConfigurationRoot Config, ServiceProvider Sp) Recargable(Dictionary<string, string?> claves)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(EnLaSeccion(claves)).Build();
        return (config, Proveedor(config));
    }

    private static NegocioDeEventosDelSitio Lector(IServiceProvider sp, Func<Guid?> sitio, ILogger<NegocioDeEventosDelSitio>? log = null)
        => new(sp.GetRequiredService<IOptionsMonitor<EventosFeatureSettings>>(), sitio,
            log ?? NullLogger<NegocioDeEventosDelSitio>.Instance);

    private static INegocioDeEventos Fijo(decimal comision)
    {
        var negocio = Substitute.For<INegocioDeEventos>();
        negocio.Actual().Returns(new NegocioDeEventos("/api/eventos", comision, 10m));
        return negocio;
    }

    private static StubEventTicketingService Motor(IPaymentProvider pagos, INegocioDeEventos? negocio)
        => new(new StubEventCatalogProvider(), new StubReservationService(), pagos,
            null, null, null, null,
            signer: new HmacTicketSigner("llave-de-tests-negocio"u8.ToArray()),
            negocio: negocio);

    private static EventAttendeeInfo Asistente(string n) => new($"Asistente {n}", $"asistente{n}@synergos.co", $"100{n}");

    private sealed class LogQueCuenta : ILogger<NegocioDeEventosDelSitio>
    {
        public int Errores { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                Errores++;
            }
        }
    }

    /// <summary>Un proveedor de pagos que recuerda con qué se le abrió la última sesión.</summary>
    private sealed class PagosQueRecuerdan : IPaymentProvider, IDisposable
    {
        private readonly StubPaymentProvider _real = new();

        public string ProviderKey => _real.ProviderKey;

        public PaymentSessionRequest? Ultima { get; private set; }

        public void Dispose() => _real.Dispose();

        public Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest request, CancellationToken ct = default)
        {
            Ultima = request;
            return _real.CreateSessionAsync(request, ct);
        }

        public Task<PaymentOutcome> GetStatusAsync(string sessionId, CancellationToken ct = default)
            => _real.GetStatusAsync(sessionId, ct);

        public Task<PaymentOutcome> CaptureAsync(string sessionId, decimal? amount = null, CancellationToken ct = default)
            => _real.CaptureAsync(sessionId, amount, ct);

        public Task<PaymentOutcome> VoidAsync(string sessionId, CancellationToken ct = default)
            => _real.VoidAsync(sessionId, ct);

        public Task<PaymentOutcome> RefundAsync(string sessionId, decimal? amount = null, CancellationToken ct = default)
            => _real.RefundAsync(sessionId, amount, ct);
    }
}
