using System.Net;
using System.Text.Json;
using Synergos.Api.Notifications.Domain;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.CMS.Tests.Architecture;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// El aviso del viaje es un paso del flujo (línea 006, historia 7, T023; ADR 0140): UN correo al confirmar, con el número
/// que vio quien viaja, y un aviso caído no deshace lo que ya se cobró y se reservó.
/// </summary>
/// <remarks>
/// Contra <c>Api.Notifications</c> real del arnés, con la plantilla del fichero que la provisiona de verdad
/// (<c>tools/provisionar.plantillas.json</c>): lo que se mira es la entrega que quedó registrada. La plantilla se cruza
/// además con lo que el paso MANDA, en los dos sentidos, con la regla de la capacidad.
/// </remarks>
public sealed class AvisoDelViajeTests
{
    [Fact]
    public async Task Al_confirmar_sale_un_correo_con_el_numero_que_vio_quien_viaja()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (_, abierta) = await r.Abrir();

        var (estado, cerrada) = await r.Cerrar(abierta.GetProperty("id").GetString()!);

        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        var entrega = Assert.Single(await r.Avisos());
        Assert.Equal(ViajesReservaReal.PlantillaDelAviso, entrega.GetProperty("templateKey").GetString());
        Assert.Equal($"Tu viaje {cerrada.GetProperty("numero").GetString()} está confirmado", entrega.GetProperty("subject").GetString());
        Assert.Equal("ana@ejemplo.co", entrega.GetProperty("address").GetString());
    }

    [Fact]
    public async Task Con_el_aviso_caido_el_viaje_queda_completado_y_no_se_devuelve_nada()
    {
        using var r = await new ViajesReservaReal(falla: m =>
            m.Method == HttpMethod.Post && m.RequestUri!.AbsolutePath == "/v1/deliveries" ? "notifications.unavailable" : null).ConLosTres();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cerrada) = await r.Cerrar(id);

        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal(SagaStatus.Completed, r.Sagas.Find(id)!.Status);
        Assert.All(await r.Reservas(), x => Assert.Equal("Confirmed", x.Estado));
        var cobro = await r.Leer("payments", $"v1/payments/{r.Dato(id, "cobroId")}");
        Assert.Equal("Captured", cobro.GetProperty("status").GetString());
        Assert.Equal(0m, cobro.GetProperty("refunded").GetProperty("amount").GetDecimal());
        Assert.Empty(await r.Avisos());
    }

    [Fact]
    public async Task Confirmar_dos_veces_no_manda_un_segundo_correo()
    {
        using var r = await new ViajesReservaReal().ConLosTres();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        await r.Cerrar(id);
        var (estado, _) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Single(await r.Avisos());
    }

    /// <summary>
    /// La plantilla usa justo los valores que el paso <c>notifications.avisar</c> manda al final de <c>cerrar</c>: ni uno
    /// que no mande —el itinerario no va en el correo— ni uno que mande y no se lea.
    /// </summary>
    [Fact]
    public async Task La_plantilla_usa_justo_los_valores_que_manda_el_paso()
    {
        var puerto = new PuertoQueApunta();
        var definicion = FlujosDeclarados.Incrustado(typeof(Synergos.Bff.Viajes.Contracts.TripResponse).Assembly, "flujos/viajes.reserva.json")
            .Pasos["avisar"];
        var contexto = new FlowContext()
            .Set("viajero", Ref.Create("viajes.viajero", "u-1"))
            .Set("contacto", new Contacto("ana@ejemplo.co", "Ana", "https://sitio/viajes/?viaje=v-1", "Viajes de prueba"))
            .Set("total", Money.Of(1_200_000m, "COP"));

        await new PasoAvisar(puerto).EjecutarAsync(
            new EntradaDePaso("v-1", Ref.Create("viajes.reserva", "v-1"), definicion, contexto, null, null), CancellationToken.None);

        Assert.Equal(ViajesReservaReal.PlantillaDelAviso, puerto.Plantilla);
        var plantilla = Declarada(ViajesReservaReal.PlantillaDelAviso);
        var relleno = NotificationRules.Fill(plantilla, puerto.Valores!);
        Assert.True(relleno.IsOk, $"La plantilla no se rellena con lo que manda el paso: {relleno.Rejection?.Message}");
        var sinLeer = puerto.Valores!.Keys
            .Where(k => !plantilla.Subject.Contains("{" + k + "}", StringComparison.Ordinal)
                        && !plantilla.Body.Contains("{" + k + "}", StringComparison.Ordinal))
            .ToList();
        Assert.True(sinLeer.Count == 0, $"El paso manda {string.Join(", ", sinLeer.Select(k => "{" + k + "}"))} y la plantilla no lo usa.");
        Assert.Contains("https://sitio/viajes/?viaje=v-1", relleno.Value.Body, StringComparison.Ordinal);
    }

    /// <summary>La plantilla del fichero como la guarda la capacidad (el canal decide si el cuerpo es HTML).</summary>
    private static Template Declarada(string clave)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        var p = doc.RootElement.EnumerateArray().Single(e => e.TryGetProperty("key", out var k) && k.GetString() == clave);
        Assert.True(Enum.TryParse<Channel>(p.GetProperty("channel").GetString(), ignoreCase: true, out var canal));
        return new Template("provisionada", clave, canal, p.GetProperty("subject").GetString()!, p.GetProperty("body").GetString()!);
    }

    /// <summary>Apunta lo que el paso de aviso manda: la clave de la plantilla y los valores.</summary>
    private sealed class PuertoQueApunta : INotificationsPort
    {
        public string? Plantilla { get; private set; }
        public IReadOnlyDictionary<string, string>? Valores { get; private set; }

        public Task<Result<string>> AvisarAsync(
            Ref destinatario, string direccion, string plantilla, IReadOnlyDictionary<string, string> valores,
            IdempotencyKey llave, CancellationToken ct)
        {
            Plantilla = plantilla;
            Valores = valores;
            return Task.FromResult(Result.Ok("d1"));
        }
    }
}
