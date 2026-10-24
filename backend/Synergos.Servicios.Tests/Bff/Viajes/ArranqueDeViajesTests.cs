using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Viajes.Contracts;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// El orquestador de Viajes con la reserva declarada (línea 006, T007–T008): arranca validando
/// <c>flujos/viajes.reserva.json</c> contra sus pasos, una copia rota no arranca, y el modo decide si hay puerta sin tocar
/// <c>/v1/trips</c>, que es del CMS.
/// </summary>
public sealed class ArranqueDeViajesTests : IDisposable
{
    private const string Llave = "llave-de-prueba";
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "arranque-viajes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* un temporal */ }
    }

    /// <summary>Anota lo que el validador del núcleo dice del catálogo, desde DENTRO del host (ver <c>ArranqueDeLaRentaTests</c>).</summary>
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

    private WebApplicationFactory<TripResponse> Orquestador(Testigo? testigo, params (string Clave, string Valor)[] ajustes)
        => new WebApplicationFactory<TripResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("viajes:Storage:Root", Path.Combine(_raiz, "datos"));
            b.UseSetting("Viajes:ApiKey", Llave);
            foreach (var (clave, valor) in ajustes) b.UseSetting(clave, valor);
            if (testigo is not null)
            {
                b.ConfigureTestServices(s => s.AddSingleton<IValidateOptions<FlowCatalog>>(
                    sp => new ValidadorTestigo(sp.GetRequiredService<IRegistroDePasos>(), testigo)));
            }
        });

    private static HttpClient Cliente(WebApplicationFactory<TripResponse> fabrica)
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add(SharedKeyAuth.HeaderName, Llave);
        return cliente;
    }

    /// <summary>El flujo tal como viaja: incrustado en el ensamblado del orquestador.</summary>
    private static JsonObject LaReserva()
    {
        using var recurso = typeof(TripResponse).Assembly.GetManifestResourceStream("flujos/viajes.reserva.json")
            ?? throw new InvalidOperationException("Bff.Viajes no incrusta flujos/viajes.reserva.json.");
        return JsonNode.Parse(recurso)!.AsObject();
    }

    private static async Task<string?> Codigo(HttpResponseMessage r)
    {
        var texto = await r.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(texto)) return null;
        using var doc = JsonDocument.Parse(texto);
        return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    [Fact]
    public async Task Arranca_con_la_reserva_declarada_y_la_valida_al_arrancar()
    {
        var testigo = new Testigo();
        using var fabrica = Orquestador(testigo);
        using var cliente = Cliente(fabrica);

        using var salud = await cliente.GetAsync(new Uri("/health", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, salud.StatusCode);
        Assert.True(testigo.Visto is { Succeeded: true }, $"El catálogo no pasó: {testigo.Visto?.FailureMessage}");

        // La reserva y sus dos posteriores del personal (T026, T027), cada una sobre la original.
        var flujos = fabrica.Services.GetServices<FlujoGenerico>().ToDictionary(f => f.Definicion.Clave, StringComparer.Ordinal);
        Assert.Equal(new[] { "viajes.cancelacion", "viajes.devolucion", "viajes.reserva" }, flujos.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.All(new[] { "viajes.cancelacion", "viajes.devolucion" }, clave =>
        {
            Assert.Equal("viajes.reserva", flujos[clave].Definicion.PosteriorA);
            Assert.True(flujos[clave].Definicion.EsDePersonal, $"{clave} es de quien vende: acceso del personal.");
        });

        // Cada máquina en su carpeta: la genérica en flujos/, TripSaga en la raíz con sus datos de siempre.
        Assert.Equal(OrquestadorDeclaradoCarpeta, fabrica.Services.GetRequiredService<CarpetaDeSagas<SagaDeFlujo>>().Nombre);
    }

    private const string OrquestadorDeclaradoCarpeta = Synergos.Bff.Pasos.OrquestadorDeclarado.CarpetaDeLosFlujos;

    [Fact]
    public void Una_copia_con_una_reserva_sin_cierre_no_arranca_y_dice_cual()
    {
        // La reserva con otra clave y sin «confirmar»: cada ítem quedaría apartado sin que nadie lo cerrara.
        var rota = LaReserva();
        rota["clave"] = "viajes.reserva-rota";
        rota["pasos"]!.AsObject().Remove("confirmar");
        var cerrar = rota["fases"]!["cerrar"]!.AsArray();
        cerrar.Remove(cerrar.First(n => n is JsonObject));

        var carpeta = Path.Combine(_raiz, "flujos");
        Directory.CreateDirectory(carpeta);
        File.WriteAllText(Path.Combine(carpeta, "viajes.reserva-rota.json"), rota.ToJsonString());

        var testigo = new Testigo();
        using var fabrica = Orquestador(testigo, ("viajes:Flujos:Carpeta", carpeta));

        var ex = Assert.ThrowsAny<Exception>(() => fabrica.CreateClient());

        var dicho = (testigo.Visto?.Failures ?? []).Concat(Mensajes(ex)).ToList();
        Assert.Contains(dicho, m => m.Contains("«viajes.reserva-rota»", StringComparison.Ordinal)
            && m.Contains("«apartar»", StringComparison.Ordinal));
    }

    private static IEnumerable<string> Mensajes(Exception? ex)
    {
        for (; ex is not null; ex = ex.InnerException) yield return ex.Message;
    }

    [Fact]
    public async Task Por_defecto_hay_puerta_y_v1_trips_sigue_igual()
    {
        using var fabrica = Orquestador(null);
        using var cliente = Cliente(fabrica);

        // La puerta: la ruta existe y, sin el sujeto que pone la puerta, lo dice ANTES de leer el cuerpo.
        using var abrir = await cliente.PostAsync(new Uri("/v1/flujos/viajes.reserva", UriKind.Relative),
            new StringContent("""{"items":[]}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, abrir.StatusCode);
        Assert.StartsWith("viajes.", await Codigo(abrir), StringComparison.Ordinal);

        // /v1/trips: la del CMS, con su código de siempre.
        using var viaje = await cliente.GetAsync(new Uri("/v1/trips/no-existe", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, viaje.StatusCode);
        Assert.Equal("viajes.trip_not_found", await Codigo(viaje));
    }

    [Fact]
    public async Task Con_el_modo_imperativo_no_hay_puerta_y_v1_trips_sigue_igual()
    {
        using var fabrica = Orquestador(null, ("viajes:Flujos:Modo", "imperativo"));
        using var cliente = Cliente(fabrica);

        Assert.Empty(fabrica.Services.GetServices<FlujoGenerico>());

        // Sin la ruta de la puerta, la respuesta es la de una ruta que no existe: sin cuerpo de rechazo.
        using var abrir = await cliente.PostAsync(new Uri("/v1/flujos/viajes.reserva", UriKind.Relative),
            new StringContent("""{"items":[]}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, abrir.StatusCode);
        Assert.Null(await Codigo(abrir));

        using var viaje = await cliente.GetAsync(new Uri("/v1/trips/no-existe", UriKind.Relative));
        Assert.Equal("viajes.trip_not_found", await Codigo(viaje));
    }

    [Fact]
    public void Una_palabra_que_no_es_un_modo_no_arranca()
    {
        using var fabrica = Orquestador(null, ("viajes:Flujos:Modo", "a-veces"));

        var ex = Assert.ThrowsAny<Exception>(() => fabrica.CreateClient());

        Assert.Contains(Mensajes(ex), m => m.Contains("a-veces", StringComparison.Ordinal));
    }
}
