using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="FileSystemCatalogSocialProof"/> (seam <c>ICatalogSocialProof</c>, T10):
/// empty (sin reseñas → <c>null</c>, NO un agregado en cero) / happy (media y conteo
/// derivados) / filter (cada SKU ve solo lo suyo) / idempotente (el mismo autor EDITA su
/// reseña, no la duplica). ADR 0075.
/// </summary>
public class FileSystemCatalogSocialProofTests
{
    /// <summary>
    /// Store en memoria con la semántica del real: <c>ReadAsync</c> de una clave que no existe
    /// devuelve <c>null</c>, y <c>WriteAsync</c> sustituye.
    /// </summary>
    private sealed class InMemoryJsonEntityStore : IJsonEntityStore
    {
        private readonly Dictionary<string, string> _data = new(StringComparer.Ordinal);

        public int Writes { get; private set; }

        public Task WriteAsync(string resourceType, string key, string json, CancellationToken cancellationToken = default)
        {
            Writes++;
            _data[resourceType + "/" + key] = json;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_data.TryGetValue(resourceType + "/" + key, out var json) ? json : null);

        public Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(_data.Keys
                .Where(k => k.StartsWith(resourceType + "/", StringComparison.Ordinal))
                .Select(k => k[(resourceType.Length + 1)..])
                .ToList());

        public Task<bool> DeleteAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_data.Remove(resourceType + "/" + key));
    }

    private static FileSystemCatalogSocialProof Make(IJsonEntityStore store)
        => new(store, NullLogger<FileSystemCatalogSocialProof>.Instance);

    private static CustomerReview Review(
        string sku = "SKU-1",
        Guid? author = null,
        int rating = 5,
        string title = "Muy bueno",
        int day = 1)
        => new(sku, author ?? Guid.NewGuid(), "Ana Torres", rating, title, "Cumple lo que promete.",
            new DateOnly(2026, 7, day));

    // ── empty ────────────────────────────────────────────────────────────────
    // El caso que de verdad importa: sin reseñas NO hay agregado. Si esto devolviera
    // (0, 0) la tarjeta pintaría "0,0" en todo el catálogo — la columna muerta que el
    // proyecto ya arregló una vez en las facetas (ADR 0112).
    [Fact]
    public async Task Sin_resenas_el_agregado_es_null_no_un_cero()
    {
        var sut = Make(new InMemoryJsonEntityStore());

        Assert.Null(await sut.GetAsync("SKU-SIN-RESENAS"));
        Assert.Empty(await sut.GetReviewsAsync("SKU-SIN-RESENAS"));
    }

    [Fact]
    public async Task Un_sku_vacio_no_revienta_ni_toca_el_store()
    {
        var store = new InMemoryJsonEntityStore();
        var sut = Make(store);

        Assert.Null(await sut.GetAsync(""));
        Assert.Null(await sut.GetAsync("   "));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Un_json_corrupto_degrada_a_sin_resenas_en_vez_de_tumbar_el_catalogo()
    {
        var store = new InMemoryJsonEntityStore();
        await store.WriteAsync("reviews", "SKU-1", "{ esto no es json valido", CancellationToken.None);

        Assert.Null(await Make(store).GetAsync("SKU-1"));
    }

    // ── happy ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Deriva_media_y_conteo_de_las_resenas()
    {
        var store = new InMemoryJsonEntityStore();
        var sut = Make(store);
        await sut.UpsertReviewAsync(Review(rating: 5));
        await sut.UpsertReviewAsync(Review(rating: 4));
        await sut.UpsertReviewAsync(Review(rating: 4));

        var proof = await sut.GetAsync("SKU-1");

        Assert.NotNull(proof);
        Assert.Equal(3, proof!.Count);
        Assert.Equal(4.3, proof.Average);       // 13/3 = 4.333… → una decimal
    }

    [Fact]
    public async Task Las_resenas_salen_de_la_mas_reciente_a_la_mas_antigua()
    {
        var sut = Make(new InMemoryJsonEntityStore());
        await sut.UpsertReviewAsync(Review(title: "vieja", day: 1));
        await sut.UpsertReviewAsync(Review(title: "nueva", day: 20));

        var reviews = await sut.GetReviewsAsync("SKU-1");

        Assert.Equal(new[] { "nueva", "vieja" }, reviews.Select(r => r.Title));
    }

    [Fact]
    public async Task Un_rating_fuera_de_rango_se_rechaza_en_vez_de_recortarse()
    {
        var sut = Make(new InMemoryJsonEntityStore());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.UpsertReviewAsync(Review(rating: 0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => sut.UpsertReviewAsync(Review(rating: 6)));
        Assert.Null(await sut.GetAsync("SKU-1"));
    }

    [Fact]
    public async Task Una_resena_sin_autor_se_rechaza()
    {
        // Sin memberKey no hay idempotencia ni forma de comprobar la compra.
        var sut = Make(new InMemoryJsonEntityStore());

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.UpsertReviewAsync(Review(author: Guid.Empty)));
    }

    // ── filter ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task Cada_sku_ve_solo_sus_propias_resenas()
    {
        var sut = Make(new InMemoryJsonEntityStore());
        await sut.UpsertReviewAsync(Review(sku: "SKU-1", rating: 5));
        await sut.UpsertReviewAsync(Review(sku: "SKU-2", rating: 2));
        await sut.UpsertReviewAsync(Review(sku: "SKU-2", rating: 2));

        var uno = await sut.GetAsync("SKU-1");
        var dos = await sut.GetAsync("SKU-2");

        Assert.Equal(1, uno!.Count);
        Assert.Equal(5, uno.Average);
        Assert.Equal(2, dos!.Count);
        Assert.Equal(2, dos.Average);
    }

    // ── idempotente ──────────────────────────────────────────────────────────
    // Un comprador tiene UNA reseña por producto: reenviarla la EDITA. Sin esto, pulsar
    // "enviar" dos veces inflaría el conteo y movería la media.
    [Fact]
    public async Task El_mismo_autor_edita_su_resena_en_vez_de_duplicarla()
    {
        var autor = Guid.NewGuid();
        var sut = Make(new InMemoryJsonEntityStore());
        await sut.UpsertReviewAsync(Review(author: autor, rating: 5, title: "Excelente"));
        await sut.UpsertReviewAsync(Review(author: autor, rating: 2, title: "Me arrepentí"));

        var proof = await sut.GetAsync("SKU-1");
        var reviews = await sut.GetReviewsAsync("SKU-1");

        Assert.Equal(1, proof!.Count);
        Assert.Equal(2, proof.Average);
        Assert.Equal("Me arrepentí", Assert.Single(reviews).Title);
    }

    [Fact]
    public async Task Autores_distintos_sobre_el_mismo_sku_SI_suman()
    {
        // El control del test de arriba: si la idempotencia estuviera de más (colapsando
        // por SKU en vez de por autor), este caso caería a 1 y nadie se enteraría.
        var sut = Make(new InMemoryJsonEntityStore());
        await sut.UpsertReviewAsync(Review(author: Guid.NewGuid(), rating: 5));
        await sut.UpsertReviewAsync(Review(author: Guid.NewGuid(), rating: 3));

        Assert.Equal(2, (await sut.GetAsync("SKU-1"))!.Count);
    }
}
