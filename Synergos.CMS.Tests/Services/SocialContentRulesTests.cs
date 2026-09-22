using Synergos.CMS.Web.Services.Catalog;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="SocialContentRules"/> — lo que el editor escribe en un <c>postPage</c> y qué
/// se hace con ello (#146). Lógica pura: no levanta contexto de Umbraco.
/// </summary>
public class SocialContentRulesTests
{
    // ── La fecha ────────────────────────────────────────────────────────────

    [Fact]
    public void Una_fecha_en_formato_ISO_se_lee()
    {
        var r = SocialContentRules.ParsePublishDate("p", "2026-08-20");

        Assert.Equal(new DateTime(2026, 8, 20, 0, 0, 0, DateTimeKind.Utc), r.Value);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Sin_fecha_no_hay_queja_y_el_post_se_siembra_igual()
    {
        var r = SocialContentRules.ParsePublishDate("p", null);

        Assert.Null(r.Value);
        Assert.Empty(r.Issues);
    }

    /// <summary>
    /// Una fecha ambigua NO se adivina.
    /// </summary>
    /// <remarks>
    /// <b>El fixture es el caso que sí parsearía con una cultura cualquiera</b>, y ésa es la
    /// mitad que cuesta: «03/04/2026» da marzo en una máquina y abril en otra, y un mes
    /// equivocado no lo ve nadie. Con un «no es una fecha» evidente —«pronto»— el parser bueno y
    /// el malo darían lo mismo y el defecto pasaría en verde
    /// (<c>feedback_a_failed_tryparse_is_not_a_value</c>).
    /// </remarks>
    [Fact]
    public void Una_fecha_AMBIGUA_se_rechaza_en_vez_de_adivinar_el_mes()
    {
        var r = SocialContentRules.ParsePublishDate("p", "03/04/2026");

        Assert.Null(r.Value);
        Assert.Single(r.Issues, i => i.Level == SocialContentIssueLevel.Warning);
    }

    // ── El autor ────────────────────────────────────────────────────────────

    [Fact]
    public void Un_autor_con_handle_y_nombre_pasa()
        => Assert.Empty(SocialContentRules.CheckAuthor("p", "camila-rios", "Camila Ríos"));

    /// <summary>
    /// Sin nombre NO se siembra, porque el feed lo fabricaría.
    /// </summary>
    /// <remarks>
    /// <c>SocialDemoSeed.AuthorById</c> devuelve el id como handle Y como nombre ante un id que
    /// no conoce. Un post sembrado sin autor legible sale firmado por algo que nadie escribió, y
    /// eso no se lee como un defecto: se lee como un handle.
    /// </remarks>
    [Fact]
    public void Un_autor_SIN_NOMBRE_deja_el_post_sin_sembrar()
    {
        var issues = SocialContentRules.CheckAuthor("p", "camila-rios", "   ");

        Assert.Single(issues, i => i.Level == SocialContentIssueLevel.Error);
    }

    [Fact]
    public void Un_autor_sin_segmento_de_URL_deja_el_post_sin_sembrar()
    {
        var issues = SocialContentRules.CheckAuthor("p", null, "Camila Ríos");

        Assert.Single(issues, i => i.Level == SocialContentIssueLevel.Error);
    }

    // ── El handle ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Camila-Rios", "camila-rios")]
    [InlineData("/camila-rios/", "camila-rios")]
    [InlineData("  camila-rios ", "camila-rios")]
    [InlineData("", "")]
    public void El_handle_sale_del_segmento_de_URL(string segmento, string esperado)
        => Assert.Equal(esperado, SocialContentRules.HandleFromSegment(segmento));

    // ── Los slugs ───────────────────────────────────────────────────────────

    [Fact]
    public void Sin_slugs_repetidos_no_hay_queja()
        => Assert.Empty(SocialContentRules.FindSlugCollisions(["uno", "dos", "tres"]));

    /// <summary>
    /// Dos postPage con el mismo slug se avisan: para el feed son el MISMO post.
    /// </summary>
    /// <remarks>
    /// El slug es la llave del mapping durable, así que el segundo pisaría la huella del primero
    /// y los dos se re-sembrarían en cada vuelta — el feed creciendo sin que nada falle.
    /// </remarks>
    [Fact]
    public void Dos_postPage_con_el_mismo_slug_se_avisan()
    {
        var issues = SocialContentRules.FindSlugCollisions(["uno", "UNO", "dos"]);

        Assert.Single(issues, i => i.Level == SocialContentIssueLevel.Error);
    }
}
