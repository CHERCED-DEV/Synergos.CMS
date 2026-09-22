using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El feed social servido desde el CONTENIDO del CMS: siembra en el stream los
/// <c>postPage</c> que el editor autoró y delega todo lo demás.
/// </summary>
/// <remarks>
/// <para><b>Es un DECORADOR y no un reemplazo, y ésa es la decisión del ticket</b> (#146). Se
/// miraron tres salidas: servir el feed directo del árbol de contenido, sembrarlo, o partir el
/// <c>Kind</c>. Sembrar es la única que deja <see cref="IContentStream.CreateAsync"/> intacto —
/// <c>BlogsController</c> publica desde la app, y las otras dos obligan a decidir qué pasa con
/// eso—, y encima reusa una mecánica ya probada: es lo mismo que Educación hace con el cuerpo
/// de sus lecciones (#100).</para>
///
/// <para><b>La huella del resumen es lo que hace que editar se VEA.</b>
/// <see cref="IContentStream"/> sólo sabe CREAR —no hay update—, así que un mapping por slug a
/// secas serviría para siempre el primer texto: el editor corrige el post, guarda, y el feed
/// sigue mostrando la versión vieja sin que nada falle
/// (<c>feedback_seeded_content_needs_fingerprint</c>). Con la huella dentro de la comparación,
/// un resumen distinto siembra un item nuevo.</para>
///
/// <para><b>Y el mapping es DURABLE</b> por la otra mitad de la misma regla: en memoria, cada
/// arranque re-sembraría el feed entero y crecería para siempre sin que nada fallara.</para>
///
/// <para><b>El AUTOR se repone al leer, y sin eso el arreglo sería invisible.</b>
/// <c>StubContentStream</c> resuelve el autor de un item con <c>SocialDemoSeed.AuthorById</c>,
/// que ante un id que no conoce devuelve <c>new ContentAuthor(id, id, id, null, false)</c> — o
/// sea fabrica el handle y el nombre. Sembrar un post autorado sin reponer su autor habría
/// firmado la tarjeta con «autor-camila-rios», que no se lee como un defecto: se lee como un
/// handle.</para>
///
/// <para><b>Lo que NO hace, dicho para no mentir sobre su alcance:</b> no toca
/// <see cref="IContentStream.CreateAsync"/> —lo que se publica desde la app sigue igual, con su
/// <c>Kind=post</c>—, no borra nada (el seam no tiene delete, así que una edición deja el item
/// viejo en el feed, igual que en Educación) y no repone el autor en
/// <c>ISocialProfileProjection</c>: el perfil de un autor autorado sigue saliendo del seed. Eso
/// último es trabajo aparte y su disparador es que alguien abra el perfil de un autor del CMS.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "El sufijo describe lo que la seam ES para el dominio —un feed de contenido—, no una herencia de System.IO.Stream. Es la misma exención que StubContentStream, y por la misma razón (#134).")]
public sealed class CatalogContentStream : IContentStream, IDisposable
{
    /// <summary>Familia del store para el mapping <c>slug → item del feed</c>.</summary>
    internal const string SeededPostResourceType = "social-seeded-posts";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ICatalogSource<AuthoredPost> _source;
    private readonly IJsonEntityStore _store;
    private readonly IContentStream _inner;
    private readonly SemaphoreSlim _seedGate = new(1, 1);

    /// <inheritdoc />
    public void Dispose() => _seedGate.Dispose();

    /// <summary>Cache del mapping durable. Se relee cuando aparece un post sin sembrar.</summary>
    private Dictionary<string, SeededPost>? _seeded;

    /// <summary>Autores por id de item, para reponerlos al leer.</summary>
    private Dictionary<string, AuthoredPostAuthor> _authors = new(StringComparer.Ordinal);

    public CatalogContentStream(
        ICatalogSource<AuthoredPost> source,
        IJsonEntityStore store,
        IContentStream inner)
    {
        _source = source;
        _store = store;
        _inner = inner;
    }

    public async Task<ContentStreamPage> GetFeedAsync(FeedQuery query, CancellationToken cancellationToken = default)
    {
        await SeedAsync(cancellationToken).ConfigureAwait(false);

        var page = await _inner.GetFeedAsync(query, cancellationToken).ConfigureAwait(false);
        return page with { Items = page.Items.Select(ConAutor).ToList() };
    }

    public async Task<ContentStreamItem?> GetItemAsync(string id, CancellationToken cancellationToken = default)
    {
        await SeedAsync(cancellationToken).ConfigureAwait(false);

        var item = await _inner.GetItemAsync(id, cancellationToken).ConfigureAwait(false);
        return item is null ? null : ConAutor(item);
    }

    /// <summary>
    /// Se delega tal cual: lo que alguien publica desde la app NO es contenido autorado.
    /// </summary>
    /// <remarks>
    /// Es el motivo por el que esto es un decorador. Un adaptador que sirviera el feed leyendo el
    /// árbol de contenido tendría que contestar qué hace con esto —¿se pierde? ¿se mezcla?— y esa
    /// pregunta es de producto, no de cableado.
    /// </remarks>
    public Task<ContentStreamItem> CreateAsync(NewContentItem item, CancellationToken cancellationToken = default)
        => _inner.CreateAsync(item, cancellationToken);

    /// <summary>El item con su autor autorado, o tal cual si no lo sembró esta clase.</summary>
    private ContentStreamItem ConAutor(ContentStreamItem item)
        => _authors.TryGetValue(item.Id, out var autor)
            ? item with
            {
                Author = new ContentAuthor(
                    Id: autor.ActorId,
                    Handle: autor.Handle,
                    DisplayName: autor.DisplayName,
                    AvatarUrl: autor.AvatarUrl,
                    // Sin campo en el schema no hay nada que leer, y aquí `false` es la lectura
                    // conservadora correcta —«no consta que nadie lo haya verificado»— y no la
                    // afirmación del addendum #111, que era decir «no» sobre algo que podía ser
                    // «sí». Ver AuthoredPostAuthor.
                    Verified: false),
            }
            : item;

    /// <summary>
    /// Siembra los posts autorados que faltan, o que cambiaron de resumen.
    /// </summary>
    /// <remarks>
    /// <b>Se llama en cada lectura del feed y eso es barato a propósito</b>: la fuente lee la
    /// caché publicada de Umbraco —en memoria— y esto es un diccionario por post. Lo que NO se
    /// hace es sembrar dentro de la fuente, que es el defecto del #100: <c>GetAllAsync</c> se
    /// llama en cada vuelta, así que el feed crecería un item por post y por lectura.
    /// </remarks>
    private async Task SeedAsync(CancellationToken cancellationToken)
    {
        var posts = await _source.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (posts.Count == 0)
        {
            return;
        }

        var seeded = _seeded ?? await LoadSeededAsync(cancellationToken).ConfigureAwait(false);

        var pendientes = posts
            .Where(p => !(seeded.TryGetValue(p.Id, out var hit)
                          && string.Equals(hit.Fingerprint, Fingerprint(p), StringComparison.Ordinal)))
            .ToList();

        // Los autores se reponen SIEMPRE, no sólo al sembrar: el mapping sobrevive al reinicio y
        // la caché de autores no, así que un proceso nuevo serviría el feed con los autores
        // fabricados hasta que alguien editara un post.
        RememberAuthors(posts, seeded);

        if (pendientes.Count == 0)
        {
            return;
        }

        await _seedGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Doble comprobación: entre el miss y el turno, otra lectura pudo sembrarlo. Sin
            // esto, dos cargas simultáneas del feed sobre un post recién publicado crean dos.
            seeded = await LoadSeededAsync(cancellationToken).ConfigureAwait(false);

            foreach (var post in pendientes)
            {
                var fingerprint = Fingerprint(post);
                if (seeded.TryGetValue(post.Id, out var hit)
                    && string.Equals(hit.Fingerprint, fingerprint, StringComparison.Ordinal))
                {
                    continue;
                }

                var item = await _inner.CreateAsync(
                    new NewContentItem(
                        AuthorId: post.Author.ActorId,
                        Body: post.Excerpt,
                        MediaUrl: post.HeroImageUrl,
                        Kind: post.Kind,
                        MediaAlt: post.HeroImageAlt),
                    cancellationToken).ConfigureAwait(false);

                var record = new SeededPost(post.Id, item.Id, fingerprint);
                await _store.WriteAsync(
                    SeededPostResourceType,
                    post.Id,
                    JsonSerializer.Serialize(record, JsonOptions),
                    cancellationToken).ConfigureAwait(false);

                seeded = new Dictionary<string, SeededPost>(seeded, StringComparer.OrdinalIgnoreCase)
                {
                    [post.Id] = record,
                };
            }

            _seeded = seeded;
            RememberAuthors(posts, seeded);
        }
        finally
        {
            _seedGate.Release();
        }
    }

    private void RememberAuthors(IReadOnlyList<AuthoredPost> posts, Dictionary<string, SeededPost> seeded)
    {
        var autores = new Dictionary<string, AuthoredPostAuthor>(_authors, StringComparer.Ordinal);
        foreach (var post in posts)
        {
            if (seeded.TryGetValue(post.Id, out var hit))
            {
                autores[hit.ContentItemId] = post.Author;
            }
        }

        _authors = autores;
    }

    private async Task<Dictionary<string, SeededPost>> LoadSeededAsync(CancellationToken cancellationToken)
    {
        var documents = await _store.ListAsync(SeededPostResourceType, cancellationToken).ConfigureAwait(false);
        var map = new Dictionary<string, SeededPost>(documents.Count, StringComparer.OrdinalIgnoreCase);

        foreach (var json in documents)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            SeededPost? record;
            try
            {
                record = JsonSerializer.Deserialize<SeededPost>(json, JsonOptions);
            }
            catch (JsonException)
            {
                // Un documento ilegible no puede parar el feed: se re-siembra ese post y ya.
                continue;
            }

            if (record is not null && !string.IsNullOrWhiteSpace(record.Slug))
            {
                map[record.Slug] = record;
            }
        }

        _seeded = map;
        return map;
    }

    /// <summary>
    /// Huella de lo que el editor puede cambiar y el feed muestra.
    /// </summary>
    /// <remarks>
    /// Lleva el resumen, la media y su alt — los tres viajan al item, así que cambiar cualquiera
    /// tiene que volver a sembrarlo. El TÍTULO no: el feed no lo pinta, y meterlo re-sembraría el
    /// post por un cambio que nadie vería. SHA-256 recortado: no es un secreto, sólo tiene que
    /// cambiar cuando el contenido cambia (y <c>GetHashCode</c> no sirve, que está aleatorizado
    /// por proceso — <c>feedback_gethashcode_is_not_a_seed</c>).
    /// </remarks>
    private static string Fingerprint(AuthoredPost post)
    {
        var material = string.Join('\u001f', post.Excerpt, post.HeroImageUrl ?? string.Empty, post.HeroImageAlt ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))[..16];
    }

    private sealed record SeededPost(string Slug, string ContentItemId, string Fingerprint);
}
