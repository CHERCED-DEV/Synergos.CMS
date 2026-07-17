namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam de detección de carts abandonados (ADR 0044 — Ola 81). Cada
/// vez que <see cref="ICartService"/> muta el cart, el caller llama
/// <see cref="MarkActivity"/> para refrescar el timestamp. Un
/// <c>IHostedService</c> de fondo invoca <see cref="DetectAbandoned"/>
/// periódicamente y emite eventos de analítica para cada cart sin
/// actividad en > N tiempo.
/// </summary>
/// <remarks>
/// Implementación por defecto in-memory en
/// <c>Synergos.CMS.Web.Services.InMemoryCartAbandonmentTracker</c> —
/// ConcurrentDictionary&lt;cartId, ActivityRecord&gt;. Para multi-instancia
/// bajo load balancer, swap por adapter sobre Redis/SQL.
///
/// Cart abandonment != cart roto: el cart sigue en la cookie del
/// visitante. "Abandonado" significa solo que el visitante no
/// completó checkout en la ventana esperada; el flag de evento es
/// señal para marketing automation o el operador.
/// </remarks>
public interface ICartAbandonmentTracker
{
    /// <summary>
    /// Registra actividad reciente del cart con el id indicado. Si el
    /// id ya existía, refresca el timestamp y la metadata.
    /// </summary>
    void MarkActivity(string cartId, int itemCount, decimal subtotal, string currency);

    /// <summary>
    /// Marca el cart como completado (post-checkout) para que NO se
    /// reporte como abandonado. Idempotente.
    /// </summary>
    void MarkCompleted(string cartId);

    /// <summary>
    /// Devuelve los carts cuya última actividad excede
    /// <paramref name="threshold"/> y aún no fueron reportados como
    /// abandonados. Marca cada uno reportado para no repetir.
    /// </summary>
    IReadOnlyList<AbandonedCart> DetectAbandoned(TimeSpan threshold);
}

/// <summary>
/// Snapshot de un cart que el tracker considera abandonado.
/// </summary>
/// <param name="CartId">Id del cart (típicamente hash de la cookie).</param>
/// <param name="ItemCount">Items que tenía cuando se reportó.</param>
/// <param name="Subtotal">Subtotal del cart en la moneda indicada.</param>
/// <param name="Currency">ISO 4217 (ej. COP, USD).</param>
/// <param name="LastActivityUtc">Última actividad detectada del cart.</param>
public sealed record AbandonedCart(
    string CartId,
    int ItemCount,
    decimal Subtotal,
    string Currency,
    DateTime LastActivityUtc);
