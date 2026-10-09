using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// <c>Bff.Eventos</c> publica la oferta de un evento —precio por localidad en <c>Api.Pricing</c>, pozos
/// de aforo en <c>Api.Inventory</c>— y lo publicado se puede comprar (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>Contra las capacidades de verdad</b> (<see cref="CompraDeEventosReal"/>): lo que se mira es
/// lo que queda en ellas —el aforo, lo cobrado— y lo que el orquestador les pidió. Un doble que
/// contesta 201 no vería un pozo declarado dos veces ni un ajuste que se suma dos.</para>
///
/// <para>Los eventos de acá no son <c>evt-1</c>, que el montaje común deja a la venta por su cuenta,
/// salvo en el test que mira justo eso: un pozo que este orquestador no declaró.</para>
/// </remarks>
public sealed class OfertaDeEventosTests
{
    private static readonly string Ana = CompraDeEventosReal.Sujeto("m1");

    /// <summary>Una oferta de una localidad general, a la venta desde ayer y para dentro de un mes.</summary>
    private static object Oferta(
        string evento, int aforo = 10, decimal precio = 50_000m, int? tope = 4, DateTimeOffset? empieza = null,
        DateTimeOffset? abre = null, DateTimeOffset? cierra = null, string[]? butacas = null)
        => new
        {
            eventId = evento,
            currency = "COP",
            startsAtUtc = empieza ?? DateTimeOffset.UtcNow.AddDays(30),
            tiers = new[]
            {
                new
                {
                    code = "GEN", price = precio, maxPerOrder = tope, capacity = aforo, seats = butacas,
                    saleOpensUtc = abre ?? DateTimeOffset.UtcNow.AddDays(-1), saleClosesUtc = cierra,
                },
            },
        };

    private static async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Publicar(CompraDeEventosReal compra, object oferta, string llave)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/ofertas") { Content = JsonContent.Create(oferta) };
        peticion.Headers.Add("Idempotency-Key", llave);
        using var r = await compra.Orquestador.SendAsync(peticion);
        var texto = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
    }

    /// <summary>El pozo de un sujeto en <c>Api.Inventory</c>, o nada si no existe.</summary>
    private static async Task<JsonElement?> Pozo(CompraDeEventosReal compra, string sujeto)
    {
        using var http = compra.Capacidades.CreateClient("inventory");
        using var r = await http.GetAsync($"v1/items?subjectKind=eventos.aforo&subjectId={Uri.EscapeDataString(sujeto)}");
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<int> Existencias(CompraDeEventosReal compra, string sujeto)
        => (await Pozo(compra, sujeto))!.Value.GetProperty("onHand").GetInt32();

    private static async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Comprar(
        CompraDeEventosReal compra, string evento, int cuantas, string llave, string? butaca = null, decimal comision = 0m)
    {
        var (abierta, cuerpo) = await compra.Abrir(Ana, CompraDeEventosReal.Negocio(comision), llave,
            new { eventId = evento, lines = new[] { new { tier = "GEN", seat = butaca, quantity = cuantas } } });
        if (abierta != HttpStatusCode.Created) return (abierta, cuerpo);
        return await compra.Sobre(cuerpo.GetProperty("id").GetString()!, "confirm", Ana);
    }

    [Fact]
    public async Task Lo_publicado_se_compra_y_se_cobra_precio_por_cantidad_mas_comision_sin_impuesto()
    {
        using var compra = new CompraDeEventosReal();
        var empieza = DateTimeOffset.UtcNow.AddDays(30);

        var (estado, publicada) = await Publicar(compra, Oferta("evt-o1", empieza: empieza), "oferta-1");
        Assert.True(estado == HttpStatusCode.OK, $"publicar: {(int)estado} {publicada}");
        var gen = Assert.Single(publicada.GetProperty("tiers").EnumerateArray());
        Assert.Equal(50_000m, gen.GetProperty("price").GetProperty("amount").GetDecimal());
        Assert.Equal(empieza, gen.GetProperty("validTo").GetDateTimeOffset());
        Assert.Equal(4, gen.GetProperty("maxPerOrder").GetInt32());
        Assert.Equal(10, gen.GetProperty("capacity").GetInt32());

        var (cierre, cerrada) = await Comprar(compra, "evt-o1", 3, "compra-o1", comision: 10m);

        Assert.True(cierre == HttpStatusCode.OK, $"comprar: {(int)cierre} {cerrada}");
        Assert.Equal("Completed", cerrada.GetProperty("status").GetString());
        // 3 × 50.000 + el 10 % de comisión, y nada de impuesto: lo que pinta la ficha.
        Assert.Equal(165_000m, cerrada.GetProperty("total").GetProperty("amount").GetDecimal());
        var id = cerrada.GetProperty("id").GetString();
        var cobro = Assert.Single(await compra.Lista("payments", $"v1/payments?forKind=eventos.compra&forId={id}"));
        Assert.Equal(165_000m, cobro.GetProperty("amount").GetProperty("amount").GetDecimal());
        Assert.Equal(7, await Existencias(compra, "evt-o1/GEN"));

        // Y el tope publicado manda: cinco de una vez no se cotizan.
        var (sobreElTope, rechazo) = await compra.Abrir(Ana, CompraDeEventosReal.Negocio(0m), "compra-o1-tope",
            new { eventId = "evt-o1", lines = new[] { new { tier = "GEN", quantity = 5 } } });
        Assert.NotEqual(HttpStatusCode.Created, sobreElTope);
        Assert.Equal("pricing.quantity_over_limit", rechazo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Un_evento_que_ya_empezo_se_rechaza_al_cotizar_sin_apartar_nada()
    {
        // Su localidad «cierra» mañana, como cuando el editor pone el día del evento: el inicio manda.
        var anota = default(QueAnota);
        using var compra = new CompraDeEventosReal(envolver: f => anota = new QueAnota(f));
        var empezo = DateTimeOffset.UtcNow.AddHours(-1);

        var (estado, publicada) = await Publicar(compra,
            Oferta("evt-o2", empieza: empezo, abre: empezo.AddDays(-10), cierra: DateTimeOffset.UtcNow.AddDays(1)), "oferta-2");
        Assert.True(estado == HttpStatusCode.OK, $"publicar: {(int)estado} {publicada}");
        Assert.Equal(empezo, Assert.Single(publicada.GetProperty("tiers").EnumerateArray()).GetProperty("validTo").GetDateTimeOffset());

        var (abierta, rechazo) = await compra.Abrir(Ana, CompraDeEventosReal.Negocio(0m), "compra-o2",
            new { eventId = "evt-o2", lines = new[] { new { tier = "GEN", quantity = 1 } } });

        Assert.NotEqual(HttpStatusCode.Created, abierta);
        Assert.Equal("pricing.price_not_in_effect", rechazo.GetProperty("code").GetString());
        Assert.DoesNotContain(anota!.Pedidos, p => p.StartsWith("inventory POST", StringComparison.Ordinal) && p.EndsWith("/holds", StringComparison.Ordinal));
        Assert.DoesNotContain(anota.Pedidos, p => p.StartsWith("payments", StringComparison.Ordinal));
        Assert.Equal(10, await Existencias(compra, "evt-o2/GEN"));
    }

    [Fact]
    public async Task Republicar_lo_mismo_con_la_misma_llave_o_con_otra_no_duplica_nada()
    {
        var anota = default(QueAnota);
        using var compra = new CompraDeEventosReal(envolver: f => anota = new QueAnota(f));
        var oferta = Oferta("evt-o3");

        foreach (var llave in new[] { "oferta-3", "oferta-3", "oferta-3-otra-vez" })
        {
            var (estado, cuerpo) = await Publicar(compra, oferta, llave);
            Assert.True(estado == HttpStatusCode.OK, $"publicar con {llave}: {(int)estado} {cuerpo}");
        }

        Assert.Equal(10, await Existencias(compra, "evt-o3/GEN"));
        Assert.Single(anota!.Pedidos, p => p == "inventory POST /v1/items");
        Assert.DoesNotContain(anota.Pedidos, p => p.EndsWith("/adjust", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_cambio_de_aforo_se_ajusta_sobre_lo_que_queda_sin_devolver_lo_vendido()
    {
        using var compra = new CompraDeEventosReal();
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, Oferta("evt-o4", aforo: 10), "oferta-4a")).Estado);
        Assert.Equal(HttpStatusCode.OK, (await Comprar(compra, "evt-o4", 3, "compra-o4")).Estado);
        Assert.Equal(7, await Existencias(compra, "evt-o4/GEN"));

        // De 10 a 15: quedan 7 + 5. Fijar el total en 15 habría devuelto al pozo las tres vendidas.
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, Oferta("evt-o4", aforo: 15), "oferta-4b")).Estado);
        Assert.Equal(12, await Existencias(compra, "evt-o4/GEN"));

        // De 15 a 8, y otra vez 8 con otra llave: lo segundo no ajusta nada.
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, Oferta("evt-o4", aforo: 8), "oferta-4c")).Estado);
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, Oferta("evt-o4", aforo: 8), "oferta-4d")).Estado);
        Assert.Equal(5, await Existencias(compra, "evt-o4/GEN"));
    }

    [Fact]
    public async Task Un_ajuste_cuya_respuesta_se_perdio_se_termina_con_su_llave_y_no_se_suma_dos_veces()
    {
        // El ajuste LLEGA a Inventory y la respuesta se pierde: para el orquestador es un fallo
        // transitorio. La publicación siguiente, con otro aforo y otra llave, tiene que terminar ése
        // primero —la capacidad lo reconoce por su llave— y ajustar desde ahí.
        var anota = default(QueAnota);
        using var compra = new CompraDeEventosReal(envolver: f => anota = new QueAnota(f, pierdeLaPrimera: "/adjust"));
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, Oferta("evt-o5", aforo: 10), "oferta-5a")).Estado);

        var (perdida, rechazo) = await Publicar(compra, Oferta("evt-o5", aforo: 15), "oferta-5b");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, perdida);
        Assert.Equal("inventory.unreachable", rechazo.GetProperty("code").GetString());
        Assert.Equal(15, await Existencias(compra, "evt-o5/GEN"));

        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, Oferta("evt-o5", aforo: 12), "oferta-5c")).Estado);
        Assert.Equal(12, await Existencias(compra, "evt-o5/GEN"));
        Assert.Equal(3, anota!.Pedidos.Count(p => p.EndsWith("/adjust", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Las_butacas_salen_como_pozos_de_uno_y_se_vende_la_butaca_pedida()
    {
        using var compra = new CompraDeEventosReal();
        var (estado, publicada) = await Publicar(compra, Oferta("evt-o6", aforo: 999, butacas: ["A-1", "A-2", "A-3"]), "oferta-6");
        Assert.True(estado == HttpStatusCode.OK, $"publicar: {(int)estado} {publicada}");
        var gen = Assert.Single(publicada.GetProperty("tiers").EnumerateArray());
        Assert.Equal(3, gen.GetProperty("pools").GetInt32());
        Assert.Equal(3, gen.GetProperty("capacity").GetInt32());
        Assert.Null(await Pozo(compra, "evt-o6/GEN"));

        var (cierre, cerrada) = await Comprar(compra, "evt-o6", 1, "compra-o6", butaca: "A-2");

        Assert.True(cierre == HttpStatusCode.OK, $"comprar: {(int)cierre} {cerrada}");
        Assert.Equal(0, await Existencias(compra, "evt-o6/GEN/A-2"));
        Assert.Equal(1, await Existencias(compra, "evt-o6/GEN/A-1"));
        Assert.Equal(1, await Existencias(compra, "evt-o6/GEN/A-3"));
    }

    [Fact]
    public async Task Un_pozo_que_no_declaro_este_orquestador_se_toma_como_declarado_sin_ajustarlo_a_ciegas()
    {
        // evt-1/GEN lo deja el montaje común con 10, por fuera del orquestador.
        using var compra = new CompraDeEventosReal();

        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, Oferta("evt-1", aforo: 25), "oferta-7a")).Estado);
        Assert.Equal(10, await Existencias(compra, "evt-1/GEN"));
        Assert.Contains(compra.Log.Lineas, l => l.Contains("eventos.aforo:evt-1/GEN", StringComparison.Ordinal)
                                                && l.Contains("sin ajustar", StringComparison.Ordinal));

        // Desde ahí, lo declarado es 25 y un cambio se ajusta sobre eso.
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, Oferta("evt-1", aforo: 30), "oferta-7b")).Estado);
        Assert.Equal(15, await Existencias(compra, "evt-1/GEN"));
    }

    // ── La oferta es el estado ENTERO del evento (ADR 0140 F3, endurecimiento) ─

    /// <summary>Una oferta de dos localidades, GEN y VIP, a la venta desde ayer y para dentro de un mes.</summary>
    private static object DosLocalidades(string evento, int aforoGen = 10, decimal precioVip = 100_000m, bool conVip = true)
    {
        var abre = DateTimeOffset.UtcNow.AddDays(-1);
        var gen = new { code = "GEN", price = 50_000m, maxPerOrder = (int?)10, capacity = aforoGen, seats = (string[]?)null, saleOpensUtc = abre };
        var vip = new { code = "VIP", price = precioVip, maxPerOrder = (int?)10, capacity = 5, seats = (string[]?)null, saleOpensUtc = abre };
        return new
        {
            eventId = evento,
            currency = "COP",
            startsAtUtc = DateTimeOffset.UtcNow.AddDays(30),
            tiers = conVip ? new[] { gen, vip } : new[] { gen },
        };
    }

    private static async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(
        CompraDeEventosReal compra, string evento, string localidad, int cuantas, string llave, string? butaca = null)
        => await compra.Abrir(Ana, CompraDeEventosReal.Negocio(0m), llave,
            new { eventId = evento, lines = new[] { new { tier = localidad, seat = butaca, quantity = cuantas } } });

    private static async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Retirar(CompraDeEventosReal compra, string evento, string llave)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/ofertas/{evento}/retirar");
        peticion.Headers.Add("Idempotency-Key", llave);
        using var r = await compra.Orquestador.SendAsync(peticion);
        var texto = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
    }

    [Fact]
    public async Task Una_localidad_que_sale_de_la_oferta_deja_de_venderse_sin_apartar_nada_y_vuelve_al_republicarla()
    {
        // Es la localidad que el editor borra, o le pone aforo cero: el contenido la omite y ya no llega.
        var anota = default(QueAnota);
        using var compra = new CompraDeEventosReal(envolver: f => anota = new QueAnota(f));
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, DosLocalidades("evt-r1"), "oferta-r1a")).Estado);
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, DosLocalidades("evt-r1", conVip: false), "oferta-r1b")).Estado);
        var apartadosAntes = anota!.Pedidos.Count(p => p.EndsWith("/holds", StringComparison.Ordinal));

        var (vip, rechazo) = await Abrir(compra, "evt-r1", "VIP", 2, "compra-r1-vip");

        Assert.NotEqual(HttpStatusCode.Created, vip);
        Assert.Equal("pricing.price_not_in_effect", rechazo.GetProperty("code").GetString());
        Assert.Equal(apartadosAntes, anota.Pedidos.Count(p => p.EndsWith("/holds", StringComparison.Ordinal)));
        Assert.Equal(HttpStatusCode.Created, (await Abrir(compra, "evt-r1", "GEN", 1, "compra-r1-gen")).Estado);

        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, DosLocalidades("evt-r1"), "oferta-r1c")).Estado);
        Assert.Equal(HttpStatusCode.Created, (await Abrir(compra, "evt-r1", "VIP", 2, "compra-r1-vip-otra")).Estado);
    }

    [Fact]
    public async Task Retirar_un_evento_deja_de_vender_todas_sus_localidades_y_republicar_lo_vuelve_a_poner_a_la_venta()
    {
        using var compra = new CompraDeEventosReal();
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, DosLocalidades("evt-r2"), "oferta-r2a")).Estado);
        Assert.Equal(HttpStatusCode.OK, (await Comprar(compra, "evt-r2", 3, "compra-r2")).Estado);

        Assert.Equal(HttpStatusCode.OK, (await Retirar(compra, "evt-r2", "retiro-r2a")).Estado);
        Assert.Equal(HttpStatusCode.OK, (await Retirar(compra, "evt-r2", "retiro-r2b")).Estado);   // repetirlo no hace daño

        foreach (var localidad in new[] { "GEN", "VIP" })
        {
            var (estado, rechazo) = await Abrir(compra, "evt-r2", localidad, 1, $"compra-r2-{localidad}");
            Assert.NotEqual(HttpStatusCode.Created, estado);
            Assert.Equal("pricing.price_not_in_effect", rechazo.GetProperty("code").GetString());
        }
        Assert.Equal(0, await Existencias(compra, "evt-r2/GEN"));   // y su aforo, agotado a lo vendido

        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, DosLocalidades("evt-r2"), "oferta-r2b")).Estado);
        Assert.Equal(HttpStatusCode.OK, (await Comprar(compra, "evt-r2", 1, "compra-r2-de-nuevo")).Estado);
        Assert.Equal(6, await Existencias(compra, "evt-r2/GEN"));
        Assert.Equal(HttpStatusCode.OK, (await Retirar(compra, "evt-nunca-publicado", "retiro-r2c")).Estado);
    }

    [Fact]
    public async Task Una_butaca_que_sale_del_mapa_se_agota_y_no_se_vende()
    {
        using var compra = new CompraDeEventosReal();
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, Oferta("evt-r3", butacas: ["A-1", "A-2", "A-3"]), "oferta-r3a")).Estado);
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, Oferta("evt-r3", butacas: ["A-1", "A-3"]), "oferta-r3b")).Estado);

        var (estado, _) = await Abrir(compra, "evt-r3", "GEN", 1, "compra-r3", butaca: "A-2");

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.Equal(0, await Existencias(compra, "evt-r3/GEN/A-2"));
        Assert.Equal(HttpStatusCode.OK, (await Comprar(compra, "evt-r3", 1, "compra-r3-a1", butaca: "A-1")).Estado);
    }

    [Fact]
    public async Task Bajar_el_aforo_por_debajo_de_lo_vendido_lo_deja_en_lo_vendido_y_publica_las_demas_localidades()
    {
        using var compra = new CompraDeEventosReal();
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, DosLocalidades("evt-r4", aforoGen: 10), "oferta-r4a")).Estado);
        Assert.Equal(HttpStatusCode.OK, (await Comprar(compra, "evt-r4", 7, "compra-r4")).Estado);

        var (estado, publicada) = await Publicar(compra, DosLocalidades("evt-r4", aforoGen: 2, precioVip: 200_000m), "oferta-r4b");

        Assert.True(estado == HttpStatusCode.OK, $"publicar: {(int)estado} {publicada}");
        Assert.Equal(7, publicada.GetProperty("tiers").EnumerateArray().Single(t => t.GetProperty("code").GetString() == "GEN")
            .GetProperty("capacity").GetInt32());
        Assert.Equal(0, await Existencias(compra, "evt-r4/GEN"));
        var (vip, abierta) = await Abrir(compra, "evt-r4", "VIP", 1, "compra-r4-vip");
        Assert.Equal(HttpStatusCode.Created, vip);
        Assert.Equal(200_000m, abierta.GetProperty("total").GetProperty("amount").GetDecimal());

        // Lo declarado quedó en lo vendido: subirlo después a 12 deja 5 libres, no 10.
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, DosLocalidades("evt-r4", aforoGen: 12, precioVip: 200_000m), "oferta-r4c")).Estado);
        Assert.Equal(5, await Existencias(compra, "evt-r4/GEN"));
    }

    [Fact]
    public async Task Bajar_el_aforo_con_apartados_vivos_no_baja_de_lo_apartado()
    {
        using var compra = new CompraDeEventosReal();
        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, Oferta("evt-r5", aforo: 10), "oferta-r5a")).Estado);
        Assert.Equal(HttpStatusCode.Created, (await Abrir(compra, "evt-r5", "GEN", 3, "compra-r5-abierta")).Estado);

        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, Oferta("evt-r5", aforo: 2), "oferta-r5b")).Estado);

        Assert.Equal(3, await Existencias(compra, "evt-r5/GEN"));
        Assert.NotEqual(HttpStatusCode.Created, (await Abrir(compra, "evt-r5", "GEN", 1, "compra-r5-otra")).Estado);
    }

    [Fact]
    public async Task Una_localidad_que_falla_no_impide_publicar_las_demas_y_republicar_termina_lo_que_falta()
    {
        // La respuesta del primer precio se pierde: para el orquestador, Pricing no contestó por GEN.
        var anota = default(QueAnota);
        using var compra = new CompraDeEventosReal(envolver: f => anota = new QueAnota(f, pierdeLaPrimera: "/v1/prices"));

        var (estado, rechazo) = await Publicar(compra, DosLocalidades("evt-r6"), "oferta-r6a");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, estado);
        Assert.Contains("GEN", rechazo.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Created, (await Abrir(compra, "evt-r6", "VIP", 1, "compra-r6-vip")).Estado);

        Assert.Equal(HttpStatusCode.OK, (await Publicar(compra, DosLocalidades("evt-r6"), "oferta-r6b")).Estado);
        Assert.Equal(HttpStatusCode.OK, (await Comprar(compra, "evt-r6", 1, "compra-r6-gen")).Estado);
        Assert.Equal(9, await Existencias(compra, "evt-r6/GEN"));
    }

    public static TheoryData<string, string> Incompletas() => new()
    {
        { """{"currency":"COP","startsAtUtc":"2030-01-01T00:00:00Z","tiers":[{"code":"GEN","price":1,"capacity":1}]}""", "eventos.bad_event" },
        { """{"eventId":"e","currency":"PESOS","startsAtUtc":"2030-01-01T00:00:00Z","tiers":[{"code":"GEN","price":1,"capacity":1}]}""", "eventos.bad_offer" },
        { """{"eventId":"e","currency":"COP","tiers":[{"code":"GEN","price":1,"capacity":1}]}""", "eventos.bad_offer" },
        { """{"eventId":"e","currency":"COP","startsAtUtc":"2030-01-01T00:00:00Z","tiers":[]}""", "eventos.bad_offer" },
        { """{"eventId":"e","currency":"COP","startsAtUtc":"2030-01-01T00:00:00Z","tiers":[{"code":"GEN","price":1,"capacity":1},{"code":"GEN","price":2,"capacity":1}]}""", "eventos.bad_tier" },
        { """{"eventId":"e","currency":"COP","startsAtUtc":"2030-01-01T00:00:00Z","tiers":[{"code":"GEN","price":-1,"capacity":1}]}""", "eventos.bad_price" },
        { """{"eventId":"e","currency":"COP","startsAtUtc":"2030-01-01T00:00:00Z","tiers":[{"code":"GEN","price":1,"capacity":1,"maxPerOrder":0}]}""", "eventos.bad_max_per_order" },
        { """{"eventId":"e","currency":"COP","startsAtUtc":"2030-01-01T00:00:00Z","tiers":[{"code":"GEN","price":1}]}""", "eventos.bad_capacity" },
        { """{"eventId":"e","currency":"COP","startsAtUtc":"2030-01-01T00:00:00Z","tiers":[{"code":"GEN","price":1,"seats":["A-1","A-1"]}]}""", "eventos.bad_capacity" },
        { """{"eventId":"e","currency":"COP","startsAtUtc":"2030-01-01T00:00:00Z","tiers":[{"code":"GEN","price":1,"capacity":1,"saleOpensUtc":"2030-01-01T00:00:00Z"}]}""", "eventos.bad_sale_window" },
        // La tercera localidad está mal: las dos primeras tampoco se publican.
        { """{"eventId":"e","currency":"COP","startsAtUtc":"2030-01-01T00:00:00Z","tiers":[{"code":"A","price":1,"capacity":1},{"code":"B","price":1,"capacity":1},{"code":"C","price":1}]}""", "eventos.bad_capacity" },
    };

    [Theory]
    [MemberData(nameof(Incompletas))]
    public async Task Una_oferta_que_no_sirve_se_rechaza_con_su_codigo_antes_de_escribir_nada(string cuerpo, string codigo)
    {
        var anota = default(QueAnota);
        using var compra = new CompraDeEventosReal(envolver: f => anota = new QueAnota(f));
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/ofertas")
        {
            Content = new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/json"),
        };
        peticion.Headers.Add("Idempotency-Key", "oferta-mala");

        using var r = await compra.Orquestador.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(codigo, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        // Sólo Pricing e Inventory: el barrido de avisos del orquestador consulta Notifications por su cuenta.
        Assert.DoesNotContain(anota!.Pedidos, p => p.StartsWith("pricing", StringComparison.Ordinal)
                                                  || p.StartsWith("inventory", StringComparison.Ordinal));
    }

    /// <summary>
    /// Anota lo que el orquestador le pide a cada capacidad —«inventory POST /v1/items»— y, si se le dice,
    /// pierde la respuesta de la primera petición cuya ruta termina en <c>pierdeLaPrimera</c>: la petición
    /// LLEGA a la capacidad y el orquestador ve una caída.
    /// </summary>
    private sealed class QueAnota(IHttpClientFactory interno, string? pierdeLaPrimera = null) : IHttpClientFactory
    {
        private int _perdidas;

        public ConcurrentQueue<string> Pedidos { get; } = new();

        private string? PierdeLaPrimera { get; } = pierdeLaPrimera;

        public HttpClient CreateClient(string name)
        {
            var dentro = interno.CreateClient(name);
            return new HttpClient(new Reenvio(this, dentro, name)) { BaseAddress = dentro.BaseAddress };
        }

        private sealed class Reenvio(QueAnota dueno, HttpClient dentro, string capacidad) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                var ruta = r.RequestUri!.AbsolutePath;
                dueno.Pedidos.Enqueue($"{capacidad} {r.Method} {ruta}");

                // Una copia y no la misma: un HttpClient no manda dos veces una petición que ya salió por otro.
                var copia = new HttpRequestMessage(r.Method, r.RequestUri) { Content = r.Content };
                foreach (var h in r.Headers) copia.Headers.TryAddWithoutValidation(h.Key, h.Value);
                var respuesta = await dentro.SendAsync(copia, ct);
                if (dueno.PierdeLaPrimera is { } sufijo && ruta.EndsWith(sufijo, StringComparison.Ordinal)
                    && Interlocked.Increment(ref dueno._perdidas) == 1)
                {
                    respuesta.Dispose();
                    throw new HttpRequestException("La respuesta se perdió por el camino.");
                }
                return respuesta;
            }
        }
    }
}
