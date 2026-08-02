using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El ranking de hashtags trending de <c>/api/blogs/explore</c> (auditoría, backlog Bajo #8).
/// </summary>
/// <remarks>
/// <para><b>Por qué no tenía tests.</b> El cálculo hacía
/// <c>GetStateAsync(...).GetAwaiter().GetResult()</c> dentro de un bucle sobre hasta 100 posts:
/// además de bloquear un hilo del pool cien veces por request en el endpoint más público del
/// vertical, mezclaba I/O con la política de ranking. Separar "resolver los pesos" de
/// "ordenar con ellos" quita el bloqueo Y deja la política a la vista.</para>
///
/// <para>Lo que se fija aquí es producto, no mecánica: cuánto pesa un post sin reacciones, cómo
/// se desempata, cuántos tags salen y qué pasa con un post cuyo peso no se pudo resolver.</para>
/// </remarks>
public sealed class BlogsTrendingTests
{
    private static ContentStreamItem Post(string id, string body) => new(
        Id: id,
        Kind: "post",
        Author: new ContentAuthor("act-x", "@x", "X", null),
        Body: body,
        MediaUrl: null,
        CreatedUtc: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
        Metrics: new ContentMetrics());

    [Fact]
    public void Un_post_sin_reacciones_igual_cuenta()
    {
        // El peso base de 1 es lo que hace que un tag nuevo aparezca. Si el peso fuera el
        // conteo pelado, nada sin reacciones entraría nunca al trending — y nada entra al
        // trending sin haber estado antes en el trending.
        var trending = BlogsController.ComputeTrending(
            new[] { Post("p1", "hablando de #arquitectura") },
            new Dictionary<string, int>());

        var tag = Assert.Single(trending);
        Assert.Equal("arquitectura", tag.Tag);
        Assert.Equal(1, tag.Posts);
        Assert.Equal(1, tag.Score);
    }

    [Fact]
    public void Las_reacciones_del_post_pesan_sobre_sus_tags()
    {
        var items = new[] { Post("p1", "#uno"), Post("p2", "#dos") };
        var weights = new Dictionary<string, int> { ["p1"] = 0, ["p2"] = 9 };

        var trending = BlogsController.ComputeTrending(items, weights);

        Assert.Equal("dos", trending[0].Tag);   // 1 + 9
        Assert.Equal(10, trending[0].Score);
        Assert.Equal("uno", trending[1].Tag);   // 1 + 0
        Assert.Equal(1, trending[1].Score);
    }

    [Fact]
    public void Un_post_cuyo_peso_no_se_resolvio_cuenta_como_cero_y_NO_desaparece()
    {
        // Si el seam de reacciones no sabe de un post, aparecer al fondo es mejor que no
        // aparecer: el tag existe en el feed y el lector lo está viendo.
        var trending = BlogsController.ComputeTrending(
            new[] { Post("desconocido", "#ausente") },
            new Dictionary<string, int> { ["otro"] = 100 });

        var tag = Assert.Single(trending);
        Assert.Equal("ausente", tag.Tag);
        Assert.Equal(1, tag.Score);
    }

    [Fact]
    public void Un_tag_repetido_en_el_MISMO_post_cuenta_una_vez()
    {
        // Si no, escribir "#js #js #js" pondría cualquier cosa en trending.
        var trending = BlogsController.ComputeTrending(
            new[] { Post("p1", "#js y más #js y otra vez #JS") },
            new Dictionary<string, int> { ["p1"] = 0 });

        var tag = Assert.Single(trending);
        Assert.Equal(1, tag.Posts);
        Assert.Equal(1, tag.Score);
    }

    [Fact]
    public void El_mismo_tag_en_posts_distintos_suma_posts_y_pesos()
    {
        var items = new[] { Post("p1", "#css"), Post("p2", "#CSS") };
        var weights = new Dictionary<string, int> { ["p1"] = 2, ["p2"] = 5 };

        var tag = Assert.Single(BlogsController.ComputeTrending(items, weights));

        Assert.Equal("css", tag.Tag);
        Assert.Equal(2, tag.Posts);
        Assert.Equal(9, tag.Score);   // (1+2) + (1+5)
    }

    [Fact]
    public void A_igual_peso_desempata_alfabeticamente_y_no_por_orden_de_llegada()
    {
        // Sin desempate estable, dos requests idénticos devolverían listas distintas.
        var items = new[] { Post("p1", "#zeta"), Post("p2", "#alfa") };
        var weights = new Dictionary<string, int> { ["p1"] = 0, ["p2"] = 0 };

        var trending = BlogsController.ComputeTrending(items, weights);

        Assert.Equal(new[] { "alfa", "zeta" }, trending.Select(t => t.Tag));
    }

    [Fact]
    public void El_trending_se_topa_en_diez()
    {
        var items = Enumerable.Range(1, 25).Select(i => Post($"p{i}", $"#tag{i:00}")).ToList();
        var weights = items.ToDictionary(i => i.Id, i => 0);

        Assert.Equal(10, BlogsController.ComputeTrending(items, weights).Count);
    }

    [Fact]
    public void Un_feed_sin_hashtags_devuelve_lista_vacia_y_no_null()
    {
        var trending = BlogsController.ComputeTrending(
            new[] { Post("p1", "un post sin ningun hashtag") },
            new Dictionary<string, int> { ["p1"] = 7 });

        Assert.Empty(trending);
    }

    [Fact]
    public void Un_feed_vacio_no_revienta()
    {
        Assert.Empty(BlogsController.ComputeTrending(
            Array.Empty<ContentStreamItem>(), new Dictionary<string, int>()));
    }
}
