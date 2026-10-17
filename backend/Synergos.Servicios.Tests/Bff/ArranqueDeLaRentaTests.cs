using System.Net;
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
/// capacidad sale una sola vez aunque la configuren las dos máquinas de saga (NN-17).
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
    public void La_cancelacion_lee_la_original_con_el_paso_comun_y_nombra_sus_pasos_propios()
    {
        // El registro del orquestador reemplaza al del núcleo: sin nombrarlo, flow.leer-original no estaría. Y los dos
        // pasos de pegamento llevan su necesidad (NN-18, NN-19) hasta que el núcleo los cubra.
        using var fabrica = Orquestador(new Testigo());
        var registro = fabrica.Services.GetRequiredService<IRegistroDePasos>();
        Assert.True(registro.Para(PasoLeerOriginal.Nombre) is { LeeLaOriginal: true });
        Assert.Equal(
            ["alquiler.antes-del-inicio", "alquiler.anular-garantia", "alquiler.cancelar-unidades", "alquiler.tramo"],
            registro.Tipos.Where(t => t.StartsWith("alquiler.", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
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
