using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre la durabilidad de los seams sociales — colecciones de usuario, grafo de
/// follows, reacciones, stream de contenido y mensajería.
/// </summary>
/// <remarks>
/// <para><b>Qué propiedad se protege.</b> Que el estado que un usuario CREA
/// (guardar algo, seguir a alguien, reaccionar, publicar, escribir un mensaje)
/// siga existiendo después de un reinicio del proceso. La durabilidad ya se
/// había decidido (<see cref="IJsonEntityStore"/>, ADR 0105) y aplicado a
/// órdenes, pagos, reservas, expedientes, tiquetes y matrículas; los seams
/// sociales se habían quedado en un diccionario del proceso, así que un
/// <c>dotnet run</c> borraba quién sigue a quién, cada reacción, cada mensaje
/// directo y cada ítem guardado.</para>
///
/// <para><b>Por qué importa más en
/// <see cref="StubUserCollection"/>.</b> Ese seam es genérico y hoy lo comparten
/// TRES verticales a la vez —la wishlist de Tienda, los favoritos de Propiedades
/// y los guardados de Blogs—, así que tanto la pérdida como una fuga entre
/// dueños o entre colecciones cruzan negocios distintos. De ahí los tests
/// explícitos de aislamiento.</para>
///
/// <para><b>El proxy de reinicio</b> es el mismo que usa
/// <see cref="TrackingAndReturnsDurabilityTests"/>: <b>construir un servicio
/// nuevo sobre el MISMO store</b>. Si el estado sobrevive ese cambio, sobrevive
/// un reinicio del proceso.</para>
///
/// <para><b>El documento corrupto</b> se prueba en cada familia porque el modo
/// de fallo es silencioso y asimétrico: un JSON ilegible no puede tumbar el feed
/// de todos: se salta y el resto sigue sirviéndose.</para>
/// </remarks>
public sealed class SocialSeamsDurabilityTests
{
    private const string Garbage = "{ esto no es JSON válido";

    // ── Colecciones de usuario (wishlist / favoritos / guardados) ─────────

    private static StubUserCollection Collections(IJsonEntityStore store, string ns = "user-collections")
        => new(null, store, ns);

    [Fact] // empty
    public async Task Una_coleccion_inexistente_devuelve_listas_vacias_sin_lanzar()
    {
        var sut = Collections(new InMemoryJsonEntityStore());

        Assert.Empty(await sut.GetAsync("nadie@synergos.co", "wishlist"));
        Assert.Empty(await sut.GetCollectionsAsync("nadie@synergos.co"));
    }

    [Fact] // happy + survives-restart
    public async Task Un_item_guardado_SOBREVIVE_a_un_servicio_nuevo_sobre_el_mismo_store()
    {
        var store = new InMemoryJsonEntityStore();

        await Collections(store).AddAsync("camila@synergos.co", "wishlist", "sku-123");

        var after = Collections(store);
        var items = await after.GetAsync("camila@synergos.co", "wishlist");

        Assert.Equal("sku-123", Assert.Single(items).ItemRef);
    }

    [Fact] // survives-restart: el resumen conserva conteo y última actividad
    public async Task El_resumen_de_colecciones_SOBREVIVE_con_su_conteo()
    {
        var store = new InMemoryJsonEntityStore();
        var at = new DateTimeOffset(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

        var before = new StubUserCollection(() => at, store, "user-collections");
        await before.AddAsync("camila@synergos.co", "wishlist", "sku-1");
        await before.AddAsync("camila@synergos.co", "wishlist", "sku-2");

        var summary = Assert.Single(await Collections(store).GetCollectionsAsync("camila@synergos.co"));

        Assert.Equal("wishlist", summary.Collection);
        Assert.Equal(2, summary.Count);
        Assert.Equal(at, summary.UpdatedAt);
    }

    [Fact] // idempotent-after-restart
    public async Task Agregar_el_MISMO_item_tras_el_reinicio_no_lo_duplica()
    {
        var store = new InMemoryJsonEntityStore();

        var first = await Collections(store).AddAsync("camila@synergos.co", "saved", "post-001");
        var second = await Collections(store).AddAsync("camila@synergos.co", "saved", "post-001");

        // Mismo ítem, misma fecha: re-agregar devuelve el existente.
        Assert.Equal(first.AddedAt, second.AddedAt);
        Assert.Single(await Collections(store).GetAsync("camila@synergos.co", "saved"));
    }

    [Fact] // idempotent-after-restart: quitar dos veces no lanza
    public async Task Quitar_dos_veces_tras_el_reinicio_devuelve_false_la_segunda()
    {
        var store = new InMemoryJsonEntityStore();
        await Collections(store).AddAsync("camila@synergos.co", "wishlist", "sku-1");

        Assert.True(await Collections(store).RemoveAsync("camila@synergos.co", "wishlist", "sku-1"));
        Assert.False(await Collections(store).RemoveAsync("camila@synergos.co", "wishlist", "sku-1"));
    }

    [Fact] // aislamiento por dueño — una fuga acá cruza negocios distintos
    public async Task Dos_duenos_distintos_NUNCA_ven_los_items_del_otro()
    {
        var store = new InMemoryJsonEntityStore();
        var sut = Collections(store);

        await sut.AddAsync("camila@synergos.co", "wishlist", "sku-de-camila");
        await sut.AddAsync("andres@synergos.co", "wishlist", "sku-de-andres");

        var after = Collections(store);
        Assert.Equal("sku-de-camila",
            Assert.Single(await after.GetAsync("camila@synergos.co", "wishlist")).ItemRef);
        Assert.Equal("sku-de-andres",
            Assert.Single(await after.GetAsync("andres@synergos.co", "wishlist")).ItemRef);
    }

    [Fact] // aislamiento por colección — el mismo dueño en tres verticales
    public async Task Tres_colecciones_del_MISMO_dueno_no_se_mezclan()
    {
        var store = new InMemoryJsonEntityStore();
        var sut = Collections(store);

        await sut.AddAsync("camila@synergos.co", "wishlist", "sku-123");    // Tienda
        await sut.AddAsync("camila@synergos.co", "favoritos", "listing-9"); // Propiedades
        await sut.AddAsync("camila@synergos.co", "saved", "post-001");      // Blogs

        var after = Collections(store);
        Assert.Equal("sku-123", Assert.Single(await after.GetAsync("camila@synergos.co", "wishlist")).ItemRef);
        Assert.Equal("listing-9", Assert.Single(await after.GetAsync("camila@synergos.co", "favoritos")).ItemRef);
        Assert.Equal("post-001", Assert.Single(await after.GetAsync("camila@synergos.co", "saved")).ItemRef);
        Assert.Equal(3, (await after.GetCollectionsAsync("camila@synergos.co")).Count);
    }

    [Fact] // aislamiento por espacio (namespace)
    public async Task Cada_familia_de_colecciones_tiene_su_ESPACIO_y_no_se_pisan()
    {
        var store = new InMemoryJsonEntityStore();

        await Collections(store, "user-collections").AddAsync("camila@synergos.co", "wishlist", "sku-1");

        // El MISMO dueño y la MISMA colección en otro espacio no existe.
        Assert.Empty(await Collections(store, "otra-familia").GetAsync("camila@synergos.co", "wishlist"));
    }

    [Fact] // corrupt-document
    public async Task Un_documento_de_colecciones_corrupto_se_SALTA_en_vez_de_tumbar_la_lista()
    {
        var store = new InMemoryJsonEntityStore();
        // La clave del documento es el dueño en minúsculas.
        await store.WriteAsync("user-collections", "camila@synergos.co", Garbage);

        var sut = Collections(store);
        Assert.Empty(await sut.GetAsync("camila@synergos.co", "wishlist"));

        // Y la próxima escritura reemplaza el documento ilegible.
        await sut.AddAsync("camila@synergos.co", "wishlist", "sku-1");
        Assert.Single(await Collections(store).GetAsync("camila@synergos.co", "wishlist"));
    }

    // ── Grafo social (follows) ───────────────────────────────────────────

    private static StubSocialGraphService Graph(IJsonEntityStore store, string ns = "social-graph")
        => new(store, ns);

    [Fact] // empty
    public async Task Un_actor_sin_grafo_no_sigue_a_nadie_ni_tiene_seguidores()
    {
        var sut = Graph(new InMemoryJsonEntityStore());

        Assert.Empty(await sut.GetFollowingAsync("act-nadie"));
        Assert.Empty(await sut.GetFollowersAsync("act-nadie"));
        Assert.Equal(new SocialCounts(0, 0), await sut.GetCountsAsync("act-nadie"));
    }

    [Fact] // happy + survives-restart
    public async Task Un_follow_SOBREVIVE_a_un_servicio_nuevo_sobre_el_mismo_store()
    {
        var store = new InMemoryJsonEntityStore();

        await Graph(store).FollowAsync("act-nuevo", "act-elena");

        var after = Graph(store);
        Assert.True(await after.IsFollowingAsync("act-nuevo", "act-elena"));
        Assert.Contains("act-nuevo", await after.GetFollowersAsync("act-elena"));
    }

    [Fact] // survives-restart: el UNfollow de una relación SEMBRADA también persiste
    public async Task Dejar_de_seguir_a_alguien_SEMBRADO_no_se_re_siembra_tras_el_reinicio()
    {
        // La semilla dice que act-elena sigue a act-mateo. Si el unfollow no
        // sobreviviera, el reinicio volvería a "seguirlo" solo.
        var store = new InMemoryJsonEntityStore();

        Assert.True(await Graph(store).IsFollowingAsync("act-elena", "act-mateo"));
        await Graph(store).UnfollowAsync("act-elena", "act-mateo");

        Assert.False(await Graph(store).IsFollowingAsync("act-elena", "act-mateo"));
    }

    [Fact] // idempotent-after-restart
    public async Task Seguir_dos_veces_a_traves_del_reinicio_sigue_siendo_seguir_UNA_vez()
    {
        var store = new InMemoryJsonEntityStore();

        await Graph(store).FollowAsync("act-nuevo", "act-elena");
        var second = await Graph(store).FollowAsync("act-nuevo", "act-elena");

        Assert.True(second.Following);
        Assert.Single(await Graph(store).GetFollowingAsync("act-nuevo"));
    }

    [Fact] // aislamiento por espacio
    public async Task Cada_grafo_tiene_su_ESPACIO_y_no_se_pisan()
    {
        var store = new InMemoryJsonEntityStore();

        await Graph(store, "social-graph").FollowAsync("act-nuevo", "act-elena");

        Assert.False(await Graph(store, "otro-grafo").IsFollowingAsync("act-nuevo", "act-elena"));
    }

    [Fact] // corrupt-document
    public async Task Un_documento_de_grafo_corrupto_se_SALTA_en_vez_de_tumbar_el_feed()
    {
        var store = new InMemoryJsonEntityStore();
        await store.WriteAsync("social-graph", "act-roto", Garbage);
        await Graph(store).FollowAsync("act-sano", "act-elena");

        var sut = Graph(store);
        Assert.Empty(await sut.GetFollowingAsync("act-roto"));
        // El actor sano sigue resolviendo pese al documento ilegible del vecino.
        Assert.Contains("act-sano", await sut.GetFollowersAsync("act-elena"));
    }

    // ── Reacciones ───────────────────────────────────────────────────────

    private static StubReactionService Reactions(IJsonEntityStore store, string ns = "social-reactions")
        => new(store, ns);

    [Fact] // empty
    public async Task Un_objeto_sin_reacciones_devuelve_total_cero()
    {
        var state = await Reactions(new InMemoryJsonEntityStore()).GetStateAsync("obj-sin-reacciones", "act-x");

        Assert.Equal(0, state.Total);
        Assert.Empty(state.CountsByType);
        Assert.Null(state.MyReaction);
    }

    [Fact] // happy + survives-restart
    public async Task Una_reaccion_SOBREVIVE_a_un_servicio_nuevo_sobre_el_mismo_store()
    {
        var store = new InMemoryJsonEntityStore();

        await Reactions(store).ReactAsync("act-x", "obj-1", "like");

        var state = await Reactions(store).GetStateAsync("obj-1", "act-x");
        Assert.Equal(1, state.Total);
        Assert.Equal("like", state.MyReaction);
    }

    [Fact] // survives-restart: retirar una reacción SEMBRADA no se re-siembra
    public async Task Retirar_una_reaccion_SEMBRADA_sobrevive_el_reinicio()
    {
        // La semilla dice que act-mateo le dio "like" a post-001.
        var store = new InMemoryJsonEntityStore();

        Assert.Equal("like", (await Reactions(store).GetStateAsync("post-001", "act-mateo")).MyReaction);
        await Reactions(store).UnreactAsync("act-mateo", "post-001");

        Assert.Null((await Reactions(store).GetStateAsync("post-001", "act-mateo")).MyReaction);
    }

    [Fact] // idempotent-after-restart: el toggle sigue siendo toggle
    public async Task El_toggle_sigue_siendo_idempotente_a_traves_del_reinicio()
    {
        var store = new InMemoryJsonEntityStore();

        await Reactions(store).ReactAsync("act-x", "obj-2", "like");
        var off = await Reactions(store).ReactAsync("act-x", "obj-2", "like");

        Assert.Equal(0, off.Total);
        Assert.Null(off.MyReaction);
        Assert.Equal(0, await Reactions(store).CountForAsync("obj-2"));
    }

    [Fact] // aislamiento por espacio
    public async Task Cada_familia_de_reacciones_tiene_su_ESPACIO_y_no_se_pisan()
    {
        var store = new InMemoryJsonEntityStore();

        await Reactions(store, "social-reactions").ReactAsync("act-x", "obj-3", "like");

        Assert.Equal(0, await Reactions(store, "otras-reacciones").CountForAsync("obj-3"));
    }

    [Fact] // corrupt-document
    public async Task Un_documento_de_reacciones_corrupto_se_SALTA_en_vez_de_reventar()
    {
        var store = new InMemoryJsonEntityStore();
        await store.WriteAsync("social-reactions", "obj-roto", Garbage);
        await Reactions(store).ReactAsync("act-x", "obj-sano", "like");

        var sut = Reactions(store);
        Assert.Equal(0, await sut.CountForAsync("obj-roto"));
        Assert.Equal(1, (await sut.CountsForAllAsync())["obj-sano"]);
    }

    // ── Stream de contenido (posts publicados) ───────────────────────────

    private static StubContentStream Stream(
        IJsonEntityStore store, string ns = "social-stream", string reactionsNs = "social-reactions")
        => new(
            new StubSocialGraphService(store, "social-graph"),
            new StubReactionService(store, reactionsNs),
            () => new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            store,
            ns);

    [Fact] // happy + survives-restart
    public async Task Un_post_publicado_SOBREVIVE_a_un_servicio_nuevo_sobre_el_mismo_store()
    {
        var store = new InMemoryJsonEntityStore();

        var created = await Stream(store).CreateAsync(
            new NewContentItem("act-elena", "Publicado antes del reinicio."));

        var after = Stream(store);
        Assert.NotNull(await after.GetItemAsync(created.Id));
        Assert.Contains(
            (await after.GetFeedAsync(new FeedQuery(PageSize: 50))).Items,
            i => i.Id == created.Id);
    }

    [Fact] // las métricas siguen siendo una proyección de las reacciones persistidas
    public async Task Las_metricas_del_feed_se_proyectan_de_las_reacciones_persistidas()
    {
        var store = new InMemoryJsonEntityStore();
        var created = await Stream(store).CreateAsync(
            new NewContentItem("act-elena", "Con una reacción."));
        await Reactions(store).ReactAsync("act-mateo", created.Id, "like");

        var item = (await Stream(store).GetFeedAsync(new FeedQuery(PageSize: 50)))
            .Items.Single(i => i.Id == created.Id);

        Assert.Equal(1, item.Metrics.Reactions);
    }

    [Fact] // aislamiento por espacio
    public async Task Cada_stream_tiene_su_ESPACIO_y_no_se_pisan()
    {
        var store = new InMemoryJsonEntityStore();

        var created = await Stream(store, "social-stream").CreateAsync(
            new NewContentItem("act-elena", "Solo en el stream de Blogs."));

        Assert.Null(await Stream(store, "otro-stream").GetItemAsync(created.Id));
    }

    [Fact] // corrupt-document
    public async Task Un_post_corrupto_se_SALTA_y_el_feed_sigue_sirviendose()
    {
        var store = new InMemoryJsonEntityStore();
        await store.WriteAsync("social-stream", "post-roto", Garbage);
        var created = await Stream(store).CreateAsync(new NewContentItem("act-elena", "Sano."));

        var page = await Stream(store).GetFeedAsync(new FeedQuery(PageSize: 50));

        Assert.Contains(page.Items, i => i.Id == created.Id);
        Assert.DoesNotContain(page.Items, i => i.Id == "post-roto");
        Assert.Contains(page.Items, i => i.Id == "post-001"); // la semilla sigue ahí
    }

    // ── Mensajería (DMs / hilos 1:1) ─────────────────────────────────────

    private static StubMessagingService Messaging(IJsonEntityStore store, string ns = "social-messages")
        => new(null, store, ns);

    [Fact] // empty
    public async Task Una_bandeja_sin_hilos_devuelve_vacio_y_un_hilo_desconocido_devuelve_null()
    {
        var sut = Messaging(new InMemoryJsonEntityStore());

        Assert.Empty(await sut.GetInboxAsync("act-nadie"));
        Assert.Null(await sut.GetThreadAsync("thr_inexistente"));
    }

    [Fact] // happy + survives-restart
    public async Task Un_mensaje_directo_SOBREVIVE_a_un_servicio_nuevo_sobre_el_mismo_store()
    {
        var store = new InMemoryJsonEntityStore();

        var thread = await Messaging(store).StartThreadAsync("dm", "act-mateo", "act-elena", "¿Leíste el post?");

        var after = Messaging(store);
        var reloaded = await after.GetThreadAsync(thread.ThreadId);
        Assert.NotNull(reloaded);
        Assert.Equal("¿Leíste el post?", reloaded!.Messages.Single().Body);
        Assert.Single(await after.GetInboxAsync("act-elena"));
    }

    [Fact] // idempotent-after-restart: el ThreadId determinista retoma el MISMO hilo
    public async Task Re_iniciar_la_misma_conversacion_tras_el_reinicio_no_crea_un_hilo_paralelo()
    {
        var store = new InMemoryJsonEntityStore();

        var first = await Messaging(store).StartThreadAsync("dm", "act-mateo", "act-elena", "Primero");
        var second = await Messaging(store).StartThreadAsync("dm", "act-elena", "act-mateo", "Segundo");

        Assert.Equal(first.ThreadId, second.ThreadId);
        Assert.Equal(2, second.Messages.Count);
        Assert.Single(await Messaging(store).GetInboxAsync("act-elena"));
    }

    [Fact] // survives-restart: responder continúa el hilo persistido
    public async Task Responder_tras_el_reinicio_continua_el_hilo_persistido()
    {
        var store = new InMemoryJsonEntityStore();
        var thread = await Messaging(store).StartThreadAsync("dm", "act-mateo", "act-elena", "Primero");

        var replied = await Messaging(store).ReplyAsync(thread.ThreadId, "act-elena", "Respondo");

        Assert.Equal(2, replied.Messages.Count);
        Assert.Equal("Respondo", replied.Messages[^1].Body);
    }

    [Fact] // aislamiento por espacio
    public async Task Cada_bandeja_tiene_su_ESPACIO_y_no_se_pisan()
    {
        var store = new InMemoryJsonEntityStore();

        var thread = await Messaging(store, "social-messages")
            .StartThreadAsync("dm", "act-mateo", "act-elena", "Solo en Blogs");

        Assert.Null(await Messaging(store, "otros-mensajes").GetThreadAsync(thread.ThreadId));
        Assert.Empty(await Messaging(store, "otros-mensajes").GetInboxAsync("act-elena"));
    }

    [Fact] // corrupt-document
    public async Task Un_hilo_corrupto_se_SALTA_en_vez_de_tumbar_la_bandeja()
    {
        var store = new InMemoryJsonEntityStore();
        var roto = await Messaging(store).StartThreadAsync("dm", "act-sofia", "act-elena", "Se va a corromper");
        var sano = await Messaging(store).StartThreadAsync("dm", "act-mateo", "act-elena", "Sano");
        await store.WriteAsync("social-messages", roto.ThreadId, Garbage);

        var sut = Messaging(store);
        var inbox = await sut.GetInboxAsync("act-elena");

        Assert.Null(await sut.GetThreadAsync(roto.ThreadId));
        Assert.Equal(sano.ThreadId, Assert.Single(inbox).ThreadId);
    }

    // ── Centro de notificaciones (sin estado propio) ──────────────────────

    [Fact]
    public async Task Las_notificaciones_quedan_durables_POR_COMPOSICION_sin_store_propio()
    {
        // StubNotificationFeed no guarda nada: deriva del grafo y de las
        // reacciones. Al hacer durables esos dos, las notificaciones sobreviven
        // el reinicio sin que exista un tercer estado que persistir.
        var store = new InMemoryJsonEntityStore();
        await Graph(store).FollowAsync("act-nuevo", "act-elena");

        var feed = new StubNotificationFeed(
            Graph(store),
            Reactions(store),
            () => new DateTime(2026, 7, 7, 12, 0, 0, DateTimeKind.Utc));
        var notifications = await feed.GetForAsync("act-elena");

        Assert.Contains(notifications, n => n.Type == "follow" && n.Actor.Id == "act-nuevo");
    }
}
