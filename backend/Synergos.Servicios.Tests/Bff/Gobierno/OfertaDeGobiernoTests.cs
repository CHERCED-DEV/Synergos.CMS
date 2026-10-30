using System.Net;

namespace Synergos.CMS.Tests.Bff.Gobierno;

/// <summary>
/// La oferta de un trámite en el orquestador (009-gobierno-tramite T021): la tasa en <c>Api.Pricing</c> real con el
/// sujeto que la radicación cotiza, y lo que pasa al republicar y al retirar, por la ruta de ofertas.
/// </summary>
/// <remarks>Empty / happy / filter / idempotent (ADR 0075), contra el arnés de capacidades.</remarks>
public sealed class OfertaDeGobiernoTests
{
    private static decimal Total(System.Text.Json.JsonElement cotizacion) => cotizacion.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal();

    [Fact] // happy: la tasa publicada es la que cotiza la radicación
    public async Task Publica_la_tasa_y_la_radicacion_la_cotiza()
    {
        using var r = new RadicacionReal();

        var (estado, oferta) = await r.Publicar();
        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal(RadicacionReal.Tramite, oferta.GetProperty("tramite").GetString());
        Assert.False(oferta.GetProperty("retirada").GetBoolean());

        var (cotizada, cotizacion) = await r.Cotizar();
        Assert.True(cotizada == HttpStatusCode.OK, cotizacion.ToString());
        Assert.Equal(RadicacionReal.Tasa, Total(cotizacion));
    }

    [Fact] // happy: un trámite gratuito publica su cero, y se cotiza en cero (no por ausencia)
    public async Task Un_tramite_gratuito_publica_su_cero()
    {
        using var r = new RadicacionReal();
        await r.ConOferta(RadicacionReal.Oferta(tasa: 0m));

        var (cotizada, cotizacion) = await r.Cotizar();
        Assert.True(cotizada == HttpStatusCode.OK, cotizacion.ToString());
        Assert.Equal(0m, Total(cotizacion));
    }

    [Fact] // idempotent: la misma llave no duplica; otra llave con otra tasa la reemplaza
    public async Task Repetir_no_duplica_y_republicar_cambia_la_tasa()
    {
        using var r = new RadicacionReal();
        var (primera, a) = await r.Publicar();
        var (repetida, b) = await r.Publicar();
        Assert.Equal(HttpStatusCode.OK, primera);
        Assert.Equal(HttpStatusCode.OK, repetida);
        Assert.Equal(a.ToString(), b.ToString());

        var (cambiada, _) = await r.Publicar(RadicacionReal.Oferta(tasa: 72_000m), llave: "oferta-2");
        Assert.Equal(HttpStatusCode.OK, cambiada);
        var (_, cotizacion) = await r.Cotizar();
        Assert.Equal(72_000m, Total(cotizacion));
    }

    [Fact] // filter: retirar deja de valer la tasa; cotizar rechaza sin caer a gratis; repetirlo no hace daño
    public async Task Retirar_deja_de_valer_y_cotizar_rechaza()
    {
        using var r = new RadicacionReal();
        await r.ConOferta();

        var (estado, retirada) = await r.Retirar();
        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.True(retirada.GetProperty("retirada").GetBoolean());

        // Retirada no es «sin oferta»: la capacidad dice que el precio ya no está en vigor (409), y la radicación no cobra.
        var (cotizada, cuerpo) = await r.Cotizar();
        Assert.Equal(HttpStatusCode.Conflict, cotizada);
        Assert.Equal("pricing.price_not_in_effect", cuerpo.GetProperty("code").GetString());

        var (otraVez, _) = await r.Retirar(llave: "retiro-2");
        Assert.Equal(HttpStatusCode.OK, otraVez);
    }

    [Fact] // empty: lo que nunca se publicó no se retira
    public async Task Retirar_lo_que_nunca_se_publico_es_no_existe()
    {
        using var r = new RadicacionReal();

        var (estado, cuerpo) = await r.Retirar("tram-que-no-existe");
        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("gobierno.oferta_no_existe", cuerpo.GetProperty("code").GetString());
    }

    [Theory] // filter: una oferta mal formada no se publica, con su código
    [InlineData("", 65_500, "COP")]
    [InlineData("tram con espacios", 65_500, "COP")]
    [InlineData(RadicacionReal.Tramite, -1, "COP")]
    [InlineData(RadicacionReal.Tramite, 65_500, "CO")]
    public async Task Una_oferta_mal_formada_no_se_publica(string tramite, decimal tasa, string moneda)
    {
        using var r = new RadicacionReal();

        var (estado, cuerpo) = await r.Publicar(RadicacionReal.Oferta(tramite, tasa, moneda));
        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("gobierno.oferta_invalida", cuerpo.GetProperty("code").GetString());
    }
}
