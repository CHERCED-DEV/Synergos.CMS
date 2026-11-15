using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La fuente del catálogo de la red lee el stream una sola vez (012-blogs-social, el catálogo lento): el stream del CMS
/// lee todo su almacén en cada página, así que la cuenta de lecturas es el costo. Con 1336 publicaciones y páginas de
/// cien, Explorar hacía catorce lecturas enteras por llamada y tardaba de 14 a 27 s en la base real.
/// </summary>
public sealed class FuenteDeLaRedTests
{
    /// <summary>Un stream que pagina como el del CMS y cuenta cuántas veces lo leen.</summary>
    private sealed class StreamQueCuenta(int cuantas) : IContentStream
    {
        public int Lecturas { get; private set; }

        public Task<ContentStreamPage> GetFeedAsync(FeedQuery query, CancellationToken cancellationToken = default)
        {
            Lecturas++;
            var desde = int.TryParse(query.Cursor, out var d) ? d : 0;
            var pagina = Enumerable.Range(desde, Math.Max(0, Math.Min(query.PageSize, cuantas - desde)))
                .Select(i => new ContentStreamItem(
                    $"p-{i}", "post", new ContentAuthor("a", "a", "A", null), $"Cuerpo {i}", null,
                    new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), new ContentMetrics()))
                .ToList();
            var siguiente = desde + pagina.Count;
            return Task.FromResult(new ContentStreamPage(pagina, siguiente < cuantas ? siguiente.ToString() : null));
        }

        public Task<ContentStreamItem?> GetItemAsync(string id, CancellationToken cancellationToken = default)
            => Task.FromResult<ContentStreamItem?>(null);

        public Task<ContentStreamItem> CreateAsync(NewContentItem item, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task Las_1336_publicaciones_de_la_base_real_salen_de_una_sola_lectura()
    {
        var stream = new StreamQueCuenta(1336);

        var todas = await new FuenteDeLaRed(stream).GetAllAsync();

        Assert.Equal(1336, todas.Count);
        Assert.Equal(1, stream.Lecturas);
    }

    [Fact]
    public async Task Un_stream_mas_grande_que_una_lectura_sigue_entero_y_con_tope()
    {
        var stream = new StreamQueCuenta(FuenteDeLaRed.PorPagina + 10);

        var todas = await new FuenteDeLaRed(stream).GetAllAsync();

        Assert.Equal(FuenteDeLaRed.PorPagina + 10, todas.Count);
        Assert.Equal(2, stream.Lecturas);
    }
}
