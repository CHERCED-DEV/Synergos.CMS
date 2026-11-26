using System.Globalization;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// La reserva del servidor del inicio de Blogs (D-012-V11): el título del bloque y la primera página del feed «Para
/// ti», con los datos que la app va a pintar, dentro del elemento hasta que hidrata.
/// </summary>
public sealed class RespaldoDeBlogsTests
{
    private static readonly CultureInfo EsCo = CultureInfo.GetCultureInfo("es-CO");

    [Fact]
    public async Task El_inicio_lleva_el_titulo_y_la_primera_pagina_del_feed_con_cada_autor_por_su_cara_publica()
    {
        var stream = Feed(
            new ContentStreamItem(
                "post-1", "article",
                new ContentAuthor("equipo", "equipo-synergoslabs", "Equipo SynergosLabs", null),
                "Por qué un solo núcleo puede ser marca, tienda o blog. #producto #plataforma",
                "/media/a/un-motor.png",
                new DateTime(2026, 6, 22, 15, 0, 0, DateTimeKind.Utc),
                new ContentMetrics(),
                MediaAlt: "Un cubo de piezas",
                CmsUrl: "/blogs/un-motor-mil-productos/",
                CmsTitle: "Un motor, mil productos"),
            new ContentStreamItem(
                "post-2", "post",
                new ContentAuthor("ana@correo.co", "ana@correo.co", "ana@correo.co", null),
                "Hoy probé el editor visual",
                null,
                new DateTime(2026, 6, 21, 9, 0, 0, DateTimeKind.Utc),
                new ContentMetrics()));
        var identidad = Substitute.For<IIdentidadPublica>();
        identidad.De("ana@correo.co").Returns(new IdentidadPublica("0123456789abcdef", "ana-0123", "Ana"));

        var html = await new RespaldoDeBlogs(stream, identidad, NullLogger<RespaldoDeBlogs>.Instance)
            .DelInicioAsync(Props(vista: null), EsCo);

        Assert.NotNull(html);
        Assert.Contains($"{SynHostFallbackBuilder.MarkerAttribute}=\"true\"", html);
        Assert.Contains("<h1 class=\"syn-ssr-blogs__title\">Conecta, publica y crece tu audiencia</h1>", html);
        Assert.Contains("<a class=\"syn-ssr-blogs__link\" href=\"/blogs/un-motor-mil-productos/\">Un motor, mil productos</a>", html);
        Assert.Contains("src=\"/media/a/un-motor.png\" alt=\"Un cubo de piezas\"", html);
        Assert.Contains("#producto #plataforma", html);
        Assert.Contains("datetime=\"2026-06-22T15:00:00Z\">22 de junio</time>", html);
        Assert.Contains(">Ana</span> <span class=\"syn-ssr-blogs__handle\">@ana-0123</span>", html);
        Assert.DoesNotContain("correo.co", html);
        await stream.Received(1).GetFeedAsync(
            Arg.Is<FeedQuery>(q => q.Scope == FeedScope.ForYou && q.Cursor == null && q.AuthorId == null && q.Kind == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Otra_vista_u_otra_api_salen_sin_reserva_y_sin_leer_el_feed()
    {
        var stream = Feed();
        var respaldo = new RespaldoDeBlogs(stream, Substitute.For<IIdentidadPublica>(), NullLogger<RespaldoDeBlogs>.Instance);

        Assert.Null(await respaldo.DelInicioAsync(Props(vista: "explorar"), EsCo));
        Assert.Null(await respaldo.DelInicioAsync(Props(vista: "inicio") with { ApiBase = "https://otra.red/api/blogs" }, EsCo));
        await stream.DidNotReceive().GetFeedAsync(Arg.Any<FeedQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Si_el_feed_no_se_lee_el_elemento_sale_sin_reserva_y_la_pagina_no_se_cae()
    {
        var stream = Substitute.For<IContentStream>();
        stream.GetFeedAsync(Arg.Any<FeedQuery>(), Arg.Any<CancellationToken>()).ThrowsAsync(new IOException("disco"));

        var html = await new RespaldoDeBlogs(stream, Substitute.For<IIdentidadPublica>(), NullLogger<RespaldoDeBlogs>.Instance)
            .DelInicioAsync(Props(vista: "inicio"), EsCo);

        Assert.Null(html);
    }

    [Fact]
    public void Lo_que_escribe_cada_uno_se_escapa_y_una_direccion_ajena_no_se_enlaza_ni_se_pide()
    {
        var html = SynHostFallbackBuilder.Blogs(
            "Red <b>",
            null,
            [
                new PublicacionDelRespaldo("<script>x</script>", "a\"b", DateTime.UtcNow, "T<i>", "C&D", "javascript:alert(1)", "javascript:alert(2)", "alt", ["x"]),
                new PublicacionDelRespaldo("Autor", null, DateTime.UtcNow, null, "Cuerpo", "//otro.sitio/x", "//otro.sitio/i.png", "alt", []),
            ],
            EsCo)!;

        Assert.Contains("Red &lt;b&gt;", html);
        Assert.Contains("&lt;script&gt;x&lt;/script&gt;", html);
        Assert.Contains("@a&quot;b", html);
        Assert.Contains("T&lt;i&gt;", html);
        Assert.Contains("C&amp;D", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("otro.sitio", html);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void La_api_local_es_la_ruta_de_BlogsController()
    {
        var ruta = typeof(BlogsController).GetCustomAttribute<RouteAttribute>()?.Template;

        Assert.Equal(RespaldoDeBlogs.ApiLocal, "/" + ruta);
    }

    private static IContentStream Feed(params ContentStreamItem[] items)
    {
        var stream = Substitute.For<IContentStream>();
        stream.GetFeedAsync(Arg.Any<FeedQuery>(), Arg.Any<CancellationToken>()).Returns(new ContentStreamPage(items, null));
        return stream;
    }

    private static BlogsProps Props(string? vista) => new(
        Heading: "Conecta, publica y crece tu audiencia",
        Subheading: "Una red social editorial",
        ApiBase: "/api/blogs",
        LecturasBase: "/api/lecturas/blogs",
        ModeracionBase: "/api/moderacion",
        Vista: vista,
        Filtros: [],
        Rutas: []);
}
