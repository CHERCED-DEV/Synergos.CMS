using System.Net;
using System.Text.Json;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff.Propiedades;

/// <summary>
/// Historia 1 de 008-propiedades-visita: agendar una visita en una franja real, por la puerta, contra la agenda y
/// los avisos reales.
/// </summary>
public sealed class VisitaPorLaPuertaTests
{
    [Fact]
    public async Task Abrir_aparta_la_franja_y_cerrar_la_confirma_a_nombre_del_interesado()
    {
        using var r = new VisitaReal();

        var (estado, abierta) = await r.Abrir();
        Assert.True(estado == HttpStatusCode.Created, abierta.ToString());
        Assert.Equal("Running", abierta.GetProperty("estado").GetString());
        var id = abierta.GetProperty("id").GetString()!;
        Assert.Equal(1, await r.Ocupadas());
        Assert.Empty(await r.Reservas());

        var (cerro, cerrada) = await r.Cerrar(id);
        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal(["Confirmed"], await r.Reservas());
        Assert.Equal(1, await r.Ocupadas());
    }

    [Fact]
    public async Task La_agenda_recibe_un_identificador_opaco_del_interesado_y_nunca_su_correo()
    {
        using var r = new VisitaReal();
        var (_, abierta) = await r.Abrir();
        var hold = await r.Leer("booking", $"v1/holds/{r.Dato(abierta.GetProperty("id").GetString()!, "apartadoId")}");

        Assert.Equal("propiedades.interesado", hold.GetProperty("heldForKind").GetString());
        Assert.Equal("u-1", hold.GetProperty("heldForId").GetString());
        Assert.DoesNotContain("@", hold.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_respuesta_dice_inmueble_franja_y_modalidad_y_de_la_agenda_solo_la_reserva_que_cancela_lo_posterior()
    {
        using var r = new VisitaReal();
        var (_, abierta) = await r.Abrir(cuerpo: VisitaReal.Cuerpo(modalidad: "video"));
        var (_, cerrada) = await r.Cerrar(abierta.GetProperty("id").GetString()!);

        var datos = cerrada.GetProperty("datos");
        Assert.Equal(["inmueble", "modo", "reservaId", "ventana"], datos.EnumerateObject().Select(p => p.Name).Order().ToList());
        Assert.Equal(VisitaReal.Clase, datos.GetProperty("inmueble").GetProperty("kind").GetString());
        Assert.Equal(VisitaReal.Inmueble, datos.GetProperty("inmueble").GetProperty("id").GetString());
        Assert.Equal("video", datos.GetProperty("modo").GetString());
        Assert.Equal(VisitaReal.Inicio, datos.GetProperty("ventana").GetProperty("start").GetDateTimeOffset());
    }

    [Fact]
    public async Task Sin_modalidad_la_visita_es_presencial_y_lo_dice()
    {
        using var r = new VisitaReal();
        var (_, abierta) = await r.Abrir();

        Assert.Equal("in-person", abierta.GetProperty("datos").GetProperty("modo").GetString());
    }

    [Fact]
    public async Task Abrir_dos_veces_con_la_misma_llave_aparta_una_sola_vez_y_contesta_lo_mismo()
    {
        using var r = new VisitaReal();
        var (_, primera) = await r.Abrir();
        var (estado, segunda) = await r.Abrir();

        Assert.True(estado is HttpStatusCode.Created or HttpStatusCode.OK, segunda.ToString());
        Assert.Equal(primera.GetProperty("id").GetString(), segunda.GetProperty("id").GetString());
        Assert.Equal(1, await r.Ocupadas());
    }

    [Fact]
    public async Task Cerrar_dos_veces_confirma_una_sola_vez()
    {
        using var r = new VisitaReal();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        await r.Cerrar(id);
        var (estado, _) = await r.Cerrar(id);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal(["Confirmed"], await r.Reservas());
    }

    [Fact]
    public async Task La_visita_de_otro_no_existe_para_quien_pregunta()
    {
        using var r = new VisitaReal();
        var (_, abierta) = await r.Abrir();

        var (estado, _) = await r.Consultar(abierta.GetProperty("id").GetString()!, quien: "intruso");

        Assert.Equal(HttpStatusCode.NotFound, estado);
    }

    [Fact]
    public async Task Una_franja_tomada_se_rechaza_con_el_codigo_de_la_agenda_sin_apartar_nada()
    {
        using var r = new VisitaReal();
        Assert.Equal(HttpStatusCode.Created, (await r.Abrir(llave: "v-ana", quien: "ana")).Estado);

        var (estado, cuerpo) = await r.Abrir(llave: "v-beto", quien: "beto");

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("booking.at_capacity", cuerpo.GetProperty("code").GetString());
        Assert.False(r.Tiene("v-beto", "apartadoId"));
        Assert.Equal(1, await r.Ocupadas());
    }

    [Fact]
    public async Task Fuera_del_horario_de_visitas_lo_rechaza_la_agenda()
    {
        using var r = new VisitaReal();

        var (estado, cuerpo) = await r.Abrir(cuerpo: VisitaReal.Cuerpo(inicio: VisitaReal.ALas(15)));

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("booking.outside_opening_hours", cuerpo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Una_franja_pasada_la_rechaza_la_agenda()
    {
        using var r = new VisitaReal();
        var ayer = VisitaReal.Inicio.AddDays(-11);

        var (estado, cuerpo) = await r.Abrir(cuerpo: VisitaReal.Cuerpo(inicio: ayer));

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.Equal("booking.window_in_the_past", cuerpo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Un_inmueble_sin_agenda_se_rechaza_y_no_aparta_nada()
    {
        using var r = new VisitaReal();

        var (estado, cuerpo) = await r.Abrir(cuerpo: VisitaReal.Cuerpo(inmueble: "prop-999"));

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("booking.resource_not_found", cuerpo.GetProperty("code").GetString());
        Assert.False(r.Tiene("visita-1", "apartadoId"));
    }

    [Fact]
    public async Task La_saga_guarda_referencias_y_ningun_dato_personal()
    {
        using var r = new VisitaReal();
        var (_, abierta) = await r.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        await r.Cerrar(id);

        var guardado = JsonSerializer.Serialize(r.Sagas.Find(id)!.Datos);
        Assert.DoesNotContain("ana@ejemplo.co", guardado, StringComparison.Ordinal);
        Assert.DoesNotContain("Ana", guardado, StringComparison.Ordinal);
        Assert.Equal(SagaStatus.Completed, r.Sagas.Find(id)!.Status);
    }
}
