using System.Net;
using Synergos.Bff.Propiedades.Domain;

namespace Synergos.CMS.Tests.Bff.Propiedades;

/// <summary>
/// Historia 3 de 008-propiedades-visita (SC-004): por el flujo de la visita sólo se aparta una franja DE UN INMUEBLE
/// y DE LA DURACIÓN de una visita. La visita es gratis y la agenda es una sola: sin esto, cualquier cuenta vaciaría
/// la agenda de otro vertical sin pagar nada.
/// </summary>
public sealed class SoloInmueblesTests
{
    [Fact]
    public async Task La_clase_de_un_recurso_ajeno_la_rechaza_el_host_y_la_agenda_ajena_no_cambia()
    {
        using var r = new VisitaReal();

        var (estado, cuerpo) = await r.Abrir(cuerpo: VisitaReal.Cuerpo(inmueble: VisitaReal.Ajeno, clase: VisitaReal.ClaseAjena));

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("propiedades.entrada_invalida", cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, await r.Ocupadas(VisitaReal.ClaseAjena, VisitaReal.Ajeno));
    }

    [Fact]
    public async Task El_id_de_un_recurso_ajeno_con_la_clase_del_inmueble_busca_un_inmueble_y_no_lo_halla()
    {
        using var r = new VisitaReal();

        var (estado, cuerpo) = await r.Abrir(cuerpo: VisitaReal.Cuerpo(inmueble: VisitaReal.Ajeno));

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("booking.resource_not_found", cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, await r.Ocupadas(VisitaReal.ClaseAjena, VisitaReal.Ajeno));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(180)]
    public async Task Una_ventana_que_no_dura_una_visita_se_rechaza_sin_escribir_en_la_agenda(int minutos)
    {
        using var r = new VisitaReal();

        var (estado, cuerpo) = await r.Abrir(cuerpo: VisitaReal.Cuerpo(minutos: minutos));

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal(PasoValidarVisita.DuracionInvalida, cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, await r.Ocupadas(minutos: 180));
    }

    [Fact]
    public async Task Una_modalidad_que_no_existe_se_rechaza_sin_apartar()
    {
        using var r = new VisitaReal();

        var (estado, cuerpo) = await r.Abrir(cuerpo: VisitaReal.Cuerpo(modalidad: "a distancia"));

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal(PasoValidarVisita.ModalidadInvalida, cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, await r.Ocupadas());
    }

    [Fact]
    public async Task La_duracion_la_fija_el_despliegue()
    {
        using var r = new VisitaReal(ajustes: new Dictionary<string, string> { ["Propiedades:Visita:Minutos"] = "30" });

        Assert.Equal(HttpStatusCode.Created, (await r.Abrir(cuerpo: VisitaReal.Cuerpo(minutos: 30))).Estado);
    }
}
