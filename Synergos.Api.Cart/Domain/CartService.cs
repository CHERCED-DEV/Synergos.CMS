using Synergos.Api.Cart.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Cart.Domain;

/// <summary>Compone las reglas de <see cref="CartRules"/> con el almacén.</summary>
public sealed class CartService
{
    private readonly ICartStore _carts;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public CartService(ICartStore carts, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _carts = carts;
        _idempotency = idempotency;
        _clock = clock;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    public Result<Cart> Open(Ref owner, TimeSpan? ttl, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("cart", key) is { } yaEra)
            {
                return _carts.Find(yaEra) is { } previa
                    ? Result.Ok(previa)
                    : Rejection.Conflict($"{CartRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero la canasta no está.");
            }

            var vigencia = ttl ?? CartRules.DefaultTtl;
            var motivo = CartRules.CheckTtl(vigencia);
            if (motivo is not null) return Result.Rejected<Cart>(motivo);

            var id = Guid.NewGuid().ToString("n");
            var cart = new Cart(id, owner, Array.Empty<CartLine>(), Now, Now + vigencia);
            _carts.Put(cart);
            _idempotency.Remember("cart", key, id);
            return Result.Ok(cart);
        }
    }

    public Result<Cart> Get(string id)
        => _carts.Find(id) is { } c
            ? Result.Ok(c)
            : Rejection.NotFound($"{CartRules.CodePrefix}.cart_not_found", $"No existe la canasta {id}.");

    /// <summary>
    /// Pone una línea con la cantidad dada. Si el sujeto ya estaba, la <b>reemplaza</b>.
    /// </summary>
    /// <remarks>
    /// Reemplazar y no sumar: es lo que hace que reintentar tras un timeout no duplique la
    /// cantidad. Con semántica de suma, cada reintento agrega otra vez y el cliente termina con
    /// seis unidades de algo que pidió dos veces.
    /// </remarks>
    public Result<Cart> SetLine(string id, Ref subject, int quantity)
    {
        lock (_gate)
        {
            var cart = _carts.Find(id);
            if (cart is null)
            {
                return Rejection.NotFound($"{CartRules.CodePrefix}.cart_not_found", $"No existe la canasta {id}.");
            }

            var motivo = CartRules.CheckOpen(cart, Now)
                         ?? CartRules.CheckQuantity(quantity)
                         ?? CartRules.CheckRoom(cart, subject);
            if (motivo is not null) return Result.Rejected<Cart>(motivo);

            var lineas = cart.Lines.Where(l => l.Subject != subject).ToList();
            lineas.Add(new CartLine(subject, quantity));

            var actualizada = cart with { Lines = lineas };
            _carts.Put(actualizada);
            return Result.Ok(actualizada);
        }
    }

    public Result<Cart> RemoveLine(string id, Ref subject)
    {
        lock (_gate)
        {
            var cart = _carts.Find(id);
            if (cart is null)
            {
                return Rejection.NotFound($"{CartRules.CodePrefix}.cart_not_found", $"No existe la canasta {id}.");
            }

            var motivo = CartRules.CheckOpen(cart, Now);
            if (motivo is not null) return Result.Rejected<Cart>(motivo);

            // Quitar lo que no está NO es un error: es el resultado que el llamador quería.
            var actualizada = cart with { Lines = cart.Lines.Where(l => l.Subject != subject).ToList() };
            _carts.Put(actualizada);
            return Result.Ok(actualizada);
        }
    }

    /// <summary>Cierra la canasta. Quien crea el pedido es el BFF, no esta capacidad.</summary>
    /// <remarks>
    /// Cart no llama a Orders. Si lo hiciera, esta capacidad tendría que conocer el modelo de
    /// pedido de cada negocio — y ahí dejaría de servir al siguiente. Devuelve la canasta
    /// cerrada y con qué llevaba; el orden de los pasos es del BFF.
    /// </remarks>
    public Result<Cart> CheckOut(string id)
    {
        lock (_gate)
        {
            var cart = _carts.Find(id);
            if (cart is null)
            {
                return Rejection.NotFound($"{CartRules.CodePrefix}.cart_not_found", $"No existe la canasta {id}.");
            }

            var motivo = CartRules.CheckOpen(cart, Now);
            if (motivo is not null) return Result.Rejected<Cart>(motivo);

            if (cart.Lines.Count == 0)
            {
                return Rejection.Conflict($"{CartRules.CodePrefix}.empty_cart", "Una canasta vacía no se puede cerrar.");
            }

            var cerrada = cart with { CheckedOut = true };
            _carts.Put(cerrada);
            return Result.Ok(cerrada);
        }
    }

    public Result<Page<Cart>> ListForOwner(Ref? owner, int offset, int limit)
    {
        if (owner is null)
        {
            return Rejection.Invalid($"{CartRules.CodePrefix}.owner_required", "Hace falta filtrar por dueño.");
        }

        var todas = _carts.ForOwner(owner)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<Cart>(todas.Skip(offset).Take(limit).ToList(), todas.Count, offset));
    }
}
