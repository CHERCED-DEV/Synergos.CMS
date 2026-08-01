using System.Text;
using System.Text.Json;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El catálogo de Eventos servido desde el CONTENIDO que el editor autoró, en vez del seed
/// hardcodeado del stub. Es el <see cref="IEventCatalogProvider"/> que se registra cuando
/// <c>Synergos:Catalog:Sources:Events = cms</c>.
/// </summary>
/// <remarks>
/// <b>Dos capas, y el orden entre ellas es la decisión.</b> Debajo está el contenido
/// (<see cref="ICatalogSource{T}"/> de <see cref="EventDetail"/>, que en producción es
/// <c>UmbracoEventCatalogSource</c>); encima, una capa de eventos publicados por
/// ORGANIZADORES desde la app, que no pasan por el backoffice. La capa de arriba gana: si un
/// organizador re-publica un evento que también existe como contenido, lo que se sirve es su
/// versión — publicar y no ver el cambio es peor que la ambigüedad.
///
/// <para><b>Por qué existe la capa de arriba.</b> Esta seam tiene un
/// <see cref="IEventCatalogProvider.PublishEventAsync"/> que la cara de organizador usa
/// (<c>POST /api/eventos/event</c>). Un catálogo puramente de contenido tendría tres salidas
/// y ninguna buena: lanzar (rompe una pantalla que hoy funciona), tragárselo en silencio (el
/// organizador publica y no pasa nada), o escribir nodos de Umbraco desde aquí (Application
/// no conoce Umbraco — ADR 0002 — y crear contenido publicado a espaldas del editor es una
/// decisión editorial, no técnica). La cuarta es esta: se persiste en el store genérico
/// (ADR 0105), sobrevive un reinicio, y el editor puede promoverlo a <c>eventPage</c> cuando
/// quiera. Que ese ascenso sea manual es deliberado, no una tarea pendiente.</para>
///
/// <para>Lógica pura: cero <c>Umbraco.Cms.*</c> y cero <c>Microsoft.AspNetCore.*</c>
/// (ADR 0002). La única clase que toca Umbraco es la fuente que se le inyecta.</para>
///
/// <para><b>Sin caché, a propósito.</b> Cada búsqueda vuelve a pedirle los ítems a la fuente,
/// igual que el stub lee su diccionario fresco. De ahí sale gratis el read-your-writes que
/// este vertical necesita: el organizador publica y lo ve en la misma pantalla.</para>
/// </remarks>
public sealed class CatalogEventCatalogProvider : IEventCatalogProvider
{
    /// <summary>
    /// Familia de entidades del store para los eventos publicados por organizadores.
    /// </summary>
    public const string ResourceType = "event-catalog";

    /// <summary>
    /// Prefijo de los ids acuñados aquí cuando el borrador no trae slug.
    /// </summary>
    private const string GeneratedIdPrefix = "evt-org-";

    /// <summary>
    /// Tope de longitud de la clave del store. Un id largo no aporta nada y las claves acaban
    /// siendo nombres de archivo en el adapter FileSystem.
    /// </summary>
    private const int MaxKeyLength = 96;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ICatalogSource<EventDetail> _source;
    private readonly IJsonEntityStore _store;
    private readonly ICatalogIndex<EventSummary> _index;
    private readonly string _resourceType;

    public CatalogEventCatalogProvider(ICatalogSource<EventDetail> source, IJsonEntityStore store)
        : this(source, store, ResourceType, new InMemoryCatalogIndex<EventSummary>(
            StubEventCatalogProvider.Descriptor, CatalogSettings.Unpaged))
    {
    }

    internal CatalogEventCatalogProvider(
        ICatalogSource<EventDetail> source,
        IJsonEntityStore store,
        string resourceType,
        ICatalogIndex<EventSummary> index)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _resourceType = string.IsNullOrWhiteSpace(resourceType)
            ? throw new ArgumentException("El resourceType del store no puede ir vacío.", nameof(resourceType))
            : resourceType;
    }

    public async Task<IReadOnlyList<EventSummary>> SearchAsync(
        string? query,
        CancellationToken cancellationToken = default)
    {
        var catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);

        // El MISMO motor y el MISMO descriptor que el stub. La búsqueda no puede comportarse
        // distinto según de dónde salgan los eventos: sería una regresión invisible que solo
        // aparece al mover el flag a 'cms'.
        var results = _index.Search(
            catalog.Select(e => e.Summary).ToList(),
            new CatalogQuery(Text: query, Take: int.MaxValue));

        return results.Items;
    }

    public async Task<EventDetail?> GetEventAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return null;
        }

        var id = eventId.Trim();

        // Se mira PRIMERO lo publicado por el organizador, por la misma razón por la que gana
        // en el listado: su versión es la más reciente.
        var published = await ReadPublishedAsync(cancellationToken).ConfigureAwait(false);
        var overlayMatch = published.FirstOrDefault(e => IsMatch(e, id));
        if (overlayMatch is not null)
        {
            return overlayMatch;
        }

        var content = await _source.GetAllAsync(null, cancellationToken).ConfigureAwait(false);
        return content.FirstOrDefault(e => IsMatch(e, id));
    }

    public async Task<EventDetail> PublishEventAsync(
        EventDetail draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var id = ResolvePublishedId(draft);
        var published = draft with { Summary = draft.Summary with { Id = id } };

        var json = JsonSerializer.Serialize(published, JsonOptions);
        await _store.WriteAsync(_resourceType, id, json, cancellationToken).ConfigureAwait(false);

        return published;
    }

    /// <summary>
    /// El id definitivo del evento publicado: el que trae el borrador, su slug, o uno acuñado.
    /// </summary>
    /// <remarks>
    /// <b>El slug se prefiere al id acuñado</b> porque es lo que la fuente de contenido usa
    /// como id (<c>UmbracoEventCatalogSource</c> emite <c>Id = slug</c>). Con eso, re-publicar
    /// un evento que también existe como <c>eventPage</c> lo SOBRESCRIBE en vez de duplicarlo
    /// en la agenda con dos fichas casi iguales — que es lo que pasaría si aquí se acuñara un
    /// id nuevo cada vez.
    ///
    /// <para>El id acuñado lleva un sufijo aleatorio y no un contador: el contador vivía en el
    /// proceso y este catálogo es durable, así que tras un reinicio volvería a 1 y el segundo
    /// evento publicado pisaría al primero.</para>
    /// </remarks>
    private static string ResolvePublishedId(EventDetail draft)
    {
        var fromId = Sanitize(draft.Summary.Id);
        if (fromId.Length > 0)
        {
            return fromId;
        }

        var fromSlug = Sanitize(draft.Summary.Slug);
        if (fromSlug.Length > 0)
        {
            return fromSlug;
        }

        return GeneratedIdPrefix + Guid.NewGuid().ToString("n")[..12];
    }

    /// <summary>
    /// Un id apto para ser clave del store: minúsculas, dígitos y guiones, nada más.
    /// </summary>
    /// <remarks>
    /// La clave termina siendo un nombre de archivo en el adapter FileSystem, así que un id con
    /// <c>../</c> o con separadores de ruta no es un id feo: es una escritura fuera del
    /// directorio del recurso. Se filtra por lista blanca (no por lista negra) porque una lista
    /// negra siempre se queda corta.
    /// </remarks>
    private static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(raw.Length, MaxKeyLength));
        foreach (var c in raw.Trim().ToLowerInvariant())
        {
            if (builder.Length == MaxKeyLength)
            {
                break;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if ((c == '-' || c == '_' || c == ' ') && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    private static bool IsMatch(EventDetail detail, string id)
        => string.Equals(detail.Summary.Id, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(detail.Summary.Slug, id, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Las dos capas fusionadas, con lo publicado por el organizador ganando por id o por slug.
    /// </summary>
    private async Task<IReadOnlyList<EventDetail>> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var published = await ReadPublishedAsync(cancellationToken).ConfigureAwait(false);
        var content = await _source.GetAllAsync(null, cancellationToken).ConfigureAwait(false);

        if (published.Count == 0)
        {
            return content;
        }

        // Se descarta por id Y por slug: la fuente de contenido emite los dos iguales, pero un
        // borrador de organizador puede traer un id acuñado y el slug de un eventPage. Mirar
        // solo el id dejaría las dos fichas en la agenda.
        var shadowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in published)
        {
            shadowed.Add(e.Summary.Id);
            if (!string.IsNullOrWhiteSpace(e.Summary.Slug))
            {
                shadowed.Add(e.Summary.Slug);
            }
        }

        var merged = new List<EventDetail>(published);
        merged.AddRange(content.Where(e =>
            !shadowed.Contains(e.Summary.Id)
            && (string.IsNullOrWhiteSpace(e.Summary.Slug) || !shadowed.Contains(e.Summary.Slug))));

        return merged;
    }

    /// <summary>
    /// Los eventos publicados por organizadores. Una entrada ilegible se SALTA.
    /// </summary>
    /// <remarks>
    /// Un JSON corrupto en el store —un despliegue a medias, una edición a mano— no puede
    /// tumbar la agenda entera: se pierde ese evento, no el catálogo. Es la misma postura que
    /// toma la fuente de contenido al omitir un <c>eventPage</c> incompleto.
    /// </remarks>
    private async Task<IReadOnlyList<EventDetail>> ReadPublishedAsync(CancellationToken cancellationToken)
    {
        // OJO: ListAsync devuelve los DOCUMENTOS, no las claves — es la forma de esta seam
        // (el motor de cada dominio deserializa y filtra encima). Pedirle luego un ReadAsync
        // por cada resultado pasaría un JSON entero como clave y devolvería null siempre.
        var documents = await _store.ListAsync(_resourceType, cancellationToken).ConfigureAwait(false);
        if (documents.Count == 0)
        {
            return Array.Empty<EventDetail>();
        }

        var events = new List<EventDetail>(documents.Count);
        foreach (var json in documents)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            EventDetail? detail;
            try
            {
                detail = JsonSerializer.Deserialize<EventDetail>(json, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (detail?.Summary is not null && !string.IsNullOrWhiteSpace(detail.Summary.Id))
            {
                events.Add(detail);
            }
        }

        return events;
    }
}
