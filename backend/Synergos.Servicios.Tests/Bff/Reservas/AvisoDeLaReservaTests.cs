using System.Net;
using System.Text.Json;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff.Reservas;

/// <summary>
/// El aviso de la reserva es un paso del flujo (011-booking-reserva, T023; ADR 0140): UN correo al confirmar, con
/// el número que vio quien reservó, y un aviso caído no deshace lo que ya se cobró y se confirmó.
/// </summary>
/// <remarks>
/// Contra <c>Api.Notifications</c> real del arnés, con la plantilla del fichero que la provisiona de verdad
/// (<c>tools/provisionar.plantillas.json</c>): lo que se mira es la entrega que quedó registrada.
/// </remarks>
public sealed class AvisoDeLaReservaTests
{
    private static async Task<List<JsonElement>> Entregas(ReservaDeServicioReal r)
        => (await r.Leer("notifications", "v1/deliveries?toKind=reservas.cliente&toId=u-1")).GetProperty("items").EnumerateArray().ToList();

    [Fact]
    public async Task Al_confirmar_sale_un_correo_con_el_numero_que_vio_quien_reservo()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();
        var (_, abierta) = await r.Abrir();

        var (estado, cerrada) = await r.Cerrar(abierta.GetProperty("id").GetString()!);

        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        var entrega = Assert.Single(await Entregas(r));
        Assert.Equal("reservas.servicio.confirmada", entrega.GetProperty("templateKey").GetString());
        Assert.Equal($"Tu reserva {cerrada.GetProperty("numero").GetString()} está confirmada", entrega.GetProperty("subject").GetString());
        Assert.Equal("ana@ejemplo.co", entrega.GetProperty("address").GetString());
    }

    [Fact]
    public async Task Con_el_aviso_caido_la_reserva_queda_completada_y_no_se_devuelve_nada()
    {
        using var r = await new ReservaDeServicioReal(falla: p => ReservaDeServicioReal.Es(p, "POST", "^/v1/deliveries$") ? "notifications.unavailable" : null)
            .ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cerrada) = await r.Cerrar(id);

        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal(SagaStatus.Completed, r.Sagas.Find(id)!.Status);
        var cobro = await r.Leer("payments", $"v1/payments/{r.Dato(id, "cobroId")}");
        Assert.Equal("Captured", cobro.GetProperty("status").GetString());
        Assert.Equal(0m, cobro.GetProperty("refunded").GetProperty("amount").GetDecimal());
        Assert.Empty(await Entregas(r));
    }

    [Fact]
    public async Task Confirmar_dos_veces_no_manda_un_segundo_correo()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        await r.Cerrar(id);
        var (estado, _) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Single(await Entregas(r));
    }
}
