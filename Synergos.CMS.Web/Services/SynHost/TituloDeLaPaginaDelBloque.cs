using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// El ajuste «Es el título de la página» (<c>compTituloDeLaPagina</c>, D-016-Y4): lo marca el editor en el bloque que es
/// lo principal de la página, y su título pasa a <c>h1</c>. Apagado, no viaja nada y la pieza abre con su <c>h2</c> de
/// siempre: así una pieza que también se monta bajo el hero de otra página no le agrega un segundo <c>h1</c>.
/// </summary>
public static class TituloDeLaPaginaDelBloque
{
    public const string Alias = "esTituloDeLaPagina";

    /// <summary>1 si el editor marcó el bloque como el título de la página; null si no (la pieza usa su nivel 2).</summary>
    public static int? Nivel(IPublishedElement elemento) => elemento.GetProperty(Alias)?.GetValue() is true ? 1 : null;
}
