using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Alquiler.Clients;
using Synergos.Bff.Alquiler.Domain;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La oferta de un equipo (línea 005, T008; lote 3, 005-P1): lo que el CMS publica para que la renta por la
/// puerta cotice con <c>Api.Pricing</c> y aparte en <c>Api.Booking</c>, en vez de un total que mande el navegador.
/// </summary>
/// <remarks>
/// Contra las capacidades REALES (<see cref="ArnesDeCapacidades"/>): lo que se prueba es que lo publicado es lo
/// que la capacidad devuelve después —el precio que cotiza Pricing, la capacidad que tiene el recurso—, no lo
/// que el orquestador cree haber mandado.
/// </remarks>
public sealed class OfertaDeAlquilerTests
{
    private sealed class Banco : IDisposable
    {
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "oferta-alquiler-" + Guid.NewGuid().ToString("N"));

        public ArnesDeCapacidades Capacidades { get; } = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking")
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing");

        public OfertaDeAlquiler Ofertas { get; }

        public Banco()
        {
            Ofertas = new OfertaDeAlquiler(new AlquilerCapabilities(Capacidades),
                Options.Create(new SagaStorageOptions { Root = _raiz }), TimeProvider.System, NullLogger<OfertaDeAlquiler>.Instance);
        }

        /// <summary>Lo que Pricing cotiza HOY para una unidad de ese sujeto.</summary>
        public async Task<decimal?> Precio(string kind, string id)
        {
            using var http = Capacidades.CreateClient("pricing");
            using var r = await http.PostAsJsonAsync("v1/quotes", new { lines = new[] { new { subjectKind = kind, subjectId = id, quantity = 1 } } });
            if (!r.IsSuccessStatusCode) return null;
            var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>();
            return cuerpo.GetProperty("total").GetProperty("amount").GetDecimal();
        }

        /// <summary>Las unidades del recurso del equipo en la agenda, o null si no hay recurso.</summary>
        public async Task<int?> Unidades(string equipo)
        {
            using var http = Capacidades.CreateClient("booking");
            using var r = await http.GetAsync($"v1/resources/por-sujeto?subjectKind=alquiler.equipo&subjectId={equipo}");
            if (r.StatusCode == HttpStatusCode.NotFound) return null;
            var cuerpo = await r.Content.ReadFromJsonAsync<JsonElement>();
            return cuerpo.GetProperty("capacity").GetInt32();
        }

        public void Dispose()
        {
            Ofertas.Dispose();
            Capacidades.Dispose();
            try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }
    }

    // Tarifa base 40 000; desde 7 días, 30 000; desde 28, 24 000: los tres distintos a propósito, para que
    // publicar un tramo con el valor de otro no pase en verde.
    private static OfertaDeEquipo Andamio(int unidades = 3, decimal valorDia = 40_000m)
        => new("andamio-6m", "COP", valorDia, [new TramoDeTarifa(7, 30_000m), new TramoDeTarifa(28, 24_000m)],
            250_000m, MinDias: 1, MaxDias: 30, unidades);

    [Fact]
    public async Task Publicar_deja_en_las_capacidades_el_precio_de_cada_tramo_la_garantia_y_las_unidades()
    {
        using var b = new Banco();

        var r = await b.Ofertas.PublicarAsync(Andamio(), CancellationToken.None);

        Assert.True(r.IsOk, r.Rejection?.ToString());
        Assert.Equal(40_000m, await b.Precio(RentalSubject.KindTarifa, "andamio-6m@1"));
        Assert.Equal(30_000m, await b.Precio(RentalSubject.KindTarifa, "andamio-6m@7"));
        Assert.Equal(24_000m, await b.Precio(RentalSubject.KindTarifa, "andamio-6m@28"));
        Assert.Equal(250_000m, await b.Precio(RentalSubject.KindGarantiaUnidad, "andamio-6m"));
        Assert.Equal(3, await b.Unidades("andamio-6m"));

        var registrada = b.Ofertas.Registrada("andamio-6m");
        Assert.NotNull(registrada);
        Assert.Equal([7, 28], registrada.Tramos.Select(t => t.DesdeDias));
        Assert.False(registrada.Retirada);
    }

    [Fact]
    public async Task Republicar_lo_mismo_no_duplica_y_cambiar_un_precio_lo_vuelve_a_fijar()
    {
        using var b = new Banco();

        Assert.True((await b.Ofertas.PublicarAsync(Andamio(), CancellationToken.None)).IsOk);
        Assert.True((await b.Ofertas.PublicarAsync(Andamio(), CancellationToken.None)).IsOk);
        Assert.True((await b.Ofertas.PublicarAsync(Andamio(valorDia: 42_000m), CancellationToken.None)).IsOk);

        Assert.Equal(3, await b.Unidades("andamio-6m"));
        Assert.Equal(42_000m, await b.Precio(RentalSubject.KindTarifa, "andamio-6m@1"));
    }

    [Fact]
    public async Task Otras_unidades_se_rechazan_y_queda_la_oferta_anterior()
    {
        using var b = new Banco();
        Assert.True((await b.Ofertas.PublicarAsync(Andamio(unidades: 3), CancellationToken.None)).IsOk);

        var r = await b.Ofertas.PublicarAsync(Andamio(unidades: 4), CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.Equal("alquiler.units_change_unsupported", r.Rejection!.Code);
        Assert.Equal(3, b.Ofertas.Registrada("andamio-6m")!.Unidades);
        Assert.Equal(3, await b.Unidades("andamio-6m"));
    }

    [Theory]
    [InlineData("valor-cero")]
    [InlineData("tramo-uno")]
    [InlineData("tramo-repetido")]
    [InlineData("maximo-menor")]
    [InlineData("sin-unidades")]
    public async Task Una_oferta_mal_formada_se_rechaza_sin_escribir_nada(string defecto)
    {
        using var b = new Banco();
        var oferta = defecto switch
        {
            "valor-cero" => Andamio(valorDia: 0m),
            "tramo-uno" => Andamio() with { Tramos = [new TramoDeTarifa(1, 30_000m)] },
            "tramo-repetido" => Andamio() with { Tramos = [new TramoDeTarifa(7, 30_000m), new TramoDeTarifa(7, 29_000m)] },
            "maximo-menor" => Andamio() with { MinDias = 5, MaxDias = 4 },
            _ => Andamio(unidades: 0),
        };

        var r = await b.Ofertas.PublicarAsync(oferta, CancellationToken.None);

        Assert.False(r.IsOk);
        Assert.StartsWith("alquiler.offer_", r.Rejection!.Code, StringComparison.Ordinal);
        Assert.Null(await b.Unidades("andamio-6m"));
        Assert.Null(b.Ofertas.Registrada("andamio-6m"));
    }

    [Fact]
    public async Task Retirar_la_deja_registrada_como_retirada_sin_borrar_nada()
    {
        using var b = new Banco();
        Assert.True((await b.Ofertas.PublicarAsync(Andamio(), CancellationToken.None)).IsOk);

        var r = await b.Ofertas.RetirarAsync("andamio-6m", CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.True(b.Ofertas.Registrada("andamio-6m")!.Retirada);
        Assert.Equal(3, await b.Unidades("andamio-6m"));
    }
}
