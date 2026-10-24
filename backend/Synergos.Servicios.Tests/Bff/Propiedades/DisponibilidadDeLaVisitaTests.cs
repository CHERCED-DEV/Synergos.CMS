using System.Net;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff.Propiedades;

/// <summary>
/// 008-P3 (FR-020): por la puerta, las franjas de visita las dice la agenda del inmueble —su horario y su franja de 60
/// minutos—, con lo libre de cada una, en la lectura sin saga «disponibilidad» que la puerta abre a invitados (núcleo
/// NB-3). Contra la agenda real del arnés.
/// </summary>
public sealed class DisponibilidadDeLaVisitaTests
{
    private static readonly DateTimeOffset Dia = VisitaReal.ALas(0);

    [Fact]
    public async Task Un_invitado_ve_las_franjas_del_horario_del_inmueble_con_lo_libre_de_cada_una()
    {
        using var r = new VisitaReal();

        var (estado, cuerpo) = await r.Disponibilidad(Dia, Dia.AddDays(1));

        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        Assert.Equal("propiedades.visita", cuerpo.GetProperty("flujo").GetString());
        var franjas = Franjas(cuerpo);
        Assert.Equal([VisitaReal.ALas(9), VisitaReal.ALas(10), VisitaReal.ALas(11)], franjas.Select(f => f.Inicio));
        Assert.All(franjas, f => Assert.Equal(f.Inicio.AddMinutes(60), f.Fin));
        Assert.All(franjas, f => Assert.Equal(1, f.Libres));
    }

    [Fact]
    public async Task La_franja_apartada_sale_llena_y_vuelve_a_salir_libre_al_soltarla()
    {
        using var r = new VisitaReal();
        var (_, abierta) = await r.Abrir();

        var llena = Franjas((await r.Disponibilidad(Dia, Dia.AddDays(1))).Cuerpo);
        Assert.Equal(0, llena.Single(f => f.Inicio == VisitaReal.Inicio).Libres);
        Assert.Equal(1, llena.Single(f => f.Inicio == VisitaReal.ALas(10)).Libres);

        await r.Cancelar(abierta.GetProperty("id").GetString()!);

        var libre = Franjas((await r.Disponibilidad(Dia, Dia.AddDays(1))).Cuerpo);
        Assert.Equal(1, libre.Single(f => f.Inicio == VisitaReal.Inicio).Libres);
    }

    [Fact]
    public async Task La_agenda_de_otra_clase_no_se_lee_por_la_visita()
    {
        using var r = new VisitaReal();

        var (estado, cuerpo) = await r.Disponibilidad(Dia, Dia.AddDays(1), VisitaReal.Ajeno, VisitaReal.ClaseAjena);

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("propiedades.entrada_invalida", cuerpo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Un_inmueble_sin_agenda_lo_dice_y_no_inventa_franjas()
    {
        using var r = new VisitaReal();

        var (estado, cuerpo) = await r.Disponibilidad(Dia, Dia.AddDays(1), "prop-sin-agenda");

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("booking.resource_not_found", cuerpo.GetProperty("code").GetString());
    }

    private static List<(DateTimeOffset Inicio, DateTimeOffset Fin, int Libres)> Franjas(JsonElement cuerpo)
        => cuerpo.GetProperty("datos").GetProperty("franjas").EnumerateArray()
            .Select(f => (f.GetProperty("inicio").GetDateTimeOffset(), f.GetProperty("fin").GetDateTimeOffset(), f.GetProperty("libres").GetInt32()))
            .ToList();
}
