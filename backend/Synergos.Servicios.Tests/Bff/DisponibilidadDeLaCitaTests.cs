using System.Net;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// I1 de Salud en producción (T046): el asistente ofrece las franjas que dice la agenda del profesional —su horario y su
/// franja—, con lo libre de cada una, en la lectura sin saga «disponibilidad» que la puerta abre a invitados (núcleo
/// NB-3). Antes ofrecía las mismas cuatro horas los siete días. Contra la agenda real del arnés.
/// </summary>
public sealed class DisponibilidadDeLaCitaTests
{
    private static readonly DateTimeOffset Dia = new(CitaDeSaludReal.Inicio.UtcDateTime.Date, TimeSpan.Zero);

    [Fact]
    public async Task Un_invitado_ve_las_franjas_del_horario_del_profesional_con_lo_libre_de_cada_una()
    {
        using var c = new CitaDeSaludReal();
        await c.RegistrarAgenda();

        var (estado, cuerpo) = await c.Disponibilidad(Dia, Dia.AddDays(1));

        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        Assert.Equal("salud.cita", cuerpo.GetProperty("flujo").GetString());
        var franjas = Franjas(cuerpo);
        Assert.Equal(
            Enumerable.Range(0, 6).Select(i => Dia.AddHours(15).AddMinutes(30 * i)),
            franjas.Select(f => f.Inicio));
        Assert.All(franjas, f => Assert.Equal(f.Inicio.AddMinutes(30), f.Fin));
        Assert.All(franjas, f => Assert.Equal(1, f.Libres));
    }

    [Fact]
    public async Task La_franja_de_una_cita_abierta_sale_llena_y_vuelve_libre_al_cancelarla()
    {
        using var c = new CitaDeSaludReal();
        await c.RegistrarAgenda();
        var (_, abierta) = await c.Abrir(profesionalId: CitaDeSaludReal.ConAgenda);

        var llena = Franjas((await c.Disponibilidad(Dia, Dia.AddDays(1))).Cuerpo);
        Assert.Equal(0, llena.Single(f => f.Inicio == CitaDeSaludReal.Inicio).Libres);
        Assert.Equal(1, llena.Single(f => f.Inicio == CitaDeSaludReal.Inicio.AddMinutes(30)).Libres);

        await c.Cancelar(abierta.GetProperty("id").GetString()!);

        var libre = Franjas((await c.Disponibilidad(Dia, Dia.AddDays(1))).Cuerpo);
        Assert.Equal(1, libre.Single(f => f.Inicio == CitaDeSaludReal.Inicio).Libres);
    }

    /// <summary>NR-1: la clase la fija la definición; la agenda de un inmueble no se lee por la cita.</summary>
    [Fact]
    public async Task La_agenda_de_otra_clase_no_se_lee_por_la_cita()
    {
        using var c = new CitaDeSaludReal();

        var (estado, cuerpo) = await c.Disponibilidad(Dia, Dia.AddDays(1), "casa-1", "realty.listado");

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("salud.entrada_invalida", cuerpo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Un_profesional_sin_agenda_lo_dice_y_no_inventa_franjas()
    {
        using var c = new CitaDeSaludReal();

        var (estado, cuerpo) = await c.Disponibilidad(Dia, Dia.AddDays(1), "dra-sin-agenda");

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("booking.resource_not_found", cuerpo.GetProperty("code").GetString());
    }

    private static List<(DateTimeOffset Inicio, DateTimeOffset Fin, int Libres)> Franjas(JsonElement cuerpo)
        => cuerpo.GetProperty("datos").GetProperty("franjas").EnumerateArray()
            .Select(f => (f.GetProperty("inicio").GetDateTimeOffset(), f.GetProperty("fin").GetDateTimeOffset(), f.GetProperty("libres").GetInt32()))
            .ToList();
}
