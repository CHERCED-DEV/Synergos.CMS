using System.Net;
using System.Net.Http.Json;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff.Propiedades;

/// <summary>
/// Historia 8 de 008-propiedades-visita (008-P5, FR-025): quien agendó cancela su visita YA confirmada dentro del plazo
/// de aviso del inmueble, como una operación posterior al cierre —el flujo aparte <c>propiedades.visita-cancelacion</c>,
/// que lee de la visita original la reserva que ella dejó para lo posterior, sin tocarla (núcleo P6, T082)—. Contra la
/// agenda real del arnés.
/// </summary>
/// <remarks>
/// En UNA fase: lo posterior ya no se anticipa (núcleo T085, NR-8 de 008), así que <c>cotizar</c> no corre la
/// cancelación; el test de cotizar lo sigue vigilando.
/// </remarks>
public sealed class CancelarLaVisitaConfirmadaTests
{
    [Fact]
    public async Task El_dueno_la_cancela_y_la_franja_vuelve_a_ofrecerse()
    {
        using var r = new VisitaReal();
        var visita = await r.Confirmada();

        var (estado, cuerpo) = await r.CancelarConfirmada(visita);

        Assert.True(estado == HttpStatusCode.Created, cuerpo.ToString());
        Assert.Equal("Completed", cuerpo.GetProperty("estado").GetString());
        Assert.Equal(visita, cuerpo.GetProperty("datos").GetProperty("visita").GetString());
        Assert.Equal(["Cancelled"], await r.Reservas());
        Assert.Equal(0, await r.Ocupadas());
        Assert.Equal(HttpStatusCode.Created, (await r.Abrir(llave: "otra", quien: "beto")).Estado);
    }

    [Fact]
    public async Task La_visita_original_no_se_toca()
    {
        using var r = new VisitaReal();
        var visita = await r.Confirmada();

        await r.CancelarConfirmada(visita);

        var (_, original) = await r.Consultar(visita);
        Assert.Equal("Completed", original.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Cotizar_la_cancelacion_no_cancela_la_visita()
    {
        using var r = new VisitaReal();
        var visita = await r.Confirmada();

        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/propiedades.visita-cancelacion/cotizar")
        {
            Content = JsonContent.Create(new { visita }),
        };
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, VisitaReal.Sujeto("u-1"));
        using var respuesta = await r.Orquestador.SendAsync(peticion);

        Assert.Equal(["Confirmed"], await r.Reservas());
    }

    [Fact]
    public async Task La_misma_llave_cancela_una_sola_vez_y_contesta_lo_mismo()
    {
        using var r = new VisitaReal();
        var visita = await r.Confirmada();

        var (_, primera) = await r.CancelarConfirmada(visita);
        var (estado, segunda) = await r.CancelarConfirmada(visita);

        Assert.True(estado is HttpStatusCode.Created or HttpStatusCode.OK, segunda.ToString());
        Assert.Equal(primera.GetProperty("id").GetString(), segunda.GetProperty("id").GetString());
        Assert.Equal(["Cancelled"], await r.Reservas());
    }

    [Fact]
    public async Task Con_otra_llave_la_agenda_dice_que_ya_estaba_cancelada_y_no_cambia()
    {
        using var r = new VisitaReal();
        var visita = await r.Confirmada();
        await r.CancelarConfirmada(visita);

        var (estado, cuerpo) = await r.CancelarConfirmada(visita, llave: "cancelar-2");

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("booking.already_cancelled", cuerpo.GetProperty("code").GetString());
        Assert.Equal(["Cancelled"], await r.Reservas());
    }

    [Fact]
    public async Task Otra_persona_no_la_cancela_ni_sabe_que_existe()
    {
        using var r = new VisitaReal();
        var visita = await r.Confirmada();

        var (estado, cuerpo) = await r.CancelarConfirmada(visita, quien: "intruso");

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("flow.original_no_encontrada", cuerpo.GetProperty("code").GetString());
        Assert.Equal(["Confirmed"], await r.Reservas());
    }

    [Fact]
    public async Task Fuera_del_plazo_de_aviso_se_rechaza_con_su_motivo_y_la_agenda_no_cambia()
    {
        using var r = new VisitaReal();
        var visita = await r.Confirmada(cuerpo: VisitaReal.Cuerpo(inmueble: VisitaReal.Tardio));

        var (estado, cuerpo) = await r.CancelarConfirmada(visita);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("booking.cancellation_too_late", cuerpo.GetProperty("code").GetString());
        Assert.Equal(["Confirmed"], await r.Reservas());
    }

    [Fact]
    public async Task Una_visita_sin_confirmar_no_se_cancela_por_aqui_y_su_apartado_sigue()
    {
        using var r = new VisitaReal();
        var (_, abierta) = await r.Abrir();

        var (estado, cuerpo) = await r.CancelarConfirmada(abierta.GetProperty("id").GetString()!);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("flow.original_no_completada", cuerpo.GetProperty("code").GetString());
        Assert.Equal(1, await r.Ocupadas());
    }
}
