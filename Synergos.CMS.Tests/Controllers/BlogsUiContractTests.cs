using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El contrato de <c>api/blogs</c> visto <b>desde quien lo consume</b>:
/// <c>&lt;synergos-blogs&gt;</c> (repo <c>Synergos.UI</c>,
/// <c>apps/elements/modules/blogs/</c>).
/// </summary>
/// <remarks>
/// <para><b>De dónde sale la afirmación es lo que los hace distintos.</b> Los otros tests de
/// este controller comprueban que el DTO lleve lo que el controller decidió poner —una
/// tautología que es cierta con el defecto abierto— y construyen los <c>record</c> de petición
/// <b>a mano</b>, que es exactamente el paso que el binding se saltaba. Acá se serializa la
/// respuesta con las MISMAS opciones que usa ASP.NET, se comprueban las claves que
/// <c>blogs-api.client.ts</c> lee con sus mismas reglas, y las escrituras se deserializan
/// <b>desde el JSON literal que manda el cliente</b>.</para>
///
/// <para>Las reglas que más duelen son dos, y las dos son silenciosas: un normalizador que no
/// encuentra las claves que busca <b>descarta la fila entera</b> —lista vacía, sin error y sin
/// cartel de datos de ejemplo— y uno que devuelve <c>null</c> manda <b>la vista entera al
/// mock</b> aunque el servidor traiga los datos buenos.</para>
///
/// <para>Las claves salen de <c>blogs.model.ts</c> y de los <c>normalizeX()</c> de
/// <c>blogs-api.client.ts</c>. La UI es la fuente de verdad del contrato (ADR 0083).</para>
/// </remarks>
public sealed class BlogsUiContractTests
{
    private const string Yo = "act-elena";

    /// <summary>El JSON tal como sale por el cable (y como entra).</summary>
    private static readonly JsonSerializerOptions ComoAspNet = new(JsonSerializerDefaults.Web);

    private static async Task<BlogsController> BuildAsync(ICommentReader? comments = null)
    {
        var graph = new StubSocialGraphService();
        var reactions = new StubReactionService();
        var stream = new StubContentStream(graph, reactions);
        var profiles = new StubSocialProfileProjection();
        var messaging = new StubMessagingService();
        var collections = new StubUserCollection();
        var notifications = new StubNotificationFeed(graph, reactions);

        await BlogsDemoSeeder.SeedAsync(messaging, collections);

        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(true);
        gate.CurrentMemberEmail.Returns(Yo);

        return new BlogsController(
            stream, graph, reactions, profiles, comments ?? new SinComentarios(),
            messaging, collections, notifications, gate);
    }

    private static JsonElement Wire(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, ok.Value!.GetType(), ComoAspNet);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>
    /// El cuerpo tal como lo manda el cliente: se DESERIALIZA, no se construye. Construir el
    /// <c>record</c> a mano ejercita todo menos el paso que se saltaba — el binding.
    /// </summary>
    private static T ComoLoManda<T>(string json)
        => JsonSerializer.Deserialize<T>(json, ComoAspNet)!;

    /// <summary>La primera clave con texto, o null. Es el <c>??</c> de la UI.</summary>
    private static string? FirstString(JsonElement value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (value.TryGetProperty(key, out var found)
                && found.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(found.GetString()))
            {
                return found.GetString();
            }
        }
        return null;
    }

    /// <summary><c>normalizeAuthor</c>: un objeto con handle (o actorKey/id) resoluble.</summary>
    private static bool EsAutorValido(JsonElement value)
        => value.ValueKind == JsonValueKind.Object
           && FirstString(value, "handle", "actorKey", "id") is not null;

    // ══════════════════════ DMs — lo que no se podía usar ══════════════════════

    /// <summary>
    /// <c>normalizeThread</c> exige <c>id</c> Y <c>participant</c>, y descarta la fila entera si
    /// falta cualquiera de los dos. El borde emitía <c>threadId</c> y <c>participants</c> (la
    /// lista cruda), así que las descartaba TODAS: la bandeja salía vacía con el servidor lleno,
    /// devolviendo un <c>[]</c> que es una respuesta válida — sin error, sin log, sin cartel.
    /// </summary>
    [Fact]
    public async Task Messages_emite_las_claves_con_las_que_la_app_NO_descarta_el_hilo()
    {
        var hilos = Wire(await (await BuildAsync()).Messages(CancellationToken.None))
            .GetProperty("threads").EnumerateArray().ToList();
        Assert.NotEmpty(hilos);

        // La regla del normalizador, aplicada tal cual.
        var supervivientes = hilos
            .Where(t => FirstString(t, "id") is not null
                        && t.TryGetProperty("participant", out var p) && EsAutorValido(p))
            .ToList();

        Assert.Equal(hilos.Count, supervivientes.Count);

        var uno = supervivientes[0];
        Assert.NotNull(FirstString(uno, "lastMessage"));
        Assert.NotNull(FirstString(uno, "lastAtUtc"));
        // Una conversación 1:1 se lista por con QUIÉN es: quien mira no se lista a sí mismo.
        Assert.NotEqual(Yo, FirstString(uno.GetProperty("participant"), "actorKey", "id"));
    }

    /// <summary>
    /// <c>normalizeMessage</c> exige <c>id</c> Y <c>author</c> (un OBJETO). El borde emitía
    /// <c>from</c>, un id suelto: cada mensaje se descartaba y la conversación abierta salía en
    /// blanco. <c>outgoing</c> es lo que alinea la burbuja propia a la derecha.
    /// </summary>
    [Fact]
    public async Task Thread_emite_mensajes_con_autor_objeto_y_outgoing()
    {
        var c = await BuildAsync();
        var hiloId = Wire(await c.Messages(CancellationToken.None))
            .GetProperty("threads")[0].GetProperty("id").GetString()!;

        var hilo = Wire(await c.Thread(hiloId, CancellationToken.None)).GetProperty("thread");

        Assert.Equal(hiloId, FirstString(hilo, "id"));
        Assert.True(hilo.TryGetProperty("participant", out var participante) && EsAutorValido(participante));

        var mensajes = hilo.GetProperty("messages").EnumerateArray().ToList();
        Assert.NotEmpty(mensajes);
        Assert.All(mensajes, m =>
        {
            Assert.NotNull(FirstString(m, "id"));
            Assert.True(m.TryGetProperty("author", out var autor) && EsAutorValido(autor));
            Assert.NotNull(FirstString(m, "createdAtUtc"));
        });
        // El hilo sembrado es entre elena y otra persona: alguno de los dos lados es propio.
        Assert.Contains(mensajes, m => m.GetProperty("outgoing").GetBoolean());
    }

    /// <summary>
    /// <b>El cuerpo que manda la app.</b> Un DM se contesta desde la conversación abierta, así
    /// que lleva <c>threadId</c> y no destinatario; el record sólo declaraba <c>to</c>, System.Text.Json
    /// descartaba lo que no mapeaba sin decir nada y el endpoint contestaba <b>400 siempre</b>.
    /// El cliente lo tapaba sintetizando la burbuja: el mensaje se veía enviado y no existía.
    /// </summary>
    [Fact]
    public async Task Message_responde_en_el_hilo_abierto_con_el_cuerpo_que_manda_la_app()
    {
        var c = await BuildAsync();
        var hiloId = Wire(await c.Messages(CancellationToken.None))
            .GetProperty("threads")[0].GetProperty("id").GetString()!;

        var cuerpo = ComoLoManda<BlogsController.SendMessageRequest>(
            $$"""{"threadId":"{{hiloId}}","body":"Va el borrador el viernes."}""");

        var res = await c.Message(cuerpo, CancellationToken.None);

        var enviado = Wire(res).GetProperty("message");
        Assert.NotNull(FirstString(enviado, "id"));
        Assert.Equal("Va el borrador el viernes.", FirstString(enviado, "body"));
        Assert.True(enviado.GetProperty("outgoing").GetBoolean());
        Assert.True(enviado.TryGetProperty("author", out var autor) && EsAutorValido(autor));
        Assert.Equal(hiloId, FirstString(enviado, "threadId"));
    }

    /// <summary>
    /// Escribir en la conversación de otros dos es peor que leerla, y se cierra igual: 403, que
    /// es lo que la app distingue de una caída («no es suya» no se arregla reintentando).
    /// </summary>
    [Fact]
    public async Task Message_en_hilo_ajeno_es_403()
    {
        var messaging = new StubMessagingService();
        var ajeno = await messaging.StartThreadAsync("dm", "act-mateo", "act-sofia", "Hola");

        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(true);
        gate.CurrentMemberEmail.Returns(Yo);
        var graph = new StubSocialGraphService();
        var reactions = new StubReactionService();
        var c = new BlogsController(
            new StubContentStream(graph, reactions), graph, reactions,
            new StubSocialProfileProjection(), new SinComentarios(), messaging,
            new StubUserCollection(), new StubNotificationFeed(graph, reactions), gate);

        var res = await c.Message(
            ComoLoManda<BlogsController.SendMessageRequest>(
                $$"""{"threadId":"{{ajeno.ThreadId}}","body":"cotilleo"}"""),
            CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(res).StatusCode);
    }

    // ══════════════════════ Estudio del creador ══════════════════════

    /// <summary>
    /// <c>normalizeStudio</c> devuelve <c>null</c> si no encuentra <c>followers</c> ni
    /// <c>reach</c> en la RAÍZ, y ese null manda la consola ENTERA al mock —con su cartel de
    /// datos de ejemplo— aunque el servidor traiga los números buenos. Estaban anidados bajo
    /// <c>metrics</c>. Es el defecto de la consola del instructor de #102, calcado.
    /// </summary>
    [Fact]
    public async Task Studio_emite_las_cifras_en_la_raiz_o_la_consola_entera_es_mock()
    {
        var estudio = Wire(await (await BuildAsync()).Studio(CancellationToken.None));

        // La guarda literal del normalizador.
        Assert.True(estudio.TryGetProperty("followers", out _) || estudio.TryGetProperty("reach", out _));
        Assert.True(estudio.GetProperty("followers").GetInt32() > 0);
        Assert.True(estudio.GetProperty("reach").GetInt32() >= 0);
        estudio.GetProperty("engagementRate").GetDouble();

        // `normalizeTopPosts` exige postId y descarta la fila sin él.
        var top = estudio.GetProperty("topPosts").EnumerateArray().ToList();
        Assert.NotEmpty(top);
        Assert.All(top, p => Assert.NotNull(FirstString(p, "postId")));
    }

    // ══════════════════════ Perfil ══════════════════════

    /// <summary>
    /// <c>normalizeProfile</c> lee <c>following</c> en la RAÍZ (anidado bajo <c>author</c> se
    /// descartaba: el botón decía «Seguir» sobre alguien a quien ya se seguía, y al pulsarlo lo
    /// dejaba de seguir) y los TRES contadores del header desde el AUTOR — no desde
    /// <c>stats</c>, que el borde sí emitía y no lee nadie, así que todo perfil salía en 0.
    /// </summary>
    [Fact]
    public async Task Profile_emite_following_en_la_raiz_y_los_contadores_del_header()
    {
        var c = await BuildAsync();
        // act-elena sigue a act-mateo en el grafo sembrado.
        var perfil = Wire(await c.Profile("mateo.design", CancellationToken.None));

        Assert.True(perfil.GetProperty("following").GetBoolean());

        var autor = perfil.GetProperty("author");
        Assert.NotNull(FirstString(autor, "actorKey"));
        Assert.NotNull(FirstString(autor, "bio"));
        Assert.True(autor.GetProperty("followersCount").GetInt32() > 0);
        Assert.True(autor.GetProperty("postsCount").GetInt32() > 0);
    }

    // ══════════════════════ Explorar / tendencias ══════════════════════

    /// <summary>
    /// <c>normalizeSearch</c> lee <c>hashtags</c>; el borde emitía la MISMA lista bajo
    /// <c>trending</c>, así que la pestaña de hashtags salía vacía. Y cada tendencia lleva su
    /// <c>postCount</c>: con <c>posts</c> a secas, la faceta mostraba un 0 al lado de cada una.
    /// </summary>
    [Fact]
    public async Task Explore_emite_hashtags_con_su_postCount()
    {
        // Los posts sembrados no llevan #tag, así que un fixture que sólo lea la semilla
        // pasaría con la lista vacía y no exigiría nada (regla 7 del CLAUDE.md de la UI).
        var c = await BuildAsync();
        await c.Create(ComoLoManda<BlogsController.CreatePostRequest>(
            """{"body":"Trazas con grep #observabilidad"}"""), CancellationToken.None);

        var res = Wire(await c.Explore(q: null, tag: null, CancellationToken.None));

        var tags = res.GetProperty("hashtags").EnumerateArray().ToList();
        Assert.NotEmpty(tags);
        Assert.All(tags, t =>
        {
            Assert.NotNull(FirstString(t, "tag"));
            Assert.True(t.GetProperty("postCount").GetInt32() > 0);
        });
    }

    /// <summary>
    /// <c>GET /trending</c> lo pide la app sola desde el día uno para su barra lateral, y no
    /// existía: 404 → tendencias de ejemplo SIEMPRE. Degrada en silencio (es auxiliar), así que
    /// ni el cartel lo delataba. No hay cálculo nuevo — es el ranking que ya daba <c>/explore</c>.
    /// </summary>
    [Fact]
    public async Task Trending_existe_y_emite_la_lista_que_la_app_lee()
    {
        var c = await BuildAsync();
        await c.Create(ComoLoManda<BlogsController.CreatePostRequest>(
            """{"body":"Presupuesto de tamaño por tier #cdn"}"""), CancellationToken.None);

        var res = Wire(await c.Trending(CancellationToken.None));

        var tags = res.GetProperty("hashtags").EnumerateArray().ToList();
        Assert.NotEmpty(tags);
        Assert.All(tags, t => Assert.NotNull(FirstString(t, "tag")));
    }

    // ══════════════════════ Notificaciones ══════════════════════

    /// <summary>
    /// Dos defectos en la misma respuesta. <c>createdAtUtc</c> faltaba, así que el normalizador
    /// caía a <c>new Date()</c> y TODA notificación decía «ahora». Y el verbo: el seam emite
    /// <c>reaction</c>/<c>comment</c>, que la app no reconoce y <b>no falla: cae en
    /// <c>mention</c></b> — con su icono, su texto y, peor, dentro de la pestaña «Menciones»,
    /// que dejaba de significar nada. Es un VOCAB, como el <c>Rejected → "resuelto"</c> de #104.
    /// </summary>
    [Fact]
    public async Task Notifications_emite_createdAtUtc_y_el_verbo_que_la_app_conoce()
    {
        var res = Wire(await (await BuildAsync()).Notifications(CancellationToken.None));

        var lista = res.GetProperty("notifications").EnumerateArray().ToList();
        Assert.NotEmpty(lista);
        Assert.All(lista, n => Assert.NotNull(FirstString(n, "createdAtUtc")));

        // El vocabulario que la app conoce; lo que no está ahí se lee como "mention".
        var conocidos = new[] { "follow", "react", "mention", "reply", "repost" };
        Assert.All(lista, n => Assert.Contains(FirstString(n, "verb"), conocidos));
        // Y una reacción tiene que llegar como reacción, no disfrazada de mención.
        Assert.Contains(lista, n => FirstString(n, "verb") == "react");
    }

    // ══════════════════════ Publicar ══════════════════════

    /// <summary>
    /// <b>El cuerpo que manda el compositor.</b> Pide el texto alternativo de la imagen y lo
    /// manda en <c>mediaAlt</c>; el record no lo declaraba, así que se descartaba en silencio y
    /// el alt de cada imagen acababa siendo un recorte del cuerpo del post.
    /// </summary>
    [Fact]
    public async Task Post_conserva_el_alt_que_escribio_quien_publica()
    {
        var cuerpo = ComoLoManda<BlogsController.CreatePostRequest>(
            """{"body":"Taller de observabilidad #infra","mediaUrl":"/media/taller.png","mediaAlt":"Pizarra con el diagrama de trazas"}""");

        var post = Wire(await (await BuildAsync()).Create(cuerpo, CancellationToken.None)).GetProperty("post");

        var media = Assert.Single(post.GetProperty("media").EnumerateArray());
        Assert.Equal("Pizarra con el diagrama de trazas", FirstString(media, "alt"));
        // Y las dos derivaciones del cuerpo, que el contrato declara en pareja.
        Assert.Contains("infra", post.GetProperty("hashtags").EnumerateArray().Select(t => t.GetString()));
        Assert.True(post.TryGetProperty("mentions", out _));
    }

    /// <summary>
    /// <b>El cuerpo que manda el editor largo.</b> Pide portada en su propio campo y la manda en
    /// <c>coverUrl</c>/<c>coverAlt</c>; el record no las declaraba y el mapper pasaba
    /// <c>MediaUrl: null</c> fijo, así que el artículo se publicaba SIN portada y sin que nada
    /// fallara — mientras el modo demo sí la pintaba, porque el post sintetizado en local sí la usa.
    /// </summary>
    [Fact]
    public async Task Article_publica_con_la_portada_que_eligio_quien_escribe()
    {
        var cuerpo = ComoLoManda<BlogsController.CreateArticleRequest>(
            """{"title":"Trazas sin colector","body":"Un grep bien puesto llega lejos.","coverUrl":"/media/portada.png","coverAlt":"Terminal con los logs de una compra"}""");

        var post = Wire(await (await BuildAsync()).Article(cuerpo, CancellationToken.None)).GetProperty("post");

        var media = Assert.Single(post.GetProperty("media").EnumerateArray());
        Assert.Equal("/media/portada.png", FirstString(media, "url"));
        Assert.Equal("Terminal con los logs de una compra", FirstString(media, "alt"));
        Assert.Equal("postPage", FirstString(post, "objectKind"));
    }

    // ══════════════════════ Hilo de comentarios ══════════════════════

    /// <summary>
    /// La app pinta el hilo ANIDADO (<c>comment.replies</c>). El borde devolvía la lista plana
    /// del nodo, así que cada respuesta salía como comentario de primer nivel: el hilo se veía
    /// completo y contaba otra conversación, que es peor que verse vacío.
    /// </summary>
    [Fact]
    public async Task Post_anida_las_respuestas_bajo_su_comentario()
    {
        var padre = Guid.NewGuid();
        var comments = new ComentariosFijos(new[]
        {
            new Comment(padre.ToString("N"), 0, "act-mateo", "Mateo", "Buenísimo el hilo.", DateTime.UtcNow, true),
            new Comment(Guid.NewGuid().ToString("N"), 0, "act-sofia", "Sofía", "Coincido.", DateTime.UtcNow, true, padre),
        });

        var detalle = Wire(await (await BuildAsync(comments)).Post("post-001", CancellationToken.None));

        var raiz = Assert.Single(detalle.GetProperty("comments").EnumerateArray());
        Assert.Equal("Buenísimo el hilo.", FirstString(raiz, "body"));
        var respuesta = Assert.Single(raiz.GetProperty("replies").EnumerateArray());
        Assert.Equal("Coincido.", FirstString(respuesta, "body"));
        // El parentId casa con el id del padre: con el formato con guiones no casaba con nada.
        Assert.Equal(FirstString(raiz, "id"), FirstString(respuesta, "parentId"));
    }

    private sealed class SinComentarios : ICommentReader
    {
        public IReadOnlyList<Comment> GetApprovedForNode(int nodeId) => Array.Empty<Comment>();
    }

    private sealed class ComentariosFijos : ICommentReader
    {
        private readonly IReadOnlyList<Comment> _all;
        public ComentariosFijos(IReadOnlyList<Comment> all) => _all = all;
        public IReadOnlyList<Comment> GetApprovedForNode(int nodeId) => _all;
    }
}
