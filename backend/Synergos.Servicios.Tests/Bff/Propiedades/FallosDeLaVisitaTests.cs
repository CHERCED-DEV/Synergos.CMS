using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff.Propiedades;

/// <summary>
/// Historia 2 de 008-propiedades-visita: si algo falla a mitad, la franja vuelve a quedar libre (SC-003), contra la
/// agenda real; lo que ninguna capacidad sabe hacer a pedido se inyecta en el cable.
/// </summary>
public sealed class FallosDeLaVisitaTests
{
    [Fact]
    public async Task Cancelar_antes_de_confirmar_suelta_la_franja_y_se_vuelve_a_ofrecer()
    {
        using var r = new VisitaReal();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cancelada) = await r.Cancelar(id);

        Assert.True(estado == HttpStatusCode.OK, cancelada.ToString());
        Assert.Equal(0, await r.Ocupadas());
        Assert.True((await r.Leer("booking", $"v1/holds/{r.Dato(id, "apartadoId")}")).GetProperty("released").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, (await r.Abrir(llave: "otra", quien: "beto")).Estado);
    }

    [Fact]
    public async Task Si_la_agenda_no_confirma_la_visita_se_deshace_y_la_franja_queda_libre()
    {
        using var r = new VisitaReal(falla: p => VisitaReal.Es(p, "POST", "^/v1/holds/[^/]+/confirm$") ? "booking.hold_expired" : null);
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cuerpo) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("booking.hold_expired", cuerpo.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Sagas.Find(id)!.Status);
        Assert.True((await r.Leer("booking", $"v1/holds/{r.Dato(id, "apartadoId")}")).GetProperty("released").GetBoolean());
        Assert.Empty(await r.Reservas());
        Assert.Empty(await r.Avisos());
    }

    [Fact]
    public async Task Si_la_agenda_no_aparta_no_queda_nada_tomado()
    {
        using var r = new VisitaReal(falla: p => VisitaReal.Es(p, "POST", "^/v1/holds$") ? "booking.unavailable" : null);

        var (estado, cuerpo) = await r.Abrir();

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.Equal("booking.unavailable", cuerpo.GetProperty("code").GetString());
        Assert.False(r.Tiene("visita-1", "apartadoId"));
        Assert.Equal(0, await r.Ocupadas());
    }

    [Fact]
    public async Task Un_intento_deshecho_no_encierra_la_misma_intencion()
    {
        var rechazar = true;
        using var r = new VisitaReal(falla: p => rechazar && VisitaReal.Es(p, "POST", "^/v1/holds/[^/]+/confirm$") ? "booking.hold_expired" : null);
        var (_, abierta) = await r.Abrir();
        Assert.Equal(HttpStatusCode.Conflict, (await r.Cerrar(abierta.GetProperty("id").GetString()!)).Estado);

        rechazar = false;
        var (estado, otra) = await r.Abrir();

        Assert.Equal(HttpStatusCode.Created, estado);
        Assert.NotEqual(abierta.GetProperty("id").GetString(), otra.GetProperty("id").GetString());
        Assert.Equal(1, await r.Ocupadas());
    }

    /// <summary>Una visita abandonada se barre a los 15 minutos (como Salud y Reservas), no a los 60 por defecto.</summary>
    [Fact]
    public void El_abandono_se_barre_a_los_15_minutos_salvo_que_el_despliegue_diga_otra_cosa()
    {
        using var r = new VisitaReal();
        Assert.Equal(15, r.Servicios.GetRequiredService<IOptions<SweepOptions>>().Value.AbandonAfterMinutes);

        using var otro = new VisitaReal(ajustes: new Dictionary<string, string> { ["propiedades:Sweep:AbandonAfterMinutes"] = "30" });
        Assert.Equal(30, otro.Servicios.GetRequiredService<IOptions<SweepOptions>>().Value.AbandonAfterMinutes);
    }
}
