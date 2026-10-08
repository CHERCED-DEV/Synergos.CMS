using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.Api.Pricing.Domain;
using Synergos.Api.Pricing.Storage;
using Synergos.CMS.Tests.Bff;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Un precio de <c>Api.Pricing</c> puede tener vigencia y tope por cotización (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>Es el calendario de venta (#195) mudado a su dueño.</b> Lo comprobaba sólo el CMS, en
/// los dos caminos de compra; una puerta genérica lo habría perdido. Con la vigencia en el precio,
/// cualquier flujo que cotice la respeta, y rechaza ANTES de apartar nada porque cotizar va primero.
/// La regla es la del CMS: desde incluido, hasta excluido.</para>
///
/// <para><b>Y el tope por compra</b> (el <c>MaxPerOrder</c> del contenido), que sólo aplicaba el motor
/// en proceso: por sujeto, sumando sus líneas.</para>
/// </remarks>
public sealed class PrecioConVigenciaTests : IDisposable
{
    private static readonly DateTimeOffset Abre = new(2026, 10, 1, 9, 0, 0, TimeSpan.FromHours(-5));
    private static readonly DateTimeOffset Cierra = Abre.AddDays(10);
    private static readonly Ref General = Ref.Create("eventos.localidad", "evt-1/GEN");
    private static readonly Ref Vip = Ref.Create("eventos.localidad", "evt-1/VIP");

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "precio-vigencia-" + Guid.NewGuid().ToString("N"));
    private readonly Reloj _reloj = new();
    private readonly PricingService _precios;

    public PrecioConVigenciaTests()
    {
        var opciones = Options.Create(new PricingStorageOptions { Root = _raiz });
        _precios = new PricingService(
            new FileSystemPriceStore(opciones), new FileSystemPromotionStore(opciones), new FileIdempotencyLedger(_raiz), _reloj);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* un temporal */ }
    }

    private sealed class Reloj : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = Abre;
        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private void Publicar(Ref sujeto, DateTimeOffset? desde = null, DateTimeOffset? hasta = null, int? tope = null)
        => Assert.True(_precios.SetPrice(sujeto, Money.Of(100_000m, "COP"), 0, IdempotencyKey.Of($"p:{sujeto}"), desde, hasta, tope).IsOk);

    private Result<Quote> Cotizar(params (Ref, int)[] lineas) => _precios.Quote(lineas, null);

    [Fact]
    public void Desde_incluido_vale_y_hasta_excluido_no()
    {
        Publicar(General, Abre, Cierra);

        _reloj.Ahora = Abre;
        Assert.True(Cotizar((General, 1)).IsOk);
        _reloj.Ahora = Cierra.AddTicks(-1);
        Assert.True(Cotizar((General, 1)).IsOk);

        foreach (var fuera in new[] { Abre.AddTicks(-1), Cierra, Cierra.AddDays(1) })
        {
            _reloj.Ahora = fuera;
            var r = Cotizar((General, 1));
            Assert.False(r.IsOk, $"Cotizó a las {fuera:O}, fuera de [{Abre:O}, {Cierra:O}).");
            Assert.Equal("pricing.price_not_in_effect", r.Rejection!.Code);
            Assert.False(r.Rejection.IsTransient);
        }
    }

    [Fact]
    public void Un_precio_sin_vigencia_ni_tope_cotiza_como_siempre_y_un_extremo_solo_tambien_vale()
    {
        Publicar(General);
        Publicar(Vip, hasta: Cierra);

        _reloj.Ahora = Abre.AddYears(-3);
        Assert.True(Cotizar((General, 50), (Vip, 50)).IsOk);
        _reloj.Ahora = Cierra;
        Assert.True(Cotizar((General, 50)).IsOk);
        Assert.Equal("pricing.price_not_in_effect", Cotizar((Vip, 1)).Rejection!.Code);
    }

    [Fact]
    public void El_tope_es_por_sujeto_sumando_todas_sus_lineas()
    {
        Publicar(General, tope: 4);
        Publicar(Vip);

        Assert.True(Cotizar((General, 2), (General, 2), (Vip, 9)).IsOk);
        Assert.Equal("pricing.quantity_over_limit", Cotizar((General, 2), (General, 3)).Rejection!.Code);
        Assert.Equal("pricing.quantity_over_limit", Cotizar((General, 1), (General, 1), (General, 1), (General, 1), (General, 1)).Rejection!.Code);
        Assert.Equal("pricing.quantity_over_limit", Cotizar((General, 5)).Rejection!.Code);
    }

    [Fact]
    public void Una_vigencia_invertida_o_un_tope_que_no_es_positivo_no_se_publican()
    {
        var invertida = _precios.SetPrice(General, Money.Of(1m, "COP"), 0, IdempotencyKey.Of("a"), Cierra, Abre);
        var vacia = _precios.SetPrice(General, Money.Of(1m, "COP"), 0, IdempotencyKey.Of("b"), Abre, Abre);
        var tope = _precios.SetPrice(General, Money.Of(1m, "COP"), 0, IdempotencyKey.Of("c"), maxPerQuote: 0);

        Assert.Equal("pricing.bad_validity", invertida.Rejection!.Code);
        Assert.Equal("pricing.bad_validity", vacia.Rejection!.Code);
        Assert.Equal("pricing.bad_limit", tope.Rejection!.Code);
    }

    [Fact]
    public async Task Contra_el_host_real_la_vigencia_se_publica_y_fuera_de_ella_no_se_cotiza()
    {
        using var arnes = new ArnesDeCapacidades().Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing");
        using var http = arnes.CreateClient("pricing");
        var ayer = DateTimeOffset.UtcNow.AddDays(-2);

        using var publicar = new HttpRequestMessage(HttpMethod.Post, "v1/prices")
        {
            Content = JsonContent.Create(new
            {
                subjectKind = "eventos.localidad", subjectId = "evt-9/GEN",
                amount = new { amount = 50_000m, currency = "COP" },
                validFrom = ayer, validTo = ayer.AddDays(1), maxPerQuote = 4,
            }),
        };
        publicar.Headers.Add("Idempotency-Key", "precio-viejo");
        using var publicado = await http.SendAsync(publicar);
        Assert.Equal(HttpStatusCode.Created, publicado.StatusCode);
        var precio = await publicado.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, precio.GetProperty("maxPerQuote").GetInt32());
        Assert.Equal(ayer.AddDays(1), precio.GetProperty("validTo").GetDateTimeOffset());

        using var cotizado = await http.PostAsJsonAsync("v1/quotes", new
        {
            lines = new[] { new { subjectKind = "eventos.localidad", subjectId = "evt-9/GEN", quantity = 1 } },
        });
        Assert.Equal(HttpStatusCode.Conflict, cotizado.StatusCode);
        Assert.Equal("pricing.price_not_in_effect",
            (await cotizado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
