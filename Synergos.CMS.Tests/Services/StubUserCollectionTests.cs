using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubUserCollection"/> (seam GENÉRICO <see cref="IUserCollection"/>,
/// plan doc 21 §1.4 P11 — favoritos/wishlist/listas/saved-searches por Member):
/// los 4 casos canónicos (ADR 0075) — empty / happy / filter / idempotent.
/// </summary>
public class StubUserCollectionTests
{
    private const string Owner = "camila@synergos.co";

    private static IUserCollection Make(Func<DateTimeOffset>? now = null)
        => new StubUserCollection(now);

    [Fact] // empty: usuario sin colecciones → listas vacías (sin lanzar)
    public async Task Get_UnknownOwnerOrCollection_ReturnsEmpty()
    {
        var svc = Make();

        Assert.Empty(await svc.GetAsync(Owner, "wishlist"));
        Assert.Empty(await svc.GetCollectionsAsync(Owner));
        Assert.Empty(await svc.GetAsync("", "wishlist"));
        Assert.Empty(await svc.GetCollectionsAsync(""));
    }

    [Fact] // inválido: argumentos vacíos en Add/Remove lanzan
    public async Task AddOrRemove_BlankArguments_Throw()
    {
        var svc = Make();

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AddAsync("", "wishlist", "sku-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.AddAsync(Owner, "", "sku-1"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.AddAsync(Owner, "wishlist", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.RemoveAsync(Owner, "wishlist", " "));
    }

    [Fact] // happy: agregar → aparece en la colección + en el resumen de colecciones
    public async Task Add_ThenGet_ReturnsItemAndSummary()
    {
        var clock = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
        var svc = Make(() => clock);

        var added = await svc.AddAsync(Owner, "wishlist", "tec-laptop-pro-14");

        Assert.Equal(Owner, added.Owner);
        Assert.Equal("wishlist", added.Collection);
        Assert.Equal("tec-laptop-pro-14", added.ItemRef);
        Assert.Equal(clock, added.AddedAt);

        var items = await svc.GetAsync(Owner, "wishlist");
        Assert.Single(items);
        Assert.Equal("tec-laptop-pro-14", items[0].ItemRef);

        var collections = await svc.GetCollectionsAsync(Owner);
        Assert.Single(collections);
        Assert.Equal("wishlist", collections[0].Collection);
        Assert.Equal(1, collections[0].Count);
        Assert.Equal(clock, collections[0].UpdatedAt);
    }

    [Fact] // happy: los ítems salen del más reciente al más antiguo
    public async Task Get_ReturnsNewestFirst()
    {
        var clock = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
        var svc = Make(() => clock);

        await svc.AddAsync(Owner, "wishlist", "sku-a");
        clock = clock.AddMinutes(5);
        await svc.AddAsync(Owner, "wishlist", "sku-b");

        var items = await svc.GetAsync(Owner, "wishlist");
        Assert.Equal(new[] { "sku-b", "sku-a" }, items.Select(i => i.ItemRef).ToArray());
    }

    [Fact] // filter: cada (owner, colección) está aislado del resto
    public async Task Collections_AreScopedPerOwnerAndName()
    {
        var svc = Make();
        await svc.AddAsync(Owner, "wishlist", "sku-a");
        await svc.AddAsync(Owner, "regalos-navidad", "sku-b");
        await svc.AddAsync("otro@synergos.co", "wishlist", "sku-c");

        var wishlist = await svc.GetAsync(Owner, "wishlist");
        Assert.Single(wishlist);
        Assert.Equal("sku-a", wishlist[0].ItemRef);

        var regalos = await svc.GetAsync(Owner, "regalos-navidad");
        Assert.Single(regalos);
        Assert.Equal("sku-b", regalos[0].ItemRef);

        // El otro usuario solo ve lo suyo.
        var otras = await svc.GetCollectionsAsync("otro@synergos.co");
        Assert.Single(otras);
        Assert.Equal(1, otras[0].Count);

        // Dos colecciones para Camila.
        Assert.Equal(2, (await svc.GetCollectionsAsync(Owner)).Count);
    }

    [Fact] // idempotent: re-agregar el mismo ítem no duplica ni cambia la fecha
    public async Task Add_SameItemTwice_IsIdempotent()
    {
        var clock = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
        var svc = Make(() => clock);

        var first = await svc.AddAsync(Owner, "wishlist", "sku-a");
        clock = clock.AddMinutes(10);
        // Mismo ítem con otra capitalización — misma colección/ítem (case-insensitive).
        var second = await svc.AddAsync(Owner, "Wishlist", "SKU-A");

        Assert.Equal(first.AddedAt, second.AddedAt);
        Assert.Single(await svc.GetAsync(Owner, "wishlist"));
    }

    [Fact] // idempotent: quitar dos veces → segunda devuelve false sin lanzar
    public async Task Remove_TwiceIsIdempotent_AndUpdatesCount()
    {
        var svc = Make();
        await svc.AddAsync(Owner, "wishlist", "sku-a");

        Assert.True(await svc.RemoveAsync(Owner, "wishlist", "sku-a"));
        Assert.False(await svc.RemoveAsync(Owner, "wishlist", "sku-a"));
        Assert.Empty(await svc.GetAsync(Owner, "wishlist"));

        // La colección queda (vacía) en el resumen con count 0.
        var summary = Assert.Single(await svc.GetCollectionsAsync(Owner));
        Assert.Equal(0, summary.Count);
    }
}
