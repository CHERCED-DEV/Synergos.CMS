using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// <c>Bff.Tienda</c> publica la oferta de un producto del catálogo —precio por variante en <c>Api.Pricing</c>,
/// existencias en <c>Api.Inventory</c>— y lo publicado es lo que la compra cotiza y aparta (004-tienda-compra,
/// US 5, FR-023).
/// </summary>
/// <remarks>
/// Contra las capacidades de verdad (<see cref="CompraDeTiendaReal"/>, sin nada aprovisionado): lo que se mira es
/// lo que queda en ellas y lo que el orquestador les pidió. Un doble que contesta 201 no vería un pozo declarado
/// dos veces ni un ajuste que se suma dos.
/// </remarks>
public sealed class OfertaDeTiendaTests
{
    private static CompraDeTiendaReal SinOferta(string? pierdeLaPrimera = null) => new(
        existencias: new Dictionary<string, int>(), precios: new Dictionary<string, decimal>(), pierdeLaPrimera: pierdeLaPrimera);

    private static object Oferta(string producto, params (string? Variante, decimal Precio, int Existencias)[] variantes) => new
    {
        productId = producto,
        currency = "COP",
        variants = variantes.Select(v => new { variantId = v.Variante, price = v.Precio, stock = v.Existencias }).ToArray(),
    };

    private static object Linea(string sujeto, int cantidad) => CompraDeTiendaReal.Linea(sujeto, cantidad);

    private static async Task<int> Existencias(CompraDeTiendaReal t, string sujeto)
        => (await t.Pozo(sujeto))!.Value.GetProperty("onHand").GetInt32();

    private static async Task Publicada(CompraDeTiendaReal t, object oferta, string llave)
    {
        var (estado, cuerpo) = await t.Publicar(oferta, llave);
        Assert.True(estado == HttpStatusCode.OK, $"publicar con {llave}: {(int)estado} {cuerpo}");
    }

    private static async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Comprar(CompraDeTiendaReal t, string llave, params object[] lineas)
    {
        var (abierta, cuerpo) = await t.Abrir(lineas, llave);
        if (abierta != HttpStatusCode.Created) return (abierta, cuerpo);
        return await t.Cerrar(cuerpo.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task Lo_publicado_se_compra_al_precio_publicado_sin_impuesto_y_aparta_de_sus_existencias()
    {
        using var t = SinOferta();

        var (estado, publicada) = await t.Publicar(Oferta("o-1", (null, 20_000m, 5)), "oferta-1");
        Assert.True(estado == HttpStatusCode.OK, $"publicar: {(int)estado} {publicada}");
        var unica = Assert.Single(publicada.GetProperty("variants").EnumerateArray());
        Assert.Equal(20_000m, unica.GetProperty("price").GetProperty("amount").GetDecimal());
        Assert.Equal(5, unica.GetProperty("stock").GetInt32());
        await Publicada(t, Oferta("o-2", ("azul", 35_000m, 3), ("rojo", 30_000m, 2)), "oferta-2");

        var (cierre, cerrada) = await Comprar(t, "compra-1", Linea("o-1", 2), Linea("o-2:azul", 1));

        Assert.True(cierre == HttpStatusCode.OK, $"comprar: {(int)cierre} {cerrada}");
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal(75_000m, cerrada.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
        Assert.Equal(3, await Existencias(t, "o-1"));
        Assert.Equal(2, await Existencias(t, "o-2:azul"));
        Assert.Equal(2, await Existencias(t, "o-2:rojo"));
    }

    [Fact]
    public async Task Republicar_lo_mismo_con_la_misma_llave_o_con_otra_no_duplica_nada()
    {
        using var t = SinOferta();

        foreach (var llave in new[] { "oferta-3", "oferta-3", "oferta-3-otra-vez" })
        {
            await Publicada(t, Oferta("o-3", (null, 10_000m, 10)), llave);
        }

        Assert.Equal(10, await Existencias(t, "o-3"));
        Assert.Equal(1, t.Veces("POST /v1/items"));
        Assert.DoesNotContain(t.Llamadas, l => l.EndsWith("/adjust", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_cambio_sigue_al_editor_y_las_existencias_se_ajustan_sobre_lo_que_queda_sin_devolver_lo_vendido()
    {
        using var t = SinOferta();
        await Publicada(t, Oferta("o-4", (null, 20_000m, 10)), "oferta-4a");
        Assert.Equal(HttpStatusCode.OK, (await Comprar(t, "compra-4", Linea("o-4", 3))).Estado);
        Assert.Equal(7, await Existencias(t, "o-4"));

        // De 10 a 15: quedan 7 + 5. Fijar el total en 15 habría devuelto al pozo las tres vendidas.
        await Publicada(t, Oferta("o-4", (null, 25_000m, 15)), "oferta-4b");
        Assert.Equal(12, await Existencias(t, "o-4"));
        var (_, cotizada) = await t.Cotizar([Linea("o-4", 1)]);
        Assert.Equal(25_000m, cotizada.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());

        // De 15 a 8, y otra vez 8 con otra llave: lo segundo no ajusta nada.
        await Publicada(t, Oferta("o-4", (null, 25_000m, 8)), "oferta-4c");
        await Publicada(t, Oferta("o-4", (null, 25_000m, 8)), "oferta-4d");
        Assert.Equal(5, await Existencias(t, "o-4"));
    }

    [Fact]
    public async Task Bajar_por_debajo_de_lo_vendido_lo_deja_en_lo_vendido_y_subir_despues_cuenta_desde_ahi()
    {
        using var t = SinOferta();
        await Publicada(t, Oferta("o-5", (null, 10_000m, 10)), "oferta-5a");
        Assert.Equal(HttpStatusCode.OK, (await Comprar(t, "compra-5", Linea("o-5", 7))).Estado);

        var (estado, publicada) = await t.Publicar(Oferta("o-5", (null, 10_000m, 2)), "oferta-5b");

        Assert.True(estado == HttpStatusCode.OK, $"publicar: {(int)estado} {publicada}");
        Assert.Equal(7, Assert.Single(publicada.GetProperty("variants").EnumerateArray()).GetProperty("stock").GetInt32());
        Assert.Equal(0, await Existencias(t, "o-5"));
        Assert.NotEqual(HttpStatusCode.Created, (await t.Abrir([Linea("o-5", 1)], "compra-5-otra")).Estado);

        // Lo declarado quedó en lo vendido: subirlo después a 12 deja 5 libres, no 10.
        await Publicada(t, Oferta("o-5", (null, 10_000m, 12)), "oferta-5c");
        Assert.Equal(5, await Existencias(t, "o-5"));
    }

    [Fact]
    public async Task Bajar_con_apartados_vivos_no_baja_de_lo_apartado()
    {
        using var t = SinOferta();
        await Publicada(t, Oferta("o-6", (null, 10_000m, 10)), "oferta-6a");
        Assert.Equal(HttpStatusCode.Created, (await t.Abrir([Linea("o-6", 3)], "compra-6-abierta")).Estado);

        await Publicada(t, Oferta("o-6", (null, 10_000m, 2)), "oferta-6b");

        Assert.Equal(3, await Existencias(t, "o-6"));
        Assert.NotEqual(HttpStatusCode.Created, (await t.Abrir([Linea("o-6", 1)], "compra-6-otra")).Estado);
    }

    [Fact]
    public async Task Una_variante_que_sale_de_la_oferta_deja_de_venderse_sin_apartar_nada_y_vuelve_al_republicarla()
    {
        using var t = SinOferta();
        await Publicada(t, Oferta("o-7", ("azul", 35_000m, 3), ("rojo", 30_000m, 2)), "oferta-7a");
        await Publicada(t, Oferta("o-7", ("azul", 35_000m, 3)), "oferta-7b");
        var antes = t.Llamadas.Count;

        var (estado, rechazo) = await t.Abrir([Linea("o-7:rojo", 1)], "compra-7-rojo");

        Assert.True(estado != HttpStatusCode.Created, rechazo.ToString());
        Assert.DoesNotContain(t.Llamadas.Skip(antes), l => l.EndsWith("/holds", StringComparison.Ordinal));
        Assert.Equal(0, await Existencias(t, "o-7:rojo"));
        Assert.Equal(HttpStatusCode.Created, (await t.Abrir([Linea("o-7:azul", 1)], "compra-7-azul")).Estado);

        await Publicada(t, Oferta("o-7", ("azul", 35_000m, 3), ("rojo", 30_000m, 2)), "oferta-7c");
        Assert.Equal(HttpStatusCode.Created, (await t.Abrir([Linea("o-7:rojo", 1)], "compra-7-rojo-otra-vez")).Estado);
    }

    [Fact]
    public async Task Retirar_un_producto_deja_de_vender_todas_sus_variantes_y_republicarlo_lo_vuelve_a_poner_a_la_venta()
    {
        using var t = SinOferta();
        await Publicada(t, Oferta("o-8", ("azul", 35_000m, 3), ("rojo", 30_000m, 2)), "oferta-8a");

        var (estado, retirada) = await t.Retirar("o-8", "retiro-8");

        Assert.True(estado == HttpStatusCode.OK, $"retirar: {(int)estado} {retirada}");
        Assert.Empty(retirada.GetProperty("variants").EnumerateArray());
        Assert.NotEqual(HttpStatusCode.Created, (await t.Abrir([Linea("o-8:azul", 1)], "compra-8-azul")).Estado);
        Assert.Equal(0, await Existencias(t, "o-8:rojo"));
        Assert.Equal(HttpStatusCode.OK, (await t.Retirar("o-8", "retiro-8-otra-vez")).Estado);
        Assert.Equal(HttpStatusCode.OK, (await t.Retirar("nunca-publicado", "retiro-nada")).Estado);

        await Publicada(t, Oferta("o-8", ("azul", 35_000m, 3), ("rojo", 30_000m, 2)), "oferta-8b");
        Assert.Equal(HttpStatusCode.Created, (await t.Abrir([Linea("o-8:azul", 1)], "compra-8-azul-otra-vez")).Estado);
        Assert.Equal(3, await Existencias(t, "o-8:azul"));
    }

    [Fact]
    public async Task Una_variante_que_falla_no_impide_publicar_las_demas_y_republicar_termina_lo_que_falta()
    {
        // La respuesta del primer precio se pierde: para el orquestador, Pricing no contestó por la azul.
        using var t = SinOferta(pierdeLaPrimera: "/v1/prices");

        var (estado, rechazo) = await t.Publicar(Oferta("o-9", ("azul", 35_000m, 3), ("rojo", 30_000m, 2)), "oferta-9a");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, estado);
        Assert.Contains("o-9:azul", rechazo.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Created, (await t.Abrir([Linea("o-9:rojo", 1)], "compra-9-rojo")).Estado);

        await Publicada(t, Oferta("o-9", ("azul", 35_000m, 3), ("rojo", 30_000m, 2)), "oferta-9b");
        Assert.Equal(HttpStatusCode.Created, (await t.Abrir([Linea("o-9:azul", 1)], "compra-9-azul")).Estado);
        Assert.Equal(3, await Existencias(t, "o-9:azul"));
        Assert.Equal(2, t.Veces("POST /v1/items"));
    }

    [Fact]
    public async Task Un_ajuste_cuya_respuesta_se_perdio_se_termina_con_su_llave_y_no_se_suma_dos_veces()
    {
        // El ajuste LLEGA a Inventory y la respuesta se pierde: la publicación siguiente, con otras existencias
        // y otra llave, tiene que terminar ése primero —la capacidad lo reconoce por su llave— y ajustar desde ahí.
        using var t = SinOferta(pierdeLaPrimera: "/adjust");
        await Publicada(t, Oferta("o-10", (null, 10_000m, 10)), "oferta-10a");

        var (perdida, rechazo) = await t.Publicar(Oferta("o-10", (null, 10_000m, 15)), "oferta-10b");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, perdida);
        Assert.Equal("inventory.unreachable", rechazo.GetProperty("code").GetString());
        Assert.Equal(15, await Existencias(t, "o-10"));

        await Publicada(t, Oferta("o-10", (null, 10_000m, 12)), "oferta-10c");
        Assert.Equal(12, await Existencias(t, "o-10"));
        Assert.Equal(3, t.Llamadas.Count(l => l.EndsWith("/adjust", StringComparison.Ordinal)));
    }

    public static TheoryData<string, string> Incompletas() => new()
    {
        { """{"currency":"COP","variants":[{"price":1,"stock":1}]}""", "tienda.bad_product" },
        { """{"productId":"a:b","currency":"COP","variants":[{"price":1,"stock":1}]}""", "tienda.bad_product" },
        { """{"productId":"p","currency":"PESOS","variants":[{"price":1,"stock":1}]}""", "tienda.bad_offer" },
        { """{"productId":"p","currency":"COP","variants":[]}""", "tienda.bad_offer" },
        { """{"productId":"p","currency":"COP","variants":[{"variantId":"a","price":1,"stock":1},{"variantId":"a","price":2,"stock":1}]}""", "tienda.bad_variant" },
        { """{"productId":"p","currency":"COP","variants":[{"price":1,"stock":1},{"price":2,"stock":1}]}""", "tienda.bad_variant" },
        { """{"productId":"p","currency":"COP","variants":[{"variantId":"a:b","price":1,"stock":1}]}""", "tienda.bad_variant" },
        { """{"productId":"p","currency":"COP","variants":[{"price":-1,"stock":1}]}""", "tienda.bad_price" },
        { """{"productId":"p","currency":"COP","variants":[{"price":1}]}""", "tienda.bad_stock" },
        // La tercera variante está mal: las dos primeras tampoco se publican.
        { """{"productId":"p","currency":"COP","variants":[{"variantId":"a","price":1,"stock":1},{"variantId":"b","price":1,"stock":1},{"variantId":"c","price":1,"stock":-1}]}""", "tienda.bad_stock" },
    };

    [Theory]
    [MemberData(nameof(Incompletas))]
    public async Task Una_oferta_que_no_sirve_se_rechaza_con_su_codigo_antes_de_escribir_nada(string cuerpo, string codigo)
    {
        using var t = SinOferta();
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/ofertas")
        {
            Content = new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/json"),
        };
        peticion.Headers.Add("Idempotency-Key", "oferta-mala");

        using var r = await t.Orquestador.SendAsync(peticion);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(codigo, (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.DoesNotContain(t.Llamadas, l => l.Contains("/v1/prices", StringComparison.Ordinal) || l.Contains("/v1/items", StringComparison.Ordinal));
    }
}
