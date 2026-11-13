using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Los comentarios de la red se guardan (012-blogs-social, I0, T005, P4 A): en el almacén del hilo de la página del
/// post, con su regla de moderación, y un artículo del CMS tiene un solo hilo.
/// </summary>
/// <remarks>
/// Antes la app pintaba el comentario en local y no lo mandaba a ninguna parte: al recargar, no estaba.
/// </remarks>
public sealed class BlogsCommentsControllerTests : IDisposable
{
    private const string Ana = "ana.perez@correo.test";
    private const string Beto = "beto.ruiz@correo.test";

    private readonly List<RedDePrueba> _redes = [];

    public void Dispose()
    {
        foreach (var red in _redes)
        {
            red.Dispose();
        }
    }

    private RedDePrueba Red(bool moderarAntes = false, int comentariosPorHora = 50, IContentStream? stream = null)
    {
        var red = new RedDePrueba(SemillaDeLaRed.Apagada, [(Ana, "Ana Pérez"), (Beto, "Beto Ruiz")], moderarAntes, comentariosPorHora, stream);
        _redes.Add(red);
        return red;
    }

    private static async Task<BlogsController.PostDto> Publicar(RedDePrueba red, string texto = "Hola, red")
        => RedDePrueba.Cuerpo<BlogsController.CreatePostResponse>(
            await red.Como(Ana).Red.Create(new BlogsController.CreatePostRequest(texto, null, null, null), CancellationToken.None)).Post;

    [Fact]
    public async Task Un_comentario_sigue_ahi_al_recargar_con_quien_lo_escribio()
    {
        var red = Red();
        var post = await Publicar(red);

        var respuesta = RedDePrueba.Cuerpo<BlogsCommentsController.CommentResponse>(
            await red.Como(Beto).Hilo.Comment(post.Id, new BlogsCommentsController.CommentRequest("Muy bueno"), CancellationToken.None));
        Assert.True(respuesta.Approved);

        // «Recargar» es pedir la publicación otra vez, con un controlador nuevo y sin nada en memoria de la app.
        var leido = RedDePrueba.Cuerpo<BlogsController.PostDetailResponse>(await red.Como(null).Red.Post(post.Id, CancellationToken.None));
        var comentario = Assert.Single(leido.Comments);
        Assert.Equal("Muy bueno", comentario.Body);
        Assert.Equal("Beto Ruiz", comentario.Author.DisplayName);
        Assert.Equal(red.Identidad.De(Beto).Alias, comentario.Author.Handle);
        Assert.Equal(red.Identidad.De(Beto).Llave, comentario.Author.ActorKey);
        Assert.Equal(1, leido.Post.CommentCount);
    }

    [Fact]
    public async Task Una_respuesta_cuelga_de_su_comentario()
    {
        var red = Red();
        var post = await Publicar(red);
        var primero = RedDePrueba.Cuerpo<BlogsCommentsController.CommentResponse>(
            await red.Como(Beto).Hilo.Comment(post.Id, new BlogsCommentsController.CommentRequest("Pregunta"), CancellationToken.None)).Comment;

        await red.Como(Ana).Hilo.Comment(post.Id, new BlogsCommentsController.CommentRequest("Respuesta", primero.Id), CancellationToken.None);

        var leido = RedDePrueba.Cuerpo<BlogsController.PostDetailResponse>(await red.Como(null).Red.Post(post.Id, CancellationToken.None));
        Assert.Equal("Respuesta", Assert.Single(Assert.Single(leido.Comments).Replies).Body);
        Assert.Equal(2, leido.Post.CommentCount);
    }

    [Fact]
    public async Task Un_articulo_del_CMS_tiene_un_solo_hilo_el_de_su_pagina()
    {
        const int nodoDeLaPagina = 4242;
        var fuente = Substitute.For<ICatalogSource<AuthoredPost>>();
        fuente.GetAllAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            new AuthoredPost("arquitectura", "Arquitectura", "Un resumen.", "/blogs/ingenieria/arquitectura/", null, null, null,
                "article", new AuthoredPostAuthor("autor-equipo", "equipo", "Equipo", null), NodeId: nodoDeLaPagina),
        });
        var grafo = new StubSocialGraphService(null, null, SemillaDeLaRed.Apagada);
        var reacciones = new StubReactionService(null, null, SemillaDeLaRed.Apagada);
        var stream = new CatalogContentStream(fuente, new InMemoryJsonEntityStore(),
            new StubContentStream(grafo, reacciones, null, null, null, SemillaDeLaRed.Apagada));
        var red = Red(stream: stream);
        var articulo = Assert.Single((await stream.GetFeedAsync(new FeedQuery())).Items);
        Assert.Equal(nodoDeLaPagina, articulo.CmsNodeId);

        await red.Como(Beto).Hilo.Comment(articulo.Id, new BlogsCommentsController.CommentRequest("Desde la app"), CancellationToken.None);

        // Es el hilo que pinta la página del post (CommentThread.cshtml lee el del nodo actual).
        Assert.Equal("Desde la app", Assert.Single(red.Comentarios.GetApprovedForNode(nodoDeLaPagina)).Body);
        stream.Dispose();
        grafo.Dispose();
        reacciones.Dispose();
    }

    // 012-blogs-social, I3: la URL de un artículo del CMS es su identidad, y su tarjeta abre esa página.
    [Fact]
    public async Task Un_articulo_del_CMS_lleva_la_url_de_su_pagina_y_lo_de_un_miembro_no()
    {
        var fuente = Substitute.For<ICatalogSource<AuthoredPost>>();
        fuente.GetAllAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            new AuthoredPost("arquitectura", "Arquitectura", "Un resumen.", "/blogs/ingenieria/arquitectura/", null, null, null,
                "article", new AuthoredPostAuthor("autor-equipo", "equipo", "Equipo", null), NodeId: 4242),
        });
        var grafo = new StubSocialGraphService(null, null, SemillaDeLaRed.Apagada);
        var reacciones = new StubReactionService(null, null, SemillaDeLaRed.Apagada);
        var stream = new CatalogContentStream(fuente, new InMemoryJsonEntityStore(),
            new StubContentStream(grafo, reacciones, null, null, null, SemillaDeLaRed.Apagada));
        var red = Red(stream: stream);
        var articulo = Assert.Single((await stream.GetFeedAsync(new FeedQuery())).Items);

        var leido = RedDePrueba.Cuerpo<BlogsController.PostDetailResponse>(await red.Como(null).Red.Post(articulo.Id, CancellationToken.None));
        Assert.Equal("/blogs/ingenieria/arquitectura/", leido.Post.PageUrl);

        var delMiembro = await Publicar(red);
        var suyo = RedDePrueba.Cuerpo<BlogsController.PostDetailResponse>(await red.Como(null).Red.Post(delMiembro.Id, CancellationToken.None));
        Assert.Null(suyo.Post.PageUrl);
        stream.Dispose();
        grafo.Dispose();
        reacciones.Dispose();
    }

    [Fact]
    public async Task Con_moderacion_previa_queda_pendiente_no_se_ve_y_se_avisa()
    {
        var red = Red(moderarAntes: true);
        var post = await Publicar(red);

        var respuesta = RedDePrueba.Cuerpo<BlogsCommentsController.CommentResponse>(
            await red.Como(Beto).Hilo.Comment(post.Id, new BlogsCommentsController.CommentRequest("Espera"), CancellationToken.None));

        Assert.False(respuesta.Approved);
        var leido = RedDePrueba.Cuerpo<BlogsController.PostDetailResponse>(await red.Como(null).Red.Post(post.Id, CancellationToken.None));
        Assert.Empty(leido.Comments);
        await red.Moderacion.Received(1).NotifyPendingAsync(Arg.Is<Comment>(c => c.Body == "Espera"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sin_sesion_no_se_comenta_ni_se_da_me_gusta()
    {
        var red = Red();
        var post = await Publicar(red);

        Assert.IsType<UnauthorizedObjectResult>(
            await red.Como(null).Hilo.Comment(post.Id, new BlogsCommentsController.CommentRequest("x"), CancellationToken.None));
        Assert.IsType<UnauthorizedObjectResult>(await red.Como(null).Hilo.Like(post.Id, "cualquiera", CancellationToken.None));
    }

    [Fact]
    public async Task Vacio_o_sobre_una_publicacion_que_no_existe_no_se_guarda()
    {
        var red = Red();
        var post = await Publicar(red);

        Assert.IsType<BadRequestObjectResult>(
            await red.Como(Beto).Hilo.Comment(post.Id, new BlogsCommentsController.CommentRequest("  "), CancellationToken.None));
        Assert.IsType<BadRequestObjectResult>(await red.Como(Beto).Hilo.Comment(post.Id, null, CancellationToken.None));
        Assert.IsType<NotFoundObjectResult>(
            await red.Como(Beto).Hilo.Comment("post-que-no-existe", new BlogsCommentsController.CommentRequest("x"), CancellationToken.None));
    }

    [Fact]
    public async Task El_limitador_de_la_pagina_vale_tambien_para_la_app()
    {
        var red = Red(comentariosPorHora: 1);
        var post = await Publicar(red);

        await red.Como(Beto).Hilo.Comment(post.Id, new BlogsCommentsController.CommentRequest("uno"), CancellationToken.None);
        var segundo = await red.Como(Beto).Hilo.Comment(post.Id, new BlogsCommentsController.CommentRequest("dos"), CancellationToken.None);

        Assert.Equal(StatusCodes.Status429TooManyRequests, Assert.IsType<ObjectResult>(segundo).StatusCode);
        Assert.Single(red.Comentarios.GetApprovedForNode(HiloDeUnaPublicacion.NodoDerivado(post.Id)));
    }

    [Fact]
    public async Task El_me_gusta_de_un_comentario_se_guarda()
    {
        var red = Red();
        var post = await Publicar(red);
        var comentario = RedDePrueba.Cuerpo<BlogsCommentsController.CommentResponse>(
            await red.Como(Beto).Hilo.Comment(post.Id, new BlogsCommentsController.CommentRequest("Me gusta"), CancellationToken.None)).Comment;

        var megusta = RedDePrueba.Cuerpo<BlogsCommentsController.CommentLikeResponse>(
            await red.Como(Ana).Hilo.Like(post.Id, comentario.Id, CancellationToken.None));

        Assert.Equal(1, megusta.LikeCount);
        var leido = RedDePrueba.Cuerpo<BlogsController.PostDetailResponse>(await red.Como(null).Red.Post(post.Id, CancellationToken.None));
        Assert.Equal(1, Assert.Single(leido.Comments).LikeCount);
        Assert.IsType<NotFoundObjectResult>(await red.Como(Ana).Hilo.Like(post.Id, "no-existe", CancellationToken.None));
    }
}
