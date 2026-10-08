using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.CMS.Tests.Architecture;   // Proyectos: la raíz del repo, resuelta del disco (#136)
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El paso <c>notifications.avisar</c> (ADR 0140 F3): un aviso por saga, sólo con dirección, y lo que
/// la capacidad rechaza vuelve tal cual.
/// </summary>
/// <remarks>
/// <para><b>Contra <c>Api.Notifications</c> DE VERDAD</b>, con la carpeta de recogida: una entrega es
/// una fila en su rastro Y un <c>.eml</c> en la carpeta. Un doble que contesta 201 a lo que sea no
/// vería que repetir el paso manda dos correos.</para>
///
/// <para>El puerto lo cumple acá un cliente mínimo sobre la capacidad; en el orquestador lo cumple el
/// suyo (Eventos, en el paso 7). Lo que se prueba es el PASO: qué llave usa, cuándo no manda y qué
/// devuelve.</para>
/// </remarks>
public sealed class AvisarTests : IDisposable
{
    private const string Plantilla = "eventos.entradas.confirmadas";

    private readonly string _buzon = Path.Combine(Path.GetTempPath(), "avisar-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_buzon)) Directory.Delete(_buzon, recursive: true); }
        catch (IOException) { /* el host puede tener un fichero aún abierto; es un temporal */ }
    }

    /// <summary>El puerto, sobre la capacidad real: lo que el orquestador hará con su cliente.</summary>
    private sealed class PuertoReal(IHttpClientFactory clientes) : CapabilityClients(clientes), INotificationsPort
    {
        private sealed record Entrega(string Id, string Status);

        public async Task<Result<string>> AvisarAsync(
            Ref destinatario, string direccion, string plantilla, IReadOnlyDictionary<string, string> valores,
            IdempotencyKey llave, CancellationToken ct)
            => (await Post<Entrega>("notifications", "v1/deliveries", new
            {
                toKind = destinatario.Kind,
                toId = destinatario.Id,
                address = direccion,
                templateKey = plantilla,
                values = valores,
            }, llave, ct)).Map(e => e.Id);
    }

    /// <summary>Cuenta los intentos y contesta lo que le digan.</summary>
    private sealed class PuertoQueCuenta(Result<string>? respuesta = null) : INotificationsPort
    {
        public List<(string Direccion, IdempotencyKey Llave, IReadOnlyDictionary<string, string> Valores)> Intentos { get; } = new();

        public Task<Result<string>> AvisarAsync(
            Ref destinatario, string direccion, string plantilla, IReadOnlyDictionary<string, string> valores,
            IdempotencyKey llave, CancellationToken ct)
        {
            Intentos.Add((direccion, llave, valores));
            return Task.FromResult(respuesta ?? Result.Ok("entrega-1"));
        }
    }

    private static readonly Ref Comprador = Ref.Create("eventos.comprador", "m1");

    private static readonly Contacto Ana = new("ana@ejemplo.co", "Ana María", "https://sitio.ejemplo/entradas?compra=s-1", "Teatro de prueba");

    private static readonly PasoDef Definicion = new(
        "avisar", "notifications.avisar", new[] { "comprador", "contacto", "total" }, Array.Empty<string>(),
        "avisar", null, null, null, null, PasoDef.Seguir, null, Plantilla);

    private static Task<SalidaDePaso> Avisar(IPaso paso, string saga, Contacto? contacto)
        => paso.EjecutarAsync(new EntradaDePaso(saga, Ref.Create("eventos.compra", saga), Definicion,
            new FlowContext().Set("comprador", Comprador).Set("contacto", contacto).Set("total", Money.Of(403_200m, "COP")),
            null, null), CancellationToken.None);

    private ArnesDeCapacidades NotificacionesConRecogida()
        => new ArnesDeCapacidades().Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>(
            "notifications", ("Notifications:Pickup:Directory", _buzon));

    /// <summary>Publica la plantilla tal como la declara <c>tools/provisionar.plantillas.json</c>.</summary>
    private static async Task Aprovisionar(ArnesDeCapacidades arnes)
    {
        using var fichero = JsonDocument.Parse(File.ReadAllText(Path.Combine(Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        var p = fichero.RootElement.EnumerateArray().Single(e => e.TryGetProperty("key", out var k) && k.GetString() == Plantilla);

        using var http = arnes.CreateClient("notifications");
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/templates")
        {
            Content = JsonContent.Create(new
            {
                key = Plantilla,
                channel = p.GetProperty("channel").GetString(),
                subject = p.GetProperty("subject").GetString(),
                body = p.GetProperty("body").GetString(),
            }),
        };
        peticion.Headers.Add("Idempotency-Key", $"prueba:{Plantilla}");
        using var r = await http.SendAsync(peticion);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    private static async Task<List<JsonElement>> Entregas(ArnesDeCapacidades arnes)
    {
        using var http = arnes.CreateClient("notifications");
        var pagina = await http.GetFromJsonAsync<JsonElement>($"v1/deliveries?toKind={Comprador.Kind}&toId={Comprador.Id}");
        return pagina.GetProperty("items").EnumerateArray().ToList();
    }

    [Fact]
    public async Task Contra_Api_Notifications_real_un_aviso_es_UNA_entrega_aunque_el_paso_se_repita()
    {
        using var arnes = NotificacionesConRecogida();
        await Aprovisionar(arnes);
        var paso = new PasoAvisar(new PuertoReal(arnes));

        Assert.Equal(PasoResultado.Continuar, (await Avisar(paso, "s-1", Ana)).Control);
        Assert.Equal(PasoResultado.Continuar, (await Avisar(paso, "s-1", Ana)).Control);

        var entrega = Assert.Single(await Entregas(arnes));
        Assert.Equal("ana@ejemplo.co", entrega.GetProperty("address").GetString());
        Assert.Equal("Accepted", entrega.GetProperty("status").GetString());
        Assert.Single(Directory.GetFiles(_buzon, "*.eml"));

        // Otra saga es otro aviso: la llave es por saga, no global.
        await Avisar(paso, "s-2", Ana);
        Assert.Equal(2, (await Entregas(arnes)).Count);
    }

    [Fact]
    public async Task Sin_contacto_o_sin_correo_no_avisa_ni_lo_intenta()
    {
        var puerto = new PuertoQueCuenta();
        var paso = new PasoAvisar(puerto);

        Assert.Equal(PasoResultado.Continuar, (await Avisar(paso, "s-1", null)).Control);
        Assert.Equal(PasoResultado.Continuar, (await Avisar(paso, "s-1", Ana with { Correo = " " })).Control);

        Assert.Empty(puerto.Intentos);
    }

    [Fact]
    public async Task Lo_que_rechaza_la_capacidad_vuelve_tal_cual_y_decide_la_definicion()
    {
        // Sin la plantilla aprovisionada, la capacidad real contesta template_not_found. El paso no
        // lo traga ni lo traduce: que deshaga o no la saga lo decide «al_fallar».
        using var arnes = NotificacionesConRecogida();
        var paso = new PasoAvisar(new PuertoReal(arnes));

        var salida = await Avisar(paso, "s-1", Ana);

        Assert.Equal(PasoResultado.Abortar, salida.Control);
        Assert.Equal("notifications.template_not_found", salida.Rechazo!.Code);
    }

    [Fact]
    public async Task La_llave_es_fija_por_saga_y_los_valores_son_los_del_paso()
    {
        var puerto = new PuertoQueCuenta();
        var paso = new PasoAvisar(puerto);

        await Avisar(paso, "pta-0123456789abcdef0123456789abcdef01234567", Ana);

        var (direccion, llave, valores) = Assert.Single(puerto.Intentos);
        Assert.Equal("ana@ejemplo.co", direccion);
        Assert.Equal(IdempotencyKey.From("pta-0123456789abcdef0123456789abcdef01234567", "avisar").Value, llave.Value);
        Assert.Equal(new Dictionary<string, string>
        {
            ["nombre"] = "Ana María",
            ["numero"] = "01234567",
            ["total"] = "403.200 COP",
            ["enlace"] = "https://sitio.ejemplo/entradas?compra=s-1",
            ["sitio"] = "Teatro de prueba",
        }, valores);
        Assert.Equal("12.345,5 COP", PasoAvisar.Valores("s", Ana, Money.Of(12_345.5m, "COP"))["total"]);
    }
}
