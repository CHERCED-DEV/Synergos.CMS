using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services.Lecturas;
using static Synergos.CMS.Tests.Controllers.Lecturas.LecturasDePrueba;

namespace Synergos.CMS.Tests.Controllers.Lecturas;

/// <summary>
/// Blogs en las lecturas genéricas (012-blogs-social, I4), vistas desde los adaptadores de <c>libs/dominio/blogs</c>:
/// Explorar lee el catálogo de la red, Guardados la colección <c>saved</c> y Mensajes la bandeja de los <c>dm</c>.
/// </summary>
/// <remarks>
/// Cada lectura con el adaptador REAL de Blogs sobre servicios falsos. Lo que se afirma además de las claves es la regla
/// de I0: el stream, las colecciones y la mensajería guardan a un miembro por su correo, y por el cable sale su cara
/// pública, nunca el correo.
/// </remarks>
public sealed class LecturasDeBlogsContractTests
{
    private const string Ana = "ana.autora@synergos.co";
    private const string LlaveDeAna = "a1b2c3d4e5f60718";
    private const string LlaveMia = "0f1e2d3c4b5a6978";

    private static readonly DateTime Hoy = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ContentStreamItem Articulo = new(
        Id: "it-articulo",
        Kind: "article",
        Author: new ContentAuthor("autor-camila-rios", "camila-rios", "Camila Ríos", "/media/camila.jpg"),
        Body: "Por qué separamos el dominio de la presentación. #arquitectura",
        MediaUrl: "/media/capas.jpg",
        CreatedUtc: Hoy.AddDays(-2),
        Metrics: new ContentMetrics(Reposts: 3),
        CmsNodeId: 1234,
        CmsUrl: "/blogs/ingenieria/arquitectura-por-capas/",
        CmsTitle: "Arquitectura por capas",
        Category: "Ingeniería",
        // La que le puso el editor y su cuerpo no nombra (I6).
        Tags: ["ingeniería"]);

    private static readonly ContentStreamItem DeAna = new(
        Id: "it-ana",
        Kind: "post",
        Author: new ContentAuthor(Ana, Ana, Ana, null),
        Body: "Leyendo a @camila-rios sobre #Arquitectura y #ux",
        MediaUrl: null,
        CreatedUtc: Hoy.AddHours(-1),
        Metrics: new ContentMetrics());

    private static readonly ContentStreamItem DeLaSemilla = new(
        Id: "it-semilla",
        Kind: "post",
        Author: new ContentAuthor("act-valeria", "valeria", "Valeria Gómez", "/media/valeria.jpg", Verified: true),
        Body: "Buenos días, red.",
        MediaUrl: null,
        CreatedUtc: Hoy.AddDays(-5),
        Metrics: new ContentMetrics());

    // ─── El catálogo (Explorar) ─────────────────────────────────────────────────

    [Fact]
    public async Task El_listado_trae_cada_publicacion_con_lo_que_pinta_la_tarjeta_y_las_tres_facetas()
    {
        var listado = Wire(await Catalogo(Sesion()).Listado("blogs", null, null, null, null, default));

        var elementos = listado.GetProperty("elementos");
        Assert.Equal(new string?[] { "it-ana", "it-articulo", "it-semilla" }, elementos.EnumerateArray().Select(e => e.GetProperty("id").GetString()));
        var articulo = elementos[1];
        Assert.Equal("Arquitectura por capas", articulo.GetProperty("titulo").GetString());
        Assert.Equal("/media/capas.jpg", articulo.GetProperty("imagen").GetString());
        var atributos = articulo.GetProperty("atributos");
        TieneLasClaves(atributos, "tipo", "cuerpo", "autor", "autorId", "alias", "autorImagen", "fecha", "categoria",
            "etiquetas", "pagina", "imagenAlt", "republicaciones", "comentarios", "reacciones");
        Assert.Equal("Ingeniería", atributos.GetProperty("categoria").GetString());
        Assert.Equal("/blogs/ingenieria/arquitectura-por-capas/", atributos.GetProperty("pagina").GetString());
        Assert.Equal("arquitectura,ingeniería", atributos.GetProperty("etiquetas").GetString());
        Assert.Equal("7", atributos.GetProperty("comentarios").GetString());
        Assert.Equal("3", atributos.GetProperty("republicaciones").GetString());

        var facetas = listado.GetProperty("facetas").EnumerateArray().ToDictionary(f => f.GetProperty("campo").GetString()!);
        Assert.Equal(new[] { "categoria", "autor", "etiqueta" }, facetas.Keys);
        Assert.Equal(new string?[] { "Ingeniería" }, Valores(facetas["categoria"]));
        Assert.Equal(new string?[] { "arquitectura", "ingeniería", "ux" }, Valores(facetas["etiqueta"]));
    }

    /// <summary>Un miembro sale por su cara pública: la llave, el alias y el nombre; su correo no sale por ningún lado.</summary>
    [Fact]
    public async Task Un_miembro_sale_por_su_cara_publica_y_su_correo_no_sale()
    {
        var resultado = await Catalogo(Sesion()).Listado("blogs", null, null, null, null, default);
        var listado = Wire(resultado);

        var ana = listado.GetProperty("elementos")[0].GetProperty("atributos");
        Assert.Equal(LlaveDeAna, ana.GetProperty("autorId").GetString());
        Assert.Equal(CaraPublica.Alias("Ana Autora", LlaveDeAna), ana.GetProperty("alias").GetString());
        Assert.Equal("Ana Autora", ana.GetProperty("autor").GetString());
        Assert.Contains(CaraPublica.Alias("Ana Autora", LlaveDeAna), Valores(listado.GetProperty("facetas")[1]));
        Assert.DoesNotContain(Ana, listado.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// La reacción de quien lee viaja con la publicación: sin ella la tarjeta le diría «no reaccionaste» a quien sí, y al
    /// pulsar le quitaría la reacción. Sin sesión no hay reacción propia que decir.
    /// </summary>
    [Fact]
    public async Task Las_reacciones_traen_los_conteos_y_la_de_quien_lee_solo_con_sesion()
    {
        var conSesion = Wire(await Catalogo(Sesion()).Listado("blogs", null, null, null, null, default));
        var sinSesion = Wire(await Catalogo(SinSesion()).Listado("blogs", null, null, null, null, default));

        var articulo = conSesion.GetProperty("elementos")[1].GetProperty("atributos");
        Assert.Equal("like:2,love:1", articulo.GetProperty("reacciones").GetString());
        Assert.Equal("like", articulo.GetProperty("miReaccion").GetString());
        var anonimo = sinSesion.GetProperty("elementos")[1].GetProperty("atributos");
        Assert.Equal("like:2,love:1", anonimo.GetProperty("reacciones").GetString());
        Assert.False(anonimo.TryGetProperty("miReaccion", out _));
    }

    /// <summary>La etiqueta de Explorar es la faceta: casa sin importar mayúsculas, como el filtro de la interina.</summary>
    [Fact]
    public async Task Filtrar_por_etiqueta_trae_las_publicaciones_que_la_llevan()
    {
        var listado = Wire(await Catalogo(Sesion(), "?filtro.etiqueta=arquitectura").Listado("blogs", null, null, null, null, default));

        Assert.Equal(new string?[] { "it-ana", "it-articulo" }, listado.GetProperty("elementos").EnumerateArray().Select(e => e.GetProperty("id").GetString()));
        Assert.Equal(2, listado.GetProperty("total").GetInt32());
    }

    /// <summary>
    /// La página de una etiqueta (I6) encuentra también lo que el editor etiquetó en el backoffice, aunque su cuerpo no la
    /// nombre: un artículo del CMS se etiqueta en <c>compTagging</c>, no con un <c>#</c>.
    /// </summary>
    [Fact]
    public async Task Filtrar_por_una_etiqueta_del_editor_trae_el_articulo_que_la_lleva()
    {
        var listado = Wire(await Catalogo(Sesion(), "?filtro.etiqueta=ingeniería").Listado("blogs", null, null, null, null, default));

        Assert.Equal(new string?[] { "it-articulo" }, listado.GetProperty("elementos").EnumerateArray().Select(e => e.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task La_ficha_trae_el_cuerpo_entero_y_un_id_que_no_existe_es_404()
    {
        var controlador = Catalogo(Sesion());

        var ficha = Wire(await controlador.Ficha("blogs", "it-semilla", default));
        Assert.Equal("Buenos días, red.", ficha.GetProperty("descripcion").GetString());
        Assert.Equal("si", ficha.GetProperty("elemento").GetProperty("atributos").GetProperty("verificado").GetString());
        Assert.Equal(404, Estado(await controlador.Ficha("blogs", "no-existe", default)));
    }

    // ─── La colección (Guardados) ───────────────────────────────────────────────

    /// <summary>Cada guardado sale con la publicación del catálogo, con sus reacciones; lo que ya no está, sin inventarlo.</summary>
    [Fact]
    public async Task Los_guardados_traen_cada_publicacion_del_catalogo_y_lo_que_ya_no_esta_sin_elemento()
    {
        var colecciones = Substitute.For<IUserCollection>();
        colecciones.GetAsync(Yo, "saved", Arg.Any<CancellationToken>()).Returns(new[]
        {
            new UserCollectionItem(Yo, "saved", "it-articulo", Ayer),
            new UserCollectionItem(Yo, "saved", "ya-no-existe", Ayer),
        });
        var lectura = new LecturaDeColeccionesDelVertical(CatalogoDeBlogs.Vertical, colecciones, new[] { Lectura(Sesion()) });
        var controlador = new LecturaDeColeccionesController(new ILecturaDeColecciones[] { lectura }, Sesion());

        var coleccion = Wire(await controlador.Coleccion("blogs", "saved", default));

        var elementos = coleccion.GetProperty("elementos");
        var articulo = elementos[0].GetProperty("elemento");
        Assert.Equal("it-articulo", articulo.GetProperty("id").GetString());
        Assert.Equal("like", articulo.GetProperty("atributos").GetProperty("miReaccion").GetString());
        Assert.Equal("ya-no-existe", elementos[1].GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, elementos[1].GetProperty("elemento").ValueKind);
        Assert.Equal(401, Estado(await new LecturaDeColeccionesController(new ILecturaDeColecciones[] { lectura }, SinSesion())
            .Coleccion("blogs", "saved", default)));
    }

    // ─── La bandeja (Mensajes) ──────────────────────────────────────────────────

    /// <summary>
    /// Sólo los mensajes directos, con el asunto en el nombre público de la contraparte y cada participante por su
    /// alias: el correo con que los guarda la mensajería no sale.
    /// </summary>
    [Fact]
    public async Task La_bandeja_trae_los_mensajes_directos_con_cada_participante_por_su_alias()
    {
        var mensajeria = Substitute.For<IMessagingService>();
        mensajeria.GetInboxAsync(Yo, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new MessageThreadSummary("th-dm", "dm", new[] { Yo, Ana }, "¡Nos vemos!", Ayer, 2),
            new MessageThreadSummary("th-semilla", "dm", new[] { "act-valeria", Yo }, "Hola", Ayer.AddDays(-1), 1),
            new MessageThreadSummary("th-tienda", "tienda:pedido:ord_1", new[] { Yo, "ventas@synergos.co" }, "Sale hoy", Ayer, 1),
        });
        mensajeria.GetThreadAsync("th-dm", Arg.Any<CancellationToken>()).Returns(new MessageThread(
            "th-dm",
            "dm",
            new[] { Yo, Ana },
            new[]
            {
                new ThreadMessage("m1", Ana, "¿Leíste el artículo?", Ayer.AddHours(-1)),
                new ThreadMessage("m2", Yo, "¡Nos vemos!", Ayer),
            },
            Ayer.AddHours(-1),
            Ayer));
        var perfiles = Substitute.For<ISocialProfileProjection>();
        perfiles.GetByActorIdAsync("act-valeria", Arg.Any<CancellationToken>())
            .Returns(new SocialProfile("act-valeria", "valeria", "Valeria Gómez", null, null, null, true, Hoy));
        var controlador = new LecturaDeMensajesController(
            new IContextoDeMensajes[] { new ContextoDeMensajesDeBlogs(Identidad(), perfiles) },
            mensajeria,
            Sesion());

        var resultado = await controlador.Bandeja("blogs", default);
        var bandeja = Wire(resultado);

        var hilos = bandeja.GetProperty("hilos");
        Assert.Equal(new string?[] { "th-dm", "th-semilla" }, hilos.EnumerateArray().Select(h => h.GetProperty("id").GetString()));
        var conAna = hilos[0];
        Assert.Equal("Ana Autora", conAna.GetProperty("asunto").GetString());
        Assert.Equal(CaraPublica.Alias("Ana Autora", LlaveDeAna), conAna.GetProperty("contraparte").GetString());
        var mensajes = conAna.GetProperty("mensajes");
        Assert.Equal(CaraPublica.Alias("Ana Autora", LlaveDeAna), mensajes[0].GetProperty("de").GetString());
        Assert.False(mensajes[0].GetProperty("mio").GetBoolean());
        Assert.True(mensajes[1].GetProperty("mio").GetBoolean());
        Assert.Equal("Valeria Gómez", hilos[1].GetProperty("asunto").GetString());
        Assert.Equal("valeria", hilos[1].GetProperty("contraparte").GetString());
        Assert.DoesNotContain("@synergos.co", bandeja.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    // ─── El montaje ─────────────────────────────────────────────────────────────

    private static LecturaDelCatalogoController Catalogo(IMemberAccessGate sesion, string? query = null)
    {
        var controlador = new LecturaDelCatalogoController(new[] { Lectura(sesion) });
        var http = new DefaultHttpContext();
        if (query is not null)
        {
            http.Request.QueryString = new QueryString(query);
        }

        controlador.ControllerContext = new ControllerContext { HttpContext = http };
        return controlador;
    }

    /// <summary>El catálogo de la red como lo registra el composer, sobre un stream con un artículo, un miembro y la semilla.</summary>
    private static ILecturaDelCatalogo Lectura(IMemberAccessGate sesion)
    {
        var stream = Substitute.For<IContentStream>();
        stream.GetFeedAsync(Arg.Any<FeedQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ContentStreamPage(new[] { DeAna, Articulo, DeLaSemilla }, null));
        var reacciones = Substitute.For<IReactionService>();
        reacciones.GetStateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ReactionState(
                ci.ArgAt<string>(0),
                0,
                new Dictionary<string, int>(),
                null));
        reacciones.GetStateAsync("it-articulo", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => new ReactionState(
                "it-articulo",
                3,
                new Dictionary<string, int> { ["love"] = 1, ["like"] = 2, ["celebrate"] = 0 },
                ci.ArgAt<string?>(1) == Yo ? "like" : null));

        return new LecturaDelCatalogoDeBlogs(
            CatalogoDeBlogs.Lectura(
                new FuenteDeLaRed(stream),
                Identidad(),
                new CatalogSettings(),
                publicacion => publicacion.Id == "it-articulo" ? 7 : 0),
            reacciones,
            sesion);
    }

    private static IIdentidadPublica Identidad()
    {
        var identidad = Substitute.For<IIdentidadPublica>();
        identidad.De(Ana).Returns(CaraPublica.De(LlaveDeAna, "Ana Autora"));
        identidad.De(Yo).Returns(CaraPublica.De(LlaveMia, "Compradora"));
        return identidad;
    }

    private static string?[] Valores(JsonElement faceta)
        => faceta.GetProperty("valores").EnumerateArray().Select(v => v.GetProperty("valor").GetString()).ToArray();
}
