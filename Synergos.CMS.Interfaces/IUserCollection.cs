namespace Synergos.CMS.Interfaces;

/// <summary>
/// Colecciones de ítems por usuario (Member) — seam GENÉRICO (plan doc 21
/// §1.4 P11): favoritos, wishlist, listas nombradas, shortlists, bookmarks y
/// saved-searches son TODAS instancias del mismo contrato (owner + nombre de
/// colección + refs de ítems). Un contrato, N dominios: Tienda (wishlist/
/// listas de regalo), Booking (wishlists de estadías), Propiedades
/// (shortlist), Blogs (guardados), Educación (cursos guardados), Eventos
/// (interesado en) — evita duplicar IWishlistService + IFavoriteService +
/// IReadingListService por dominio.
/// </summary>
/// <remarks>
/// Seam stub-first (ADR 0075): el default <c>StubUserCollection</c>
/// (Application, lógica pura — ADR 0002) guarda el estado en memoria del
/// proceso; un adapter real delega a DB/perfil del Member sin tocar los
/// consumidores. <c>owner</c> es el identificador estable del usuario (email
/// del Member, igual que el historial de <see cref="IShopOrderService"/>);
/// <c>collection</c> es el nombre lógico de la colección (e.g. "wishlist",
/// "regalos-navidad"); <c>itemRef</c> es la referencia opaca al ítem del
/// dominio (sku, listingId, courseId, postId…) — el seam NO interpreta el
/// ref (agnóstico del dominio). <see cref="AddAsync"/> es idempotente:
/// re-agregar el mismo ítem no lo duplica.
/// </remarks>
public interface IUserCollection
{
    /// <summary>
    /// Agrega <paramref name="itemRef"/> a la colección
    /// <paramref name="collection"/> de <paramref name="owner"/> (la crea si
    /// no existe). Idempotente: si el ítem ya está, devuelve el existente sin
    /// duplicar ni cambiar su fecha. Lanza <see cref="ArgumentException"/> si
    /// owner/collection/itemRef vienen vacíos.
    /// </summary>
    Task<UserCollectionItem> AddAsync(
        string owner,
        string collection,
        string itemRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Quita <paramref name="itemRef"/> de la colección. Devuelve
    /// <c>true</c> si el ítem estaba y se quitó; <c>false</c> si no estaba
    /// (idempotente — quitar dos veces no lanza).
    /// </summary>
    Task<bool> RemoveAsync(
        string owner,
        string collection,
        string itemRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve los ítems de una colección del usuario, del más reciente al
    /// más antiguo. Lista vacía si la colección no existe o está vacía.
    /// </summary>
    Task<IReadOnlyList<UserCollectionItem>> GetAsync(
        string owner,
        string collection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve el resumen de TODAS las colecciones del usuario (nombre +
    /// conteo + última actividad), de la más recientemente tocada a la más
    /// antigua. Lista vacía si el usuario no tiene colecciones.
    /// </summary>
    Task<IReadOnlyList<UserCollectionSummary>> GetCollectionsAsync(
        string owner,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Un ítem guardado en una colección de usuario: a quién pertenece, en qué
/// colección vive, la referencia opaca al ítem del dominio y cuándo se agregó.
/// </summary>
public sealed record UserCollectionItem(
    string Owner,
    string Collection,
    string ItemRef,
    DateTimeOffset AddedAt);

/// <summary>
/// Resumen de una colección de usuario para la vista "mis listas": nombre +
/// cuántos ítems tiene + cuándo fue la última actividad (agregar/quitar).
/// </summary>
public sealed record UserCollectionSummary(
    string Collection,
    int Count,
    DateTimeOffset UpdatedAt);
