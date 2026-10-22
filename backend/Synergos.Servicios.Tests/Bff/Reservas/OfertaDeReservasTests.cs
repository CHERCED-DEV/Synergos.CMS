using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff.Reservas;

/// <summary>
/// La oferta de un servicio reservable (011-booking-reserva, T005): la ruta que el CMS llama al publicar y al
/// despublicar la ficha, contra la agenda y precios reales.
/// </summary>
public sealed class OfertaDeReservasTests
{
    private static async Task<List<JsonElement>> RecursosDelServicio(ReservaDeServicioReal r, string servicio = ReservaDeServicioReal.Servicio)
        => (await r.Leer("booking", "v1/resources?limit=200")).GetProperty("items").EnumerateArray()
            .Where(x => x.GetProperty("subjectKind").GetString() == "reservas.servicio" && x.GetProperty("subjectId").GetString() == servicio)
            .ToList();

    [Fact]
    public async Task Publicar_registra_el_recurso_con_su_horario_cupos_y_plazo_y_fija_el_precio()
    {
        using var r = new ReservaDeServicioReal();

        var (estado, publicada) = await r.Publicar();

        Assert.True(estado == HttpStatusCode.OK, publicada.ToString());
        Assert.Equal(60, publicada.GetProperty("duracionMinutos").GetInt32());
        Assert.False(publicada.GetProperty("retirada").GetBoolean());
        var recurso = Assert.Single(await RecursosDelServicio(r));
        Assert.Equal(2, recurso.GetProperty("capacity").GetInt32());
        Assert.Equal("UTC", recurso.GetProperty("timeZoneId").GetString());
        Assert.Equal(7, recurso.GetProperty("opening").GetArrayLength());
        Assert.Equal(24 * 60, recurso.GetProperty("cancellationNoticeMinutes").GetInt32());
        Assert.Equal(60, recurso.GetProperty("slotMinutes").GetInt32());
        Assert.Equal(0, publicada.GetProperty("reservasFuera").GetInt32());
        Assert.Equal(140_000m, (await r.Cotizar()).Cuerpo.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Repetir_con_la_misma_llave_o_con_los_mismos_valores_no_registra_otro_recurso()
    {
        using var r = new ReservaDeServicioReal();

        Assert.Equal(HttpStatusCode.OK, (await r.Publicar()).Estado);
        Assert.Equal(HttpStatusCode.OK, (await r.Publicar()).Estado);
        Assert.Equal(HttpStatusCode.OK, (await r.Publicar(llave: "oferta-2")).Estado);

        Assert.Single(await RecursosDelServicio(r));
    }

    public static TheoryData<string, object> Invalidas() => new()
    {
        { "duracionMinutos", ReservaDeServicioReal.Oferta(duracion: 0) },
        { "cupos", ReservaDeServicioReal.Oferta(cupos: 0) },
        { "plazoCancelacionHoras", ReservaDeServicioReal.Oferta(plazoHoras: -1) },
        { "precio", ReservaDeServicioReal.Oferta(precio: -1m) },
        { "horario", ReservaDeServicioReal.Oferta(desde: "20:00", hasta: "08:00") },
        { "duracionMinutos", ReservaDeServicioReal.Oferta(duracion: 800) },
        { "servicio", ReservaDeServicioReal.Oferta(servicio: "../otro") },
    };

    [Theory]
    [MemberData(nameof(Invalidas))]
    public async Task Una_oferta_invalida_nombra_el_campo_y_no_publica_nada(string campo, object oferta)
    {
        using var r = new ReservaDeServicioReal();

        var (estado, cuerpo) = await r.Publicar(oferta);

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("reservas.oferta_invalida", cuerpo.GetProperty("code").GetString());
        Assert.Contains($"«{campo}»", cuerpo.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Empty(await RecursosDelServicio(r));
    }

    /// <summary>
    /// T028 con NB-7: republicar con otro horario, cupos, plazo o duración REVISA el recurso, sin cancelar nada; la
    /// confirmada que ya no cabe sigue confirmada y la respuesta la cuenta para que el CMS se lo diga al editor.
    /// </summary>
    [Fact]
    public async Task Republicar_con_otro_horario_revisa_el_recurso_y_la_confirmada_que_no_cabe_sigue_confirmada()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();
        var (_, abierta) = await r.Abrir();
        Assert.Equal(HttpStatusCode.OK, (await r.Cerrar(abierta.GetProperty("id").GetString()!)).Estado);

        var (estado, cuerpo) = await r.Publicar(
            ReservaDeServicioReal.Oferta(desde: "11:00", precio: 999_000m, duracion: 30, cupos: 3, plazoHoras: 2), llave: "oferta-2");

        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        Assert.Equal(1, cuerpo.GetProperty("reservasFuera").GetInt32());
        var recurso = Assert.Single(await RecursosDelServicio(r));
        Assert.Equal("11:00", recurso.GetProperty("opening")[0].GetProperty("from").GetString());
        Assert.Equal(30, recurso.GetProperty("slotMinutes").GetInt32());
        Assert.Equal(3, recurso.GetProperty("capacity").GetInt32());
        Assert.Equal(120, recurso.GetProperty("cancellationNoticeMinutes").GetInt32());
        Assert.Equal(["Confirmed"], await ReservaDeServicioTests.Reservas(r));
        var media = ReservaDeServicioReal.Cuerpo(inicio: ReservaDeServicioReal.Inicio.AddHours(2), minutos: 30);
        Assert.Equal(999_000m, (await r.Cotizar(cuerpo: media)).Cuerpo.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
    }

    /// <summary>
    /// La revisión lleva la llave de SU publicación: volver a unos valores que el recurso ya tuvo es otra revisión. Con
    /// una llave sacada sólo de los valores, la cuarta devolvería la segunda y la agenda se quedaría en la tercera.
    /// </summary>
    [Fact]
    public async Task Ir_y_volver_entre_dos_horarios_deja_el_ultimo()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();

        foreach (var (desde, llave) in new[] { ("09:00", "oferta-2"), ("08:00", "oferta-3"), ("09:00", "oferta-4") })
        {
            var (estado, cuerpo) = await r.Publicar(ReservaDeServicioReal.Oferta(desde: desde), llave: llave);
            Assert.True(estado == HttpStatusCode.OK, $"{llave}: {cuerpo}");
        }

        var recurso = Assert.Single(await RecursosDelServicio(r));
        Assert.Equal("09:00", recurso.GetProperty("opening")[0].GetProperty("from").GetString());
    }

    /// <summary>La zona es lo único que la agenda no revisa: se rechaza sin publicar a medias (T028).</summary>
    [Fact]
    public async Task Republicar_en_otra_zona_se_rechaza_sin_tocar_la_agenda_ni_el_precio()
    {
        using var r = new ReservaDeServicioReal();
        await r.Publicar();
        var oferta = JsonSerializer.SerializeToElement(ReservaDeServicioReal.Oferta(desde: "09:00", precio: 999_000m));
        var enBogota = JsonSerializer.Deserialize<Dictionary<string, object?>>(oferta.GetRawText())!;
        enBogota["zona"] = "America/Bogota";

        var (estado, cuerpo) = await r.Publicar(enBogota, llave: "oferta-2");

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("reservas.oferta_no_actualizable", cuerpo.GetProperty("code").GetString());
        var recurso = Assert.Single(await RecursosDelServicio(r));
        Assert.Equal("08:00", recurso.GetProperty("opening")[0].GetProperty("from").GetString());
        Assert.Equal("UTC", recurso.GetProperty("timeZoneId").GetString());
        Assert.Equal(140_000m, (await r.Cotizar()).Cuerpo.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Retirar_deja_de_ofrecer_el_servicio_y_repetirlo_no_hace_dano()
    {
        using var r = await new ReservaDeServicioReal().ConOferta();

        var (estado, retirada) = await r.Retirar();
        Assert.True(estado == HttpStatusCode.OK, retirada.ToString());
        Assert.True(retirada.GetProperty("retirada").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await r.Retirar(llave: "retiro-2")).Estado);

        var (abrir, cuerpo) = await r.Abrir(llave: "r-retirada");
        Assert.Equal(HttpStatusCode.Conflict, abrir);
        Assert.Equal("pricing.price_not_in_effect", cuerpo.GetProperty("code").GetString());
        Assert.False(r.Tiene("r-retirada", "apartadoId"));
        Assert.Single(await RecursosDelServicio(r));

        // Y en precios la tarifa dejó de valer: también para quien cotice sin pasar por el flujo.
        using var precios = r.Capacidades.CreateClient("pricing");
        using var cotizacion = await precios.PostAsJsonAsync("v1/quotes", new
        {
            lines = new[] { new { subjectKind = "reservas.servicio", subjectId = ReservaDeServicioReal.Servicio, quantity = 1 } },
        });
        Assert.False(cotizacion.IsSuccessStatusCode, await cotizacion.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Retirar_un_servicio_que_nunca_se_publico_dice_que_no_existe()
    {
        using var r = new ReservaDeServicioReal();

        var (estado, cuerpo) = await r.Retirar("nunca");

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("reservas.oferta_no_existe", cuerpo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Las_seis_ofertas_del_piloto_se_publican_y_quedan_reservables()
    {
        using var r = new ReservaDeServicioReal();
        using var fichero = JsonDocument.Parse(File.ReadAllText(
            Proyectos.Dir("Synergos.Servicios.Tests", "Bff", "Reservas", "ofertas-booking.json")));
        var comunes = fichero.RootElement[0].GetProperty("comunes");
        var ofertas = fichero.RootElement.EnumerateArray().Skip(1).ToList();

        foreach (var o in ofertas)
        {
            var (estado, cuerpo) = await r.Publicar(new
            {
                servicio = o.GetProperty("servicio").GetString(),
                nombre = o.GetProperty("nombre").GetString(),
                duracionMinutos = o.GetProperty("duracionMinutos").GetInt32(),
                horario = comunes.GetProperty("horario").EnumerateArray()
                    .Select(d => new { dia = d.GetString(), desde = comunes.GetProperty("desde").GetString(), hasta = comunes.GetProperty("hasta").GetString() })
                    .ToList(),
                cupos = o.GetProperty("cupos").GetInt32(),
                plazoCancelacionHoras = comunes.GetProperty("plazoCancelacionHoras").GetInt32(),
                precio = o.GetProperty("precio").GetDecimal(),
                moneda = comunes.GetProperty("moneda").GetString(),
                zona = comunes.GetProperty("zona").GetString(),
            }, llave: "piloto-" + o.GetProperty("servicio").GetString());
            Assert.True(estado == HttpStatusCode.OK, $"{o.GetProperty("nombre").GetString()}: {cuerpo}");
        }

        Assert.Equal(6, ofertas.Count);
    }
}
