using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Alquiler.Clients;
using Synergos.Bff.Alquiler.Domain;
using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La oferta de un equipo (línea 005, T008; lote 3, 005-P1): lo que el CMS publica para que la renta por la
/// puerta cotice con <c>Api.Pricing</c> y aparte en <c>Api.Booking</c>, en vez de un total que mande el navegador.
/// </summary>
/// <remarks>
/// Contra las capacidades REALES (<see cref="ArnesDeCapacidades"/>): lo que se prueba es que lo publicado es lo
/// que la capacidad devuelve después —el precio que cotiza Pricing, la capacidad que tiene el recurso—, no lo
/// que el orquestador cree haber mandado. Cada publicación lleva su llave, como la manda el CMS.
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

        public AlquilerCapabilities Clientes { get; }

        public Banco()
        {
            Clientes = new AlquilerCapabilities(Capacidades);
            Ofertas = new OfertaDeAlquiler(Clientes,
                Options.Create(new SagaStorageOptions { Root = _raiz }), TimeProvider.System, NullLogger<OfertaDeAlquiler>.Instance);
        }

        /// <summary>Una publicación, con su llave: una nueva cada vez, como la manda el CMS al publicar la ficha.</summary>
        public Task<Result<OfertaDeAlquilerRegistrada>> Publicar(OfertaDeEquipo oferta, string? llave = null)
            => Ofertas.PublicarAsync(oferta, IdempotencyKey.Of(llave ?? "oferta-" + Guid.NewGuid().ToString("n")), CancellationToken.None);

        /// <summary>Aparta y confirma una unidad del equipo en esa ventana, como lo haría una renta.</summary>
        public async Task<string> Reservar(string equipo, TimeWindow ventana, string quien)
        {
            var recurso = await Clientes.FindResourceBySubjectAsync(RentalSubject.For(equipo), CancellationToken.None);
            var apartado = await Clientes.HoldAsync(recurso.Value.Id, ventana, Ref.Create("alquiler.arrendatario", quien),
                IdempotencyKey.Of($"hold-{quien}"), CancellationToken.None);
            Assert.True(apartado.IsOk, apartado.Rejection?.ToString());
            var reserva = await Clientes.ConfirmHoldAsync(apartado.Value.Id, IdempotencyKey.Of($"confirm-{quien}"), CancellationToken.None);
            Assert.True(reserva.IsOk, reserva.Rejection?.ToString());
            return reserva.Value.Id;
        }

        /// <summary>En qué estado está una reserva en la agenda.</summary>
        public async Task<string?> EstadoDeLaReserva(string id)
        {
            using var http = Capacidades.CreateClient("booking");
            var cuerpo = await http.GetFromJsonAsync<JsonElement>($"v1/reservations/{id}");
            return cuerpo.GetProperty("status").GetString();
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
    private static OfertaDeEquipo Andamio(int unidades = 3, decimal valorDia = 40_000m, decimal garantia = 250_000m)
        => new("andamio-6m", "COP", valorDia, [new TramoDeTarifa(7, 30_000m), new TramoDeTarifa(28, 24_000m)],
            garantia, MinDias: 1, MaxDias: 30, unidades);

    [Fact]
    public async Task Publicar_deja_en_las_capacidades_el_precio_de_cada_tramo_la_garantia_y_las_unidades()
    {
        using var b = new Banco();

        var r = await b.Publicar(Andamio());

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

        Assert.True((await b.Publicar(Andamio(), "oferta-1")).IsOk);
        Assert.True((await b.Publicar(Andamio(), "oferta-1")).IsOk);
        Assert.True((await b.Publicar(Andamio())).IsOk);
        Assert.True((await b.Publicar(Andamio(valorDia: 42_000m))).IsOk);

        Assert.Equal(3, await b.Unidades("andamio-6m"));
        Assert.Equal(42_000m, await b.Precio(RentalSubject.KindTarifa, "andamio-6m@1"));
    }

    [Fact]
    public async Task Volver_a_una_garantia_de_antes_la_vuelve_a_fijar()
    {
        // Medido en vivo el 2026-10-22: 300 000 → 0 → 300 000 dejaba 0 en Pricing, porque volver a 300 000 repetía la
        // llave de la primera vez y Pricing contestaba aquella respuesta. La renta apartaba entonces sin garantía.
        using var b = new Banco();
        Assert.True((await b.Publicar(Andamio())).IsOk);
        Assert.True((await b.Publicar(Andamio(garantia: 0m))).IsOk);
        Assert.Equal(0m, await b.Precio(RentalSubject.KindGarantiaUnidad, "andamio-6m"));

        Assert.True((await b.Publicar(Andamio())).IsOk);

        Assert.Equal(250_000m, await b.Precio(RentalSubject.KindGarantiaUnidad, "andamio-6m"));
        Assert.Equal(250_000m, b.Ofertas.Registrada("andamio-6m")!.GarantiaUnidad);
    }

    [Fact]
    public async Task Volver_a_unas_unidades_de_antes_revisa_otra_vez()
    {
        using var b = new Banco();
        foreach (var unidades in new[] { 3, 4, 3, 4 })
        {
            var r = await b.Publicar(Andamio(unidades: unidades));
            Assert.True(r.IsOk, r.Rejection?.ToString());
            Assert.Equal(unidades, await b.Unidades("andamio-6m"));
        }
    }

    [Fact]
    public async Task Por_el_endpoint_cada_publicacion_del_CMS_vuelve_a_fijar_la_garantia()
    {
        // El orquestador toma la llave de la publicación de la cabecera: antes la leía y la tiraba.
        using var r = new RentaDeAlquilerReal();
        foreach (var (garantia, llave) in new[] { (250_000m, "oferta-1"), (0m, "oferta-2"), (250_000m, "oferta-3") })
        {
            var (estado, cuerpo) = await r.PublicarOferta(garantia: garantia, llave: llave);
            Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        }

        Assert.Equal(250_000m, await r.Total(("alquiler.garantia-unidad", RentaDeAlquilerReal.Equipo, 1)));
    }

    [Fact]
    public async Task Otras_unidades_revisan_el_recurso_y_la_oferta_queda_con_ellas()
    {
        // NN-6: la agenda del núcleo revisa la capacidad de un recurso ya creado (`revise`).
        using var b = new Banco();
        Assert.True((await b.Publicar(Andamio(unidades: 3))).IsOk);

        var r = await b.Publicar(Andamio(unidades: 4));

        Assert.True(r.IsOk, r.Rejection?.ToString());
        Assert.Equal(4, b.Ofertas.Registrada("andamio-6m")!.Unidades);
        Assert.Equal(4, await b.Unidades("andamio-6m"));
        Assert.Empty(r.Value.ReservasFuera!);
    }

    [Fact]
    public async Task Bajar_las_unidades_no_cancela_nada_y_deja_con_nombre_lo_que_ya_no_cabe()
    {
        using var b = new Banco();
        Assert.True((await b.Publicar(Andamio(unidades: 3))).IsOk);
        var ventana = TimeWindow.Of(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 8, 0, 0, 0, TimeSpan.Zero));
        var reservas = new[] { await b.Reservar("andamio-6m", ventana, "m-1"), await b.Reservar("andamio-6m", ventana, "m-2"),
            await b.Reservar("andamio-6m", ventana, "m-3") };

        var r = await b.Publicar(Andamio(unidades: 1));

        Assert.True(r.IsOk, r.Rejection?.ToString());
        Assert.Equal(1, await b.Unidades("andamio-6m"));
        // Quedan fuera las confirmadas DESPUÉS: la primera en llegar conserva su lugar.
        Assert.Equal(reservas[1..].Order(), r.Value.ReservasFuera!.Order());
        foreach (var id in reservas) Assert.Equal("Confirmed", await b.EstadoDeLaReserva(id));

        // Republicar sin cambiar las unidades no revisa otra vez, y lo que se sabe de la última no se borra.
        var otra = await b.Publicar(Andamio(unidades: 1, valorDia: 41_000m));
        Assert.Equal(reservas[1..].Order(), otra.Value.ReservasFuera!.Order());
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

        var r = await b.Publicar(oferta);

        Assert.False(r.IsOk);
        Assert.StartsWith("alquiler.offer_", r.Rejection!.Code, StringComparison.Ordinal);
        Assert.Null(await b.Unidades("andamio-6m"));
        Assert.Null(b.Ofertas.Registrada("andamio-6m"));
    }

    [Fact]
    public async Task Retirar_la_deja_registrada_como_retirada_sin_borrar_nada()
    {
        using var b = new Banco();
        Assert.True((await b.Publicar(Andamio())).IsOk);

        var r = await b.Ofertas.RetirarAsync("andamio-6m", CancellationToken.None);

        Assert.True(r.IsOk);
        Assert.True(b.Ofertas.Registrada("andamio-6m")!.Retirada);
        Assert.Equal(3, await b.Unidades("andamio-6m"));
    }
}
