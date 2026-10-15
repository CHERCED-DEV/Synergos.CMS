using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Los pasos de forma sin parámetros y la carga de flujos del host genérico (núcleo N1).
/// </summary>
public sealed class PasosDeFormaYCargaTests
{
    [Fact]
    public void El_orquestador_declarado_levanta_la_maquina_compartida_con_sus_barridos_y_su_arriendo()
    {
        // Lo que `BarridoSegregationTests` vigila sobre `AddSagaMachinery` vale para el host genérico sólo si
        // entra por ahí (núcleo N1, T022): un `AddOrquestadorDeclarado` que registrara la saga a mano
        // arrancaría sano y pasaría su /health, pero no reintentaría lo que quedó sin deshacer, no barrería
        // los avisos y, con dos réplicas, compensaría dos veces.
        var raiz = Path.Combine(Path.GetTempPath(), "syn-declarado-" + Guid.NewGuid().ToString("N"));
        try
        {
            var builder = WebApplication.CreateBuilder();
            builder.Configuration["muestra:Storage:Root"] = raiz;
            builder.AddOrquestadorDeclarado(ArranqueDeLaMuestra.Vocabulario, typeof(ArranqueDeLaMuestra).Assembly);

            var deFondo = builder.Services.Where(d => d.ServiceType == typeof(IHostedService)).Select(d => d.ImplementationType).ToList();
            Assert.Contains(typeof(CompensationSweeper<SagaDeFlujo>), deFondo);
            Assert.Contains(typeof(DeliverySweeper), deFondo);

            using var proveedor = builder.Services.BuildServiceProvider();
            Assert.IsType<FileSystemSagaLease>(proveedor.GetRequiredService<ISagaLease>());
            Assert.NotNull(proveedor.GetRequiredService<SagaEngine<SagaDeFlujo>>());
        }
        finally
        {
            try { if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }
    }

    private sealed class Precios : IPricingPort
    {
        public Rejection? Rechaza { get; set; }
        public List<string?> Promociones { get; } = [];

        public Task<Result<QuoteView>> CotizarAsync(IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas, string? promocion, CancellationToken ct)
        {
            Promociones.Add(promocion);
            var m = Money.Of(1000m * lineas.Sum(l => l.Cantidad), "COP");
            return Task.FromResult(Rechaza is { } r ? Result.Rejected<QuoteView>(r) : Result.Ok(new QuoteView(m, Money.Zero("COP"), Money.Zero("COP"), m)));
        }
    }

    private static EntradaDePaso Entrada(string tipo, FlowContext ctx, params string[] lee)
        => new("s-1", Ref.Create("prueba.flujo", "s-1"), new PasoDef("p", tipo, lee, [], null, null, null, null, null), ctx, null, null);

    [Fact]
    public async Task Los_precios_por_linea_se_cruzan_por_SUJETO_y_no_por_posicion()
    {
        var a = Ref.Create("prueba.producto", "a");
        var b = Ref.Create("prueba.producto", "b");
        IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas = [(a, 2), (b, 1)];
        // La cotización contesta en OTRO orden: un cruce por posición le pondría a «a» el precio de «b».
        var cotizacion = new QuoteView(Money.Of(500m, "COP"), Money.Zero("COP"), Money.Zero("COP"), Money.Of(500m, "COP"),
            [(b, Money.Of(300m, "COP")), (a, Money.Of(100m, "COP"))]);

        var salida = await new PasoPreciosPorLinea().EjecutarAsync(
            Entrada("pricing.precios-por-linea", new FlowContext().Set("l", lineas).Set("c", cotizacion), "l", "c"), CancellationToken.None);

        var pedido = Assert.IsAssignableFrom<IReadOnlyList<LineaDePedido>>(Assert.Single(salida.Valores));
        Assert.Equal(new[] { new LineaDePedido(a, 2, Money.Of(100m, "COP")), new LineaDePedido(b, 1, Money.Of(300m, "COP")) }, pedido);
    }

    [Fact]
    public async Task Una_linea_sin_precio_se_rechaza_y_no_se_rellena()
    {
        var a = Ref.Create("prueba.producto", "a");
        IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas = [(a, 1)];
        var sinLineas = new QuoteView(Money.Of(100m, "COP"), Money.Zero("COP"), Money.Zero("COP"), Money.Of(100m, "COP"));

        var salida = await new PasoPreciosPorLinea().EjecutarAsync(
            Entrada("pricing.precios-por-linea", new FlowContext().Set("l", lineas).Set("c", sinLineas), "l", "c"), CancellationToken.None);

        Assert.Equal(PasoPreciosPorLinea.SinPrecioDeLinea, salida.Rechazo?.Code);
        Assert.Empty(sinLineas.Lineas);
    }

    [Fact]
    public async Task El_adaptador_trae_el_precio_de_cada_linea_si_Pricing_lo_da()
    {
        const string ConLineas = """
            {"subtotal":{"amount":500,"currency":"COP"},"tax":{"amount":0,"currency":"COP"},"total":{"amount":500,"currency":"COP"},
             "lines":[{"subjectKind":"prueba.producto","subjectId":"b","quantity":1,"unitPrice":{"amount":300,"currency":"COP"}}]}
            """;
        IPricingPort conPrecios = new Synergos.Bff.Pasos.Http.PuertosHttp(new Contesta(ConLineas));
        IPricingPort sinPrecios = new Synergos.Bff.Pasos.Http.PuertosHttp(new Contesta(
            """{"subtotal":{"amount":0,"currency":"COP"},"tax":{"amount":0,"currency":"COP"},"total":{"amount":0,"currency":"COP"}}"""));
        IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas = [(Ref.Create("prueba.producto", "b"), 1)];

        var con = await conPrecios.CotizarAsync(lineas, null, CancellationToken.None);
        var sin = await sinPrecios.CotizarAsync(lineas, null, CancellationToken.None);

        Assert.Equal((Ref.Create("prueba.producto", "b"), Money.Of(300m, "COP")), Assert.Single(con.Value.Lineas));
        Assert.Empty(sin.Value.Lineas);
    }

    /// <summary>Un <see cref="IHttpClientFactory"/> que contesta siempre lo mismo.</summary>
    private sealed class Contesta(string json) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Cable(json)) { BaseAddress = new Uri("http://capacidad.local/") };

        private sealed class Cable(string json) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
                => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                });
        }
    }

    [Fact]
    public async Task Una_linea_es_el_sujeto_con_cantidad_uno()
    {
        var servicio = Ref.Create("prueba.servicio", "consulta");

        var salida = await new PasoUnaLinea().EjecutarAsync(Entrada("forma.una-linea", new FlowContext().Set("s", servicio), "s"), CancellationToken.None);

        var lineas = Assert.IsAssignableFrom<IReadOnlyList<(Ref Sujeto, int Cantidad)>>(Assert.Single(salida.Valores));
        Assert.Equal((servicio, 1), Assert.Single(lineas));
    }

    [Fact]
    public async Task El_desglose_escribe_subtotal_descuento_impuesto_y_total_en_ese_orden()
    {
        var c = new QuoteView(Money.Of(100m, "COP"), Money.Of(10m, "COP"), Money.Of(19m, "COP"), Money.Of(109m, "COP"));

        var salida = await new PasoDesglose().EjecutarAsync(Entrada("pricing.desglose", new FlowContext().Set("c", c), "c"), CancellationToken.None);

        Assert.Equal(new object?[] { c.Subtotal, c.Descuento, c.Impuesto, c.Total }, salida.Valores);
    }

    [Fact]
    public async Task Sin_codigo_cotiza_sin_promocion_y_o_cero_solo_es_cero_si_no_hay_precio()
    {
        var precios = new Precios();
        var lineas = new FlowContext().Set("l", (IReadOnlyList<(Ref, int)>)[(Ref.Create("prueba.servicio", "x"), 2)]);

        var sinCodigo = await new PasoCotizacionSinCodigo(precios).EjecutarAsync(Entrada("pricing.cotizar-sin-codigo", lineas, "l"), CancellationToken.None);
        Assert.Equal(Money.Of(2000m, "COP"), ((QuoteView)sinCodigo.Valores[0]!).Total);
        Assert.Null(Assert.Single(precios.Promociones));

        precios.Rechaza = Rejection.NotFound(PasoCotizacionOCero.SinPrecio, "sin precio");
        var cero = await new PasoCotizacionOCero(precios).EjecutarAsync(Entrada("pricing.cotizar-o-cero", lineas, "l"), CancellationToken.None);
        Assert.True(((QuoteView)cero.Valores[0]!).Total.IsZero);

        precios.Rechaza = Rejection.Unavailable("pricing.unreachable", "caída");
        var caida = await new PasoCotizacionOCero(precios).EjecutarAsync(Entrada("pricing.cotizar-o-cero", lineas, "l"), CancellationToken.None);
        Assert.Equal("pricing.unreachable", caida.Rechazo?.Code);
    }

    // ── La carga de flujos ──────────────────────────────────────────────────

    private const string Uno = """{ "clave": "prueba.uno", "entrada": [], "fases": { "abrir": ["a"] }, "pasos": { "a": { "tipo": "x.a" } } }""";

    private static string Carpeta(params (string Fichero, string Contenido)[] ficheros)
    {
        var dir = Path.Combine(Path.GetTempPath(), "flujos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        foreach (var (f, c) in ficheros) File.WriteAllText(Path.Combine(dir, f), c);
        return dir;
    }

    [Fact]
    public void Una_clave_en_dos_ficheros_no_arranca_y_nombra_las_dos_fuentes()
    {
        var dir = Carpeta(("uno.json", Uno), ("otro.json", Uno));
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => FlujosDeclarados.Cargar(typeof(PasosDeFormaYCargaTests).Assembly, dir));

            Assert.Contains("«prueba.uno»", ex.Message, StringComparison.Ordinal);
            Assert.Contains("uno.json", ex.Message, StringComparison.Ordinal);
            Assert.Contains("otro.json", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Una_carpeta_configurada_que_no_existe_no_arranca_y_una_definicion_rota_dice_de_donde_es()
    {
        Assert.Throws<InvalidOperationException>(() =>
            FlujosDeclarados.Cargar(typeof(PasosDeFormaYCargaTests).Assembly, Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid().ToString("N"))));

        var dir = Carpeta(("roto.json", "{ \"clave\": \"prueba.roto\" }"));
        try
        {
            var ex = Assert.Throws<FormatException>(() => FlujosDeclarados.Cargar(typeof(PasosDeFormaYCargaTests).Assembly, dir));
            Assert.Contains("roto.json", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Sin_carpeta_se_cargan_los_incrustados_y_con_ella_los_suyos()
    {
        var eventos = FlujosDeclarados.Cargar(typeof(Synergos.Bff.Eventos.Domain.TicketingFlow).Assembly, null);
        Assert.Equal("eventos.compra", Assert.Single(eventos).Flujo.Clave);

        var dir = Carpeta(("uno.json", Uno));
        try
        {
            Assert.Equal("prueba.uno", Assert.Single(FlujosDeclarados.Cargar(typeof(PasosDeFormaYCargaTests).Assembly, dir)).Flujo.Clave);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
