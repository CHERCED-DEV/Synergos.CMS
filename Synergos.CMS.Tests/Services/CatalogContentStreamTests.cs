using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="CatalogContentStream"/> — el feed social servido desde el CONTENIDO del CMS
/// (#146): los 4 casos canónicos (ADR 0075) más lo que esta clase añade y ninguna de las otras
/// siete fuentes necesita — que lo autorado y lo publicado DESDE la app convivan.
/// </summary>
public class CatalogContentStreamTests
{
    private sealed class FakeSource : ICatalogSource<AuthoredPost>
    {
        public List<AuthoredPost> Posts { get; } = new();

        public int Calls { get; private set; }

        public Task<IReadOnlyList<AuthoredPost>> GetAllAsync(
            string? scope = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<AuthoredPost>>(Posts.ToList());
        }
    }

    private static StubContentStream Feed()
        => new(new StubSocialGraphService(), new StubReactionService(),
            () => new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

    /// <summary>
    /// Un post autorado. <b>El nombre del autor es lo que el fixture necesita que la fabricación
    /// NO pueda producir</b>: <c>SocialDemoSeed.AuthorById</c> devuelve el id como handle Y como
    /// nombre ante un id desconocido, así que afirmar «Camila Ríos» es lo único que distingue el
    /// autor autorado del inventado. Con un `DisplayName` igual al id, el defecto pasa en verde.
    /// </summary>
    private static AuthoredPost Post(
        string slug = "arquitectura-que-se-borra",
        string excerpt = "La mejor arquitectura es la que te deja borrar código sin miedo.",
        string title = "Arquitectura que se borra",
        string? heroUrl = null,
        string? heroAlt = null)
        => new(
            Id: slug,
            Title: title,
            Excerpt: excerpt,
            Url: "/blog/" + slug,
            HeroImageUrl: heroUrl,
            HeroImageAlt: heroAlt,
            PublishedUtc: new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc),
            Kind: "article",
            Author: new AuthoredPostAuthor(
                ActorId: "autor-camila-rios",
                Handle: "camila-rios",
                DisplayName: "Camila Ríos",
                AvatarUrl: "/media/autores/camila.webp"));

    private static async Task<IReadOnlyList<ContentStreamItem>> FeedDe(IContentStream stream)
        => (await stream.GetFeedAsync(new FeedQuery(Scope: FeedScope.ForYou, PageSize: 50))).Items;

    // ── Los cuatro canónicos ────────────────────────────────────────────────

    [Fact]
    public async Task Sin_posts_autorados_el_feed_es_el_de_siempre()
    {
        var inner = Feed();
        var esperado = (await FeedDe(inner)).Count;

        var stream = new CatalogContentStream(new FakeSource(), new InMemoryJsonEntityStore(), inner);

        Assert.Equal(esperado, (await FeedDe(stream)).Count);
    }

    [Fact]
    public async Task Un_post_autorado_entra_al_feed_CON_SU_AUTOR()
    {
        var source = new FakeSource();
        source.Posts.Add(Post());

        var stream = new CatalogContentStream(source, new InMemoryJsonEntityStore(), Feed());

        var item = Assert.Single(
            await FeedDe(stream),
            i => i.Body.StartsWith("La mejor arquitectura", StringComparison.Ordinal));

        Assert.Equal("article", item.Kind);
        // Lo que la fabricación NO puede producir.
        Assert.Equal("Camila Ríos", item.Author.DisplayName);
        Assert.Equal("camila-rios", item.Author.Handle);
        Assert.Equal("/media/autores/camila.webp", item.Author.AvatarUrl);
    }

    [Fact]
    public async Task Leer_el_feed_dos_veces_NO_siembra_dos_veces()
    {
        var source = new FakeSource();
        source.Posts.Add(Post());

        var stream = new CatalogContentStream(source, new InMemoryJsonEntityStore(), Feed());

        await FeedDe(stream);
        var segunda = await FeedDe(stream);

        Assert.Single(segunda, i => i.Body.StartsWith("La mejor arquitectura", StringComparison.Ordinal));
    }

    [Fact]
    public async Task El_feed_filtra_por_Kind_igual_que_antes()
    {
        var source = new FakeSource();
        source.Posts.Add(Post());

        var stream = new CatalogContentStream(source, new InMemoryJsonEntityStore(), Feed());

        var soloPosts = (await stream.GetFeedAsync(
            new FeedQuery(Scope: FeedScope.ForYou, Kind: "post", PageSize: 50))).Items;

        Assert.DoesNotContain(soloPosts, i => i.Body.StartsWith("La mejor arquitectura", StringComparison.Ordinal));
    }

    // ── Lo que esta clase añade ─────────────────────────────────────────────

    /// <summary>
    /// Editar el resumen tiene que VERSE, y es la razón de que la huella exista.
    /// </summary>
    /// <remarks>
    /// <see cref="IContentStream"/> sólo sabe CREAR: sin huella, un mapping por slug serviría
    /// para siempre el primer texto — el editor corrige, guarda, y el feed sigue mostrando la
    /// versión vieja sin que nada falle (<c>feedback_seeded_content_needs_fingerprint</c>).
    /// </remarks>
    [Fact]
    public async Task Editar_el_resumen_se_VE_en_el_feed()
    {
        var source = new FakeSource();
        source.Posts.Add(Post(excerpt: "El primer resumen."));

        var store = new InMemoryJsonEntityStore();
        var inner = Feed();
        var stream = new CatalogContentStream(source, store, inner);

        await FeedDe(stream);

        source.Posts.Clear();
        source.Posts.Add(Post(excerpt: "El resumen corregido."));

        var despues = await FeedDe(stream);

        Assert.Contains(despues, i => i.Body == "El resumen corregido.");
    }

    /// <summary>
    /// Y cambiar sólo el TÍTULO no re-siembra: el feed no lo pinta.
    /// </summary>
    /// <remarks>
    /// Es la otra mitad de la decisión de la huella. Meter el título dentro haría que corregir
    /// una tilde dejara un item huérfano por cada corrección, sin que nadie viera nada distinto.
    /// </remarks>
    [Fact]
    public async Task Cambiar_solo_el_titulo_NO_siembra_otra_vez()
    {
        var source = new FakeSource();
        source.Posts.Add(Post(title: "Título viejo"));

        var stream = new CatalogContentStream(source, new InMemoryJsonEntityStore(), Feed());
        await FeedDe(stream);

        source.Posts.Clear();
        source.Posts.Add(Post(title: "Título nuevo"));

        var despues = await FeedDe(stream);

        Assert.Single(despues, i => i.Body.StartsWith("La mejor arquitectura", StringComparison.Ordinal));
    }

    /// <summary>
    /// Un proceso nuevo NO re-siembra el feed entero.
    /// </summary>
    /// <remarks>
    /// Con el mapping en memoria, cada arranque volvería a sembrar todo lo autorado y el feed
    /// crecería para siempre sin que nada fallara. Por eso se construye un decorador NUEVO sobre
    /// el MISMO store y el MISMO stream interior — el stream es el almacén durable del feed, y lo
    /// que se simula es que se reinició el proceso, no que se perdieron los datos.
    /// </remarks>
    [Fact]
    public async Task Un_proceso_NUEVO_no_vuelve_a_sembrar()
    {
        var source = new FakeSource();
        source.Posts.Add(Post());

        var store = new InMemoryJsonEntityStore();
        var inner = Feed();

        await FeedDe(new CatalogContentStream(source, store, inner));
        var despues = await FeedDe(new CatalogContentStream(source, store, inner));

        Assert.Single(despues, i => i.Body.StartsWith("La mejor arquitectura", StringComparison.Ordinal));
    }

    /// <summary>
    /// Y ese proceso nuevo sigue sirviendo el autor AUTORADO, no el fabricado.
    /// </summary>
    /// <remarks>
    /// <b>Éste es el que caza el defecto fino.</b> El mapping es durable y la caché de autores
    /// no, así que un decorador que sólo recordara a los autores al SEMBRAR serviría el feed con
    /// «autor-camila-rios» de nombre hasta que alguien editara un post — y eso no se lee como un
    /// defecto: se lee como un handle. Con el test anterior solo, la reposición se puede quitar
    /// y todo sigue en verde.
    /// </remarks>
    [Fact]
    public async Task Tras_reiniciar_el_autor_sigue_siendo_el_AUTORADO()
    {
        var source = new FakeSource();
        source.Posts.Add(Post());

        var store = new InMemoryJsonEntityStore();
        var inner = Feed();

        await FeedDe(new CatalogContentStream(source, store, inner));

        var item = Assert.Single(
            await FeedDe(new CatalogContentStream(source, store, inner)),
            i => i.Body.StartsWith("La mejor arquitectura", StringComparison.Ordinal));

        Assert.Equal("Camila Ríos", item.Author.DisplayName);
    }

    /// <summary>
    /// Lo que se publica DESDE la app sigue funcionando y sigue siendo un <c>post</c>.
    /// </summary>
    /// <remarks>
    /// Es la razón de que esto sea un decorador y no un reemplazo: <c>BlogsController</c> llama a
    /// <see cref="IContentStream.CreateAsync"/>, y un adaptador que sirviera el feed del árbol de
    /// contenido tendría que contestar qué pasa con eso.
    /// </remarks>
    [Fact]
    public async Task Publicar_desde_la_app_sigue_intacto()
    {
        var source = new FakeSource();
        source.Posts.Add(Post());

        var stream = new CatalogContentStream(source, new InMemoryJsonEntityStore(), Feed());

        var creado = await stream.CreateAsync(new NewContentItem("act-elena", "Un apunte corto."));

        Assert.Equal("post", creado.Kind);
        Assert.Contains(await FeedDe(stream), i => i.Id == creado.Id);
    }

    /// <summary>
    /// El detalle de un post autorado también trae su autor.
    /// </summary>
    /// <remarks>
    /// La tarjeta del feed y la pantalla del post son dos lecturas distintas; reponer el autor en
    /// una sola dejaría el nombre bueno en la lista y el inventado al abrirlo.
    /// </remarks>
    [Fact]
    public async Task El_detalle_de_un_post_autorado_trae_su_autor()
    {
        var source = new FakeSource();
        source.Posts.Add(Post());

        var stream = new CatalogContentStream(source, new InMemoryJsonEntityStore(), Feed());

        var delFeed = Assert.Single(
            await FeedDe(stream),
            i => i.Body.StartsWith("La mejor arquitectura", StringComparison.Ordinal));

        var detalle = await stream.GetItemAsync(delFeed.Id);

        Assert.NotNull(detalle);
        Assert.Equal("Camila Ríos", detalle!.Author.DisplayName);
    }

    /// <summary>
    /// El alt de la imagen es del autor: si nadie lo escribió, no se inventa.
    /// </summary>
    [Fact]
    public async Task El_alt_de_la_media_viaja_tal_cual_o_no_viaja()
    {
        var source = new FakeSource();
        source.Posts.Add(Post(slug: "con-alt", heroUrl: "/media/a.webp", heroAlt: "Un diagrama de capas"));
        source.Posts.Add(Post(slug: "sin-alt", excerpt: "Otro resumen.", heroUrl: "/media/b.webp"));

        var stream = new CatalogContentStream(source, new InMemoryJsonEntityStore(), Feed());
        var feed = await FeedDe(stream);

        Assert.Equal(
            "Un diagrama de capas",
            Assert.Single(feed, i => i.MediaUrl == "/media/a.webp").MediaAlt);
        Assert.Null(Assert.Single(feed, i => i.MediaUrl == "/media/b.webp").MediaAlt);
    }
}
