using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre las fichas de estadía servidas desde el CONTENIDO del hotelero (ADR 0119), que es lo
/// que se registra cuando <c>Synergos:Catalog:Sources:Booking = cms</c>.
/// </summary>
/// <remarks>
/// <para>La propiedad protegida es una sola y es de regresión silenciosa: <b>la referencia se
/// resuelve igual venga la estadía de donde venga</b>. <c>GetStayAsync</c> acepta tres formas
/// —<c>stayId</c>, código de tipo de habitación y <c>offerId</c> del buscador
/// ("DLX/DLX-FLEX")— y las resuelve en ese orden. Si el orden o las formas cambiaran al mover
/// el flag, cada oferta del buscador de hotel perdería su nombre, su foto y su pin sin que nada
/// fallara.</para>
///
/// <para>Aquí <b>no hay capa durable que probar</b>, y eso es la decisión, no un hueco: la seam
/// es de solo lectura, no existe un segundo autor que publique por fuera del backoffice, y un
/// overlay sin escritor sería un campo que promete algo que nadie cumple.</para>
/// </remarks>
public sealed class CatalogStayContentProviderTests
{
    private sealed class FakeSource : ICatalogSource<StayDetail>
    {
        private readonly IReadOnlyList<StayDetail> _items;

        public FakeSource(params StayDetail[] items) => _items = items;

        public int Calls { get; private set; }

        public Task<IReadOnlyList<StayDetail>> GetAllAsync(
            string? scope = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_items);
        }
    }

    private static StayDetail Stay(string stayId, params string[] roomTypeCodes)
        => new(
            StayId: stayId,
            Name: "Casa Getsemaní Boutique",
            City: "Cartagena",
            Region: "Bolívar",
            Address: "Calle de la Sierpe #29-108",
            Description: "Casa republicana restaurada.",
            Gallery: Array.Empty<string>(),
            Amenities: Array.Empty<string>(),
            Specs: Array.Empty<StaySpec>(),
            Geo: new StayGeo(10.4224, -75.5468),
            Reviews: new StayReviewSummary(9.2, 418, "Ubicación excepcional", Array.Empty<StayReviewCategoryScore>()),
            RoomTypeCodes: roomTypeCodes);

    [Fact]
    public async Task Un_catalogo_vacio_no_resuelve_nada_y_no_revienta()
    {
        var provider = new CatalogStayContentProvider(new FakeSource());

        Assert.Null(await provider.GetStayAsync("ctg-getsemani"));
    }

    [Fact]
    public async Task Resuelve_por_stayId_sin_importar_mayusculas_ni_espacios()
    {
        var provider = new CatalogStayContentProvider(new FakeSource(Stay("ctg-getsemani", "STD")));

        var stay = await provider.GetStayAsync("  CTG-Getsemani ");

        Assert.Equal("ctg-getsemani", stay!.StayId);
    }

    [Fact]
    public async Task Resuelve_por_codigo_de_tipo_de_habitacion()
    {
        var provider = new CatalogStayContentProvider(new FakeSource(Stay("med-provenza", "DLX")));

        Assert.Equal("med-provenza", (await provider.GetStayAsync("dlx"))!.StayId);
    }

    [Fact]
    public async Task Resuelve_por_offerId_del_buscador_partiendo_por_la_barra()
    {
        // "DLX/DLX-FLEX" es lo que emite el search de hotel: room type + rate plan.
        var provider = new CatalogStayContentProvider(new FakeSource(Stay("med-provenza", "DLX")));

        Assert.Equal("med-provenza", (await provider.GetStayAsync("DLX/DLX-FLEX"))!.StayId);
    }

    [Fact]
    public async Task El_stayId_gana_sobre_el_codigo_de_habitacion()
    {
        // Es el orden del stub, y no es cosmético: una propiedad cuyo stayId coincida con el
        // código de otra debe resolverse a sí misma.
        var porCodigo = Stay("ctg-bocagrande", "DLX");
        var porId = Stay("dlx", "STE");
        var provider = new CatalogStayContentProvider(new FakeSource(porCodigo, porId));

        Assert.Equal("dlx", (await provider.GetStayAsync("DLX"))!.StayId);
    }

    [Fact]
    public async Task Una_referencia_desconocida_devuelve_null_y_no_la_primera_del_catalogo()
    {
        var provider = new CatalogStayContentProvider(new FakeSource(Stay("ctg-getsemani", "STD")));

        Assert.Null(await provider.GetStayAsync("no-existe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Una_referencia_vacia_devuelve_null_sin_tocar_la_fuente(string stayRef)
    {
        // Recorrer el árbol de contenido para no comparar nada sería trabajo puro por petición.
        var source = new FakeSource(Stay("ctg-getsemani", "STD"));
        var provider = new CatalogStayContentProvider(source);

        Assert.Null(await provider.GetStayAsync(stayRef));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Sin_fuente_no_se_construye()
    {
        Assert.Throws<ArgumentNullException>(() => new CatalogStayContentProvider(null!));
        await Task.CompletedTask;
    }
}
