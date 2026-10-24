using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Synergos.Bff.Alquiler.Contracts;
using Synergos.Bff.Core.Flow;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El orquestador de alquiler con la renta declarada (línea 005, T010): arranca validando
/// <c>flujos/alquiler.renta.json</c> contra sus pasos, una copia rota no arranca, y la llave de cada
/// capacidad sale una sola vez aunque la configuren las dos máquinas de saga (NN-17). Y su interruptor
/// <c>alquiler:Flujos:Modo</c> en los dos sentidos (FR-029, T045): con «imperativo» queda sólo la ruta directa.
/// </summary>
public sealed class ArranqueDeLaRentaTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "arranque-renta-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* un temporal */ }
    }

    /// <summary>
    /// Anota lo que el validador del núcleo dice del catálogo, desde DENTRO del host: cuando el arranque
    /// falla, lo que sube es una carrera entre la excepción y el host desechado (ver
    /// <c>FlowValidatorTests.Testigo</c>), y lo anotado no depende de quién gane.
    /// </summary>
    private sealed class Testigo
    {
        public ValidateOptionsResult? Visto { get; set; }
    }

    private sealed class ValidadorTestigo(IRegistroDePasos registro, Testigo testigo) : IValidateOptions<FlowCatalog>
    {
        public ValidateOptionsResult Validate(string? name, FlowCatalog options)
        {
            testigo.Visto = new FlowDefinitionValidator(registro).Validate(name, options);
            return ValidateOptionsResult.Skip;
        }
    }

    private WebApplicationFactory<RentalResponse> Orquestador(Testigo testigo, params (string Clave, string Valor)[] ajustes)
        => new WebApplicationFactory<RentalResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("alquiler:Storage:Root", Path.Combine(_raiz, "datos"));
            b.UseSetting("Alquiler:ApiKey", "llave-de-prueba");
            foreach (var (clave, valor) in ajustes) b.UseSetting(clave, valor);
            b.ConfigureTestServices(s => s.AddSingleton<IValidateOptions<FlowCatalog>>(
                sp => new ValidadorTestigo(sp.GetRequiredService<IRegistroDePasos>(), testigo)));
        });

    /// <summary>El flujo tal como viaja: incrustado en el ensamblado del orquestador.</summary>
    private static JsonObject LaRenta()
    {
        using var recurso = typeof(RentalResponse).Assembly.GetManifestResourceStream("flujos/alquiler.renta.json")
            ?? throw new InvalidOperationException("Bff.Alquiler no incrusta flujos/alquiler.renta.json.");
        return JsonNode.Parse(recurso)!.AsObject();
    }

    [Fact]
    public async Task Arranca_con_la_renta_declarada_y_la_valida_al_arrancar()
    {
        var testigo = new Testigo();
        using var fabrica = Orquestador(testigo);
        using var cliente = fabrica.CreateClient();

        using var salud = await cliente.GetAsync(new Uri("/health", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, salud.StatusCode);
        Assert.True(testigo.Visto is { Succeeded: true }, $"El catálogo no pasó: {testigo.Visto?.FailureMessage}");

        // La renta y su cancelación de lo confirmado, que es una operación posterior a ella (NN-8, D-005-NN8).
        var flujos = fabrica.Services.GetServices<FlujoGenerico>().ToDictionary(f => f.Definicion.Clave);
        Assert.Equal(["alquiler.cancelacion", "alquiler.renta"], flujos.Keys.Order(StringComparer.Ordinal));
        Assert.Null(flujos["alquiler.renta"].Definicion.PosteriorA);
        Assert.Equal("alquiler.renta", flujos["alquiler.cancelacion"].Definicion.PosteriorA);
    }

    [Fact]
    public void La_cancelacion_lee_lo_que_la_renta_deja_y_sus_unidades_y_la_garantia_las_mueven_los_pasos_comunes()
    {
        // El registro del orquestador reemplaza al del núcleo: sin nombrarlo, flow.leer-original no estaría. Desde el
        // bloque 7c bis los dos pasos provisionales de T044 se fueron (NN-18, NN-19): queda la regla del día de inicio.
        using var fabrica = Orquestador(new Testigo());
        var registro = fabrica.Services.GetRequiredService<IRegistroDePasos>();
        Assert.True(registro.Para(PasoLeerOriginal.Nombre) is { LeeLaOriginal: true });
        Assert.Equal(
            ["alquiler.antes-del-inicio", "alquiler.tramo"],
            registro.Tipos.Where(t => t.StartsWith("alquiler.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));

        var deja = LaRenta()["para_lo_posterior"]!.AsArray().Select(n => (string?)n).ToList();
        Assert.Equal(["cobroId", "total", "ventana", "garantiaId", "reservas:apartar"], deja);

        var cancelacion = fabrica.Services.GetServices<FlujoGenerico>().Single(f => f.Definicion.Clave == "alquiler.cancelacion").Definicion;
        Assert.Equal("booking.cancelar", cancelacion.Pasos["cancelar"].Tipo);
        Assert.Equal("payments.anular", cancelacion.Pasos["garantia"].Tipo);
        Assert.Equal("garantiaId", cancelacion.Pasos["garantia"].OmitirSiFalta);
    }

    [Fact]
    public void Una_copia_con_una_reserva_sin_cierre_no_arranca_y_dice_cual()
    {
        // La renta con otra clave y sin «conservar»: la garantía quedaría retenida sin que nadie la cerrara.
        var rota = LaRenta();
        rota["clave"] = "alquiler.renta-rota";
        rota["pasos"]!.AsObject().Remove("conservar");
        var cerrar = rota["fases"]!["cerrar"]!.AsArray();
        cerrar.Remove(cerrar.First(n => n is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String && (string?)v == "conservar"));

        var carpeta = Path.Combine(_raiz, "flujos");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "alquiler.renta-rota.json"), rota.ToJsonString());

        var testigo = new Testigo();
        using var fabrica = Orquestador(testigo, ("alquiler:Flujos:Carpeta", carpeta));

        var ex = Assert.ThrowsAny<Exception>(() => fabrica.CreateClient());

        // Lo dice el núcleo por una de dos puertas: el intérprete, que valida al construirse cuando se mapean
        // las rutas, o la validación del catálogo al arrancar. Las dos nombran el flujo y la reserva.
        var dicho = (testigo.Visto?.Failures ?? []).Concat(Mensajes(ex)).ToList();
        Assert.Contains(dicho, m => m.Contains("«alquiler.renta-rota»", StringComparison.Ordinal)
            && m.Contains("«retener»", StringComparison.Ordinal));
    }

    [Fact]
    public void La_garantia_la_conserva_el_paso_comun_del_nucleo()
    {
        // NN-4 (lote 6, P7 = A): la reserva de la garantía la cierra payments.conservar, que el intérprete trata como un
        // cierre que conserva, y ya no una salida propia que tenía que ir al final de «cerrar».
        var conservar = LaRenta()["pasos"]!["conservar"]!;
        Assert.Equal("payments.conservar", (string?)conservar["tipo"]);
        Assert.Equal("retener", (string?)conservar["cierra_reserva"]);

        using var fabrica = Orquestador(new Testigo());
        var registro = fabrica.Services.GetRequiredService<IRegistroDePasos>();
        Assert.True(registro.Para("payments.conservar") is { Conserva: true });
        Assert.DoesNotContain(registro.Tipos, t => t.StartsWith("alquiler.conservar", StringComparison.Ordinal));
    }

    private static IEnumerable<string> Mensajes(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException) yield return ex.Message;
    }

    // ── El interruptor del orquestador (FR-029, T045) ───────────────────────

    [Fact]
    public async Task Con_el_interruptor_en_imperativo_alquila_por_la_ruta_directa_y_no_expone_los_flujos()
    {
        // El flujo imperativo se conserva detrás de alquiler:Flujos:Modo. Con «imperativo» arranca sin el host
        // declarado: la ruta directa alquila contra las capacidades reales, y ni la renta, ni su cancelación posterior,
        // ni la liquidación existen.
        using var r = new RentaDeAlquilerReal(ajustes: new Dictionary<string, string> { ["alquiler:Flujos:Modo"] = "imperativo" });
        Assert.Equal(HttpStatusCode.OK, (await r.PublicarOferta()).Estado);

        var (estado, cuerpo) = await PorLaDirecta(r);
        Assert.True(estado == HttpStatusCode.Created, $"{(int)estado} {cuerpo}");
        Assert.Equal(2, await r.Tomadas());

        Assert.Empty(r.Servicios.GetServices<FlujoGenerico>());
        Assert.Equal(HttpStatusCode.NotFound, (await r.Cotizar()).Estado);
        Assert.Equal(HttpStatusCode.NotFound, (await r.Abrir("alq-puerta")).Estado);
        Assert.Equal(HttpStatusCode.NotFound, (await r.CancelarPorElFlujo("directa-1")).Estado);
        Assert.Equal(HttpStatusCode.NotFound, (await r.Declarar("directa-1")).Estado);
    }

    [Fact]
    public async Task Con_el_valor_por_defecto_estan_la_ruta_directa_y_los_flujos()
    {
        using var r = new RentaDeAlquilerReal();
        Assert.Equal(HttpStatusCode.OK, (await r.PublicarOferta()).Estado);

        var (estado, cuerpo) = await PorLaDirecta(r);
        Assert.True(estado == HttpStatusCode.Created, $"{(int)estado} {cuerpo}");
        Assert.Equal(2, await r.Tomadas());

        Assert.Equal(["alquiler.cancelacion", "alquiler.renta"],
            r.Servicios.GetServices<FlujoGenerico>().Select(f => f.Definicion.Clave).Order(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await r.Cotizar(RentaDeAlquilerReal.Pedido(unidades: 1))).Estado);
    }

    [Fact]
    public void Una_palabra_que_no_conoce_en_el_interruptor_no_arranca()
    {
        using var fabrica = Orquestador(new Testigo(), ("alquiler:Flujos:Modo", "declarativo"));

        var ex = Assert.ThrowsAny<Exception>(() => fabrica.CreateClient());
        Assert.Contains(Mensajes(ex), m => m.Contains("«declarativo»", StringComparison.Ordinal)
            && m.Contains("imperativo", StringComparison.Ordinal));
    }

    /// <summary>Lo que manda la ruta directa del CMS con <c>Synergos:Alquiler:Mode=Bff</c>: el total y la garantía ya cotizados.</summary>
    private static async Task<(HttpStatusCode Estado, string Cuerpo)> PorLaDirecta(RentaDeAlquilerReal r)
    {
        var (inicio, fin) = RentaDeAlquilerReal.Ventana(7);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/rentals")
        {
            Content = JsonContent.Create(new
            {
                equipmentId = RentaDeAlquilerReal.Equipo,
                quantity = 2,
                start = inicio,
                end = fin,
                renterKind = "alquiler.arrendatario",
                renterId = "u-1",
                rentalTotal = new { amount = 420_000m, currency = "COP" },
                deposit = new { amount = 500_000m, currency = "COP" },
            }),
        };
        peticion.Headers.Add("Idempotency-Key", "directa-1");
        using var respuesta = await r.Orquestador.SendAsync(peticion);
        return (respuesta.StatusCode, await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    public void La_llave_de_cada_capacidad_sale_una_vez_aunque_la_configuren_las_dos_maquinas()
    {
        using var fabrica = Orquestador(new Testigo(),
            ("Alquiler:Capabilities:booking:ApiKey", "k-booking"), ("Alquiler:Capabilities:payments:ApiKey", "k-payments"));

        var clientes = fabrica.Services.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(["k-booking"], clientes.CreateClient("booking").DefaultRequestHeaders.GetValues(SharedKeyAuth.HeaderName));
        Assert.Equal(["k-payments"], clientes.CreateClient("payments").DefaultRequestHeaders.GetValues(SharedKeyAuth.HeaderName));
    }
}
