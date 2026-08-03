using Synergos.Api.Cart.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Api;

/// <summary>Cubre <see cref="CartRules"/> — vigencia, cantidades y cierre.</summary>
public sealed class CartRulesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);

    private static Cart Canasta(int lineas = 0, bool cerrada = false, int venceEnH = 24)
        => new("c1", Ref.Create("identity.member", "m-1"),
            Enumerable.Range(0, lineas).Select(i => new CartLine(Ref.Create("shop.producto", $"p{i}"), 1)).ToList(),
            Ahora, Ahora.AddHours(venceEnH), cerrada);

    [Fact]
    public void Una_canasta_VENCIDA_da_Expired_y_no_NotFound()
    {
        // Al cliente le sirve saber que existió y se le venció, para abrir otra en vez de
        // buscar un identificador que escribió bien.
        Assert.Equal(RejectionKind.Expired, CartRules.CheckOpen(Canasta(venceEnH: -1), Ahora)?.Kind);
        Assert.Null(CartRules.CheckOpen(Canasta(), Ahora));
    }

    [Fact]
    public void Una_canasta_ya_CERRADA_no_admite_cambios()
    {
        Assert.Equal("cart.already_checked_out", CartRules.CheckOpen(Canasta(cerrada: true), Ahora)?.Code);
    }

    [Fact]
    public void Poner_cantidad_CERO_no_es_quitar()
    {
        // Aceptarlo haría que un error de cálculo del cliente vaciara líneas en silencio.
        var bad = CartRules.CheckQuantity(0);

        Assert.Equal("cart.bad_quantity", bad?.Code);
        Assert.Contains("/lines/remove", bad!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void La_cantidad_por_linea_tiene_tope()
    {
        Assert.Null(CartRules.CheckQuantity(CartRules.MaxQuantityPerLine));
        Assert.Equal("cart.quantity_over_limit", CartRules.CheckQuantity(CartRules.MaxQuantityPerLine + 1)?.Code);
    }

    [Fact]
    public void Una_canasta_llena_admite_ACTUALIZAR_una_linea_que_ya_tiene()
    {
        // Si el tope se aplicara también al reemplazo, una canasta llena quedaría congelada: no
        // se podría ni corregir una cantidad.
        var llena = Canasta(lineas: CartRules.MaxLines);
        var yaEsta = Ref.Create("shop.producto", "p0");
        var nueva = Ref.Create("shop.producto", "otro");

        Assert.Null(CartRules.CheckRoom(llena, yaEsta));
        Assert.Equal("cart.too_many_lines", CartRules.CheckRoom(llena, nueva)?.Code);
    }

    [Fact]
    public void La_vigencia_tiene_techo()
    {
        Assert.Null(CartRules.CheckTtl(CartRules.MaxTtl));
        Assert.Equal("cart.bad_ttl", CartRules.CheckTtl(CartRules.MaxTtl + TimeSpan.FromDays(1))?.Code);
        Assert.Equal("cart.bad_ttl", CartRules.CheckTtl(TimeSpan.Zero)?.Code);
    }
}
