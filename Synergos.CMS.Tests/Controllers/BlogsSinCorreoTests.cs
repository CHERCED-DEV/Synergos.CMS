using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Nadie sale en la red con su correo, y quien lee es el de la sesión (012-blogs-social, I0, T003 y T004).
/// </summary>
/// <remarks>
/// <para>Lo que medía el plan, leyendo el código: lo que publicaba un miembro salía firmado con su correo, en el nombre,
/// en el alias y en la llave, y el inicio es público. Y las rutas tomaban a «Elena», un actor de la demo, como lectora.</para>
///
/// <para>La prueba recorre la red entera como la usan dos miembros —publicar, escribir un artículo, seguir, reaccionar,
/// escribirse, comentar, guardar— y lee cada respuesta tal como sale por el cable, como cada una de las dos y sin
/// sesión. Ningún correo en ninguna.</para>
/// </remarks>
public sealed class BlogsSinCorreoTests : IDisposable
{
    private const string Ana = "ana.perez@correo.test";
    private const string Beto = "beto.ruiz@correo.test";

    private readonly RedDePrueba _red = new(SemillaDeLaRed.Apagada, [(Ana, "Ana Pérez"), (Beto, "Beto Ruiz")]);

    public void Dispose() => _red.Dispose();

    private static void SinCorreos(string json, string donde)
    {
        Assert.False(json.Contains("ana.perez@", StringComparison.OrdinalIgnoreCase), $"{donde} lleva el correo de Ana: {json}");
        Assert.False(json.Contains("beto.ruiz@", StringComparison.OrdinalIgnoreCase), $"{donde} lleva el correo de Beto: {json}");
        Assert.False(json.Contains("correo.test", StringComparison.OrdinalIgnoreCase), $"{donde} lleva un dominio de correo: {json}");
    }

    [Fact]
    public async Task Ninguna_respuesta_de_la_red_lleva_el_correo_de_nadie()
    {
        var ana = _red.Como(Ana);
        var beto = _red.Como(Beto);
        var nadie = _red.Como(null);
        var ct = CancellationToken.None;
        var respuestas = new List<(string Donde, IActionResult Resultado)>();

        // Lo que hacen las dos.
        var publicar = await ana.Red.Create(new BlogsController.CreatePostRequest("Hola, red #arquitectura", null, null, null), ct);
        respuestas.Add(("publicar", publicar));
        var post = RedDePrueba.Cuerpo<BlogsController.CreatePostResponse>(publicar).Post;
        respuestas.Add(("artículo", await ana.Red.Article(
            new BlogsController.CreateArticleRequest(null, "Un artículo", "Con su cuerpo.", null), ct)));
        respuestas.Add(("seguir", await beto.Red.Follow(post.Author.ActorKey!, null, ct)));
        respuestas.Add(("reaccionar", await beto.Red.React(post.Id, new BlogsController.ReactRequest("love"), null, ct)));
        var escribir = await beto.Red.Message(new BlogsController.SendMessageRequest(null, post.Author.ActorKey, "Hola, Ana"), ct);
        respuestas.Add(("escribir", escribir));
        var hilo = RedDePrueba.Cuerpo<BlogsController.ThreadResponse>(escribir).Thread.ThreadId;
        var comentar = await ana.Hilo.Comment(post.Id, new BlogsCommentsController.CommentRequest("Gracias por leer"), ct);
        respuestas.Add(("comentar", comentar));
        var comentario = RedDePrueba.Cuerpo<BlogsCommentsController.CommentResponse>(comentar).Comment;
        respuestas.Add(("responder", await beto.Hilo.Comment(
            post.Id, new BlogsCommentsController.CommentRequest("A ti", comentario.Id), ct)));
        respuestas.Add(("me gusta", await beto.Hilo.Like(post.Id, comentario.Id, ct)));
        respuestas.Add(("guardar", await ana.Red.Save(new BlogsController.SaveRequest(null, post.Id), ct)));

        // Lo que ve cada una, y quien no tiene sesión.
        foreach (var (quien, c) in new[] { ("Ana", ana.Red), ("Beto", beto.Red), ("sin sesión", nadie.Red) })
        {
            respuestas.Add(($"inicio de {quien}", await c.Feed(null, null, null, ct)));
            respuestas.Add(($"siguiendo de {quien}", await c.Feed("following", null, null, ct)));
            respuestas.Add(($"publicación de {quien}", await c.Post(post.Id, ct)));
            respuestas.Add(($"perfil de Ana visto por {quien}", await c.Profile(post.Author.Handle, ct)));
            respuestas.Add(($"explorar de {quien}", await c.Explore(null, "arquitectura", ct)));
            respuestas.Add(($"tendencias de {quien}", await c.Trending(ct)));
        }
        foreach (var (quien, c) in new[] { ("Ana", ana.Red), ("Beto", beto.Red) })
        {
            respuestas.Add(($"quién soy, {quien}", await c.Me(ct)));
            respuestas.Add(($"bandeja de {quien}", await c.Messages(ct)));
            respuestas.Add(($"conversación de {quien}", await c.Thread(hilo, ct)));
            respuestas.Add(($"notificaciones de {quien}", await c.Notifications(ct)));
            respuestas.Add(($"guardados de {quien}", await c.Saved(ct)));
            respuestas.Add(($"estudio de {quien}", await c.Studio(ct)));
        }

        foreach (var (donde, resultado) in respuestas)
        {
            SinCorreos(RedDePrueba.Cable(resultado), donde);
        }
    }

    [Fact]
    public async Task Lo_que_publica_un_miembro_lo_firma_su_cara_publica()
    {
        var ana = _red.Como(Ana);
        await ana.Red.Create(new BlogsController.CreatePostRequest("Hola, red", null, null, null), CancellationToken.None);
        var cara = _red.Identidad.De(Ana);

        var inicio = RedDePrueba.Cuerpo<BlogsController.FeedResponse>(await _red.Como(null).Red.Feed(null, null, null, CancellationToken.None));
        var autor = Assert.Single(inicio.Posts).Author;

        Assert.Equal("Ana Pérez", autor.DisplayName);
        Assert.Equal(cara.Alias, autor.Handle);
        Assert.Equal(cara.Llave, autor.ActorKey);
        Assert.Equal(cara.Llave, autor.Id);
        Assert.Equal(IdentidadDePrueba.LlaveDe(Ana), autor.ActorKey);
        Assert.NotEqual(SeudonimoDePersona.De(Ana), autor.ActorKey);
    }

    [Fact]
    public async Task Quien_soy_es_la_misma_cara_con_la_que_firmo()
    {
        var ana = _red.Como(Ana);
        var publicado = RedDePrueba.Cuerpo<BlogsController.CreatePostResponse>(
            await ana.Red.Create(new BlogsController.CreatePostRequest("Hola", null, null, null), CancellationToken.None));

        var yo = RedDePrueba.Cuerpo<BlogsController.MeResponse>(await ana.Red.Me(CancellationToken.None)).Author;

        Assert.Equal(publicado.Post.Author, yo);
        Assert.IsType<UnauthorizedObjectResult>(await _red.Como(null).Red.Me(CancellationToken.None));
    }

    [Fact]
    public async Task Se_sigue_y_se_escribe_por_la_llave_publica_y_un_correo_no_nombra_a_nadie()
    {
        var ana = _red.Como(Ana);
        var beto = _red.Como(Beto);
        var post = RedDePrueba.Cuerpo<BlogsController.CreatePostResponse>(
            await ana.Red.Create(new BlogsController.CreatePostRequest("Hola", null, null, null), CancellationToken.None)).Post;
        var ct = CancellationToken.None;

        var seguir = RedDePrueba.Cuerpo<BlogsController.FollowResponse>(await beto.Red.Follow(post.Author.ActorKey!, null, ct));
        Assert.True(seguir.Following);
        Assert.Equal(post.Author.ActorKey, seguir.AuthorId);
        Assert.True(await _red.Grafo.IsFollowingAsync(Beto, Ana, ct));

        Assert.IsType<NotFoundObjectResult>(await beto.Red.Follow(Ana, null, ct));
        Assert.IsType<NotFoundObjectResult>(await beto.Red.Message(new BlogsController.SendMessageRequest(null, Ana, "x"), ct));
        Assert.IsType<BadRequestObjectResult>(await ana.Red.Follow(post.Author.ActorKey!, null, ct));
    }

    [Fact]
    public async Task El_perfil_de_un_miembro_se_abre_por_su_alias_con_sus_publicaciones()
    {
        var ana = _red.Como(Ana);
        await ana.Red.Create(new BlogsController.CreatePostRequest("Primera", null, null, null), CancellationToken.None);
        await _red.Como(Beto).Red.Follow(_red.Identidad.De(Ana).Llave, null, CancellationToken.None);

        var perfil = RedDePrueba.Cuerpo<BlogsController.ProfileResponse>(
            await _red.Como(Beto).Red.Profile(_red.Identidad.De(Ana).Alias, CancellationToken.None));

        Assert.Equal("Ana Pérez", perfil.Author.DisplayName);
        Assert.Single(perfil.Posts);
        Assert.Equal(1, perfil.Author.FollowersCount);
        Assert.True(perfil.Following);
    }

    [Fact]
    public async Task Quien_sigue_sale_en_la_notificacion_con_su_nombre_y_su_llave()
    {
        await _red.Como(Ana).Red.Me(CancellationToken.None);
        await _red.Como(Beto).Red.Follow(_red.Identidad.De(Ana).Llave, null, CancellationToken.None);

        var notificacion = Assert.Single(RedDePrueba.Cuerpo<BlogsController.NotificationsResponse>(
            await _red.Como(Ana).Red.Notifications(CancellationToken.None)).Notifications);

        Assert.Equal("Beto Ruiz empezó a seguirte.", notificacion.Text);
        Assert.Equal(_red.Identidad.De(Beto).Llave, notificacion.Actor.ActorKey);
        Assert.Equal($"ntf-follow-{_red.Identidad.De(Beto).Llave}", notificacion.Id);
    }
}

/// <summary>
/// Quien lee es el de la sesión, nunca «Elena» (012-blogs-social, I0, T003). Con la semilla encendida, que es donde
/// Elena existe: sus reacciones, sus seguidos y su «Siguiendo» se le pintaban a cualquiera.
/// </summary>
public sealed class BlogsLectorDeLaSesionTests : IDisposable
{
    private const string Beto = "beto.ruiz@correo.test";

    private readonly RedDePrueba _red = new(SemillaDeLaRed.Encendida, [(Beto, "Beto Ruiz")]);

    public void Dispose() => _red.Dispose();

    [Fact]
    public async Task Sin_sesion_siguiendo_esta_vacio_y_no_es_el_de_Elena()
    {
        var siguiendo = RedDePrueba.Cuerpo<BlogsController.FeedResponse>(
            await _red.Como(null).Red.Feed("following", null, null, CancellationToken.None));

        Assert.Empty(siguiendo.Posts);
    }

    [Fact]
    public async Task Siguiendo_es_el_de_quien_lee()
    {
        var beto = _red.Como(Beto);
        await beto.Red.Follow("act-mateo", null, CancellationToken.None);

        var siguiendo = RedDePrueba.Cuerpo<BlogsController.FeedResponse>(
            await beto.Red.Feed("following", null, null, CancellationToken.None));

        Assert.NotEmpty(siguiendo.Posts);
        Assert.All(siguiendo.Posts, p => Assert.Equal("act-mateo", p.Author.ActorKey));
    }

    [Fact]
    public async Task La_publicacion_no_le_muestra_a_nadie_la_reaccion_de_Elena()
    {
        // Elena reaccionó «celebrate» a post-002 en la semilla.
        var post = RedDePrueba.Cuerpo<BlogsController.PostDetailResponse>(
            await _red.Como(null).Red.Post("post-002", CancellationToken.None)).Post;

        Assert.Null(post.Reactions.Mine);
        Assert.Null(post.Reactions.MyReaction);
        Assert.True(post.Reactions.Total > 0);
    }

    [Fact]
    public async Task Explorar_no_le_muestra_a_nadie_las_reacciones_de_Elena()
    {
        var explorar = RedDePrueba.Cuerpo<BlogsController.ExploreResponse>(
            await _red.Como(Beto).Red.Explore(null, null, CancellationToken.None));

        Assert.All(explorar.Posts, p => Assert.Null(p.Reactions.Mine));
    }

    [Fact]
    public async Task El_perfil_no_dice_siguiendo_porque_Elena_lo_siga()
    {
        // Elena sigue a Mateo en la semilla.
        var perfil = RedDePrueba.Cuerpo<BlogsController.ProfileResponse>(
            await _red.Como(null).Red.Profile("mateo.design", CancellationToken.None));

        Assert.False(perfil.Following);
        Assert.False(perfil.Author.Following);
    }
}
