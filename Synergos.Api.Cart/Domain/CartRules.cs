using Synergos.Core;

namespace Synergos.Api.Cart.Domain;

/// <summary>Lo que la canasta rechaza <b>sola</b>.</summary>
public static class CartRules
{
    public const string CodePrefix = "cart";

    /// <summary>Cuánto vive una canasta si nadie pide otra cosa.</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(7);

    /// <summary>Techo de vida. Una canasta eterna no es una canasta.</summary>
    public static readonly TimeSpan MaxTtl = TimeSpan.FromDays(30);

    /// <summary>Líneas distintas por canasta.</summary>
    public const int MaxLines = 100;

    /// <summary>Unidades de una misma línea.</summary>
    public const int MaxQuantityPerLine = 999;

    /// <summary>Si la canasta admite cambios.</summary>
    public static Rejection? CheckOpen(Cart cart, DateTimeOffset now)
    {
        if (cart.CheckedOut)
        {
            return Rejection.Conflict($"{CodePrefix}.already_checked_out",
                "La canasta ya se convirtió en pedido; los cambios van sobre el pedido.");
        }
        // Expired y no NotFound: al cliente le sirve saber que la canasta existió y se le venció,
        // para poder abrir otra en vez de buscar un identificador que escribió bien.
        return now >= cart.ExpiresAtUtc
            ? Rejection.Expired($"{CodePrefix}.expired", $"La canasta venció en {cart.ExpiresAtUtc:O}.")
            : null;
    }

    /// <summary>Si la cantidad pedida para una línea sirve.</summary>
    public static Rejection? CheckQuantity(int quantity)
    {
        if (quantity <= 0)
        {
            // Poner cero NO es quitar: para quitar está la operación de quitar. Aceptarlo acá
            // haría que un error de cálculo del cliente vaciara líneas en silencio.
            return Rejection.Invalid($"{CodePrefix}.bad_quantity",
                "La cantidad tiene que ser mayor que cero. Para quitar una línea está /lines/remove.");
        }
        return quantity > MaxQuantityPerLine
            ? Rejection.Invalid($"{CodePrefix}.quantity_over_limit", $"Hasta {MaxQuantityPerLine} unidades por línea.")
            : null;
    }

    /// <summary>Si cabe otra línea distinta.</summary>
    public static Rejection? CheckRoom(Cart cart, Ref subject)
        => cart.Lines.Any(l => l.Subject == subject) || cart.Lines.Count < MaxLines
            ? null
            : Rejection.Conflict($"{CodePrefix}.too_many_lines", $"Hasta {MaxLines} líneas distintas.");

    /// <summary>Si la vigencia pedida cabe.</summary>
    public static Rejection? CheckTtl(TimeSpan ttl)
        => ttl > TimeSpan.Zero && ttl <= MaxTtl
            ? null
            : Rejection.Invalid($"{CodePrefix}.bad_ttl", $"La vigencia va entre 0 y {MaxTtl}.");
}
