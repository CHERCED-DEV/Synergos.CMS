using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;

namespace Synergos.CMS.Web.Services.Diccionario;

/// <summary>
/// Una frase del diccionario con un dato del visitante dentro (<c>{0}</c>), pintada sin confiar en
/// ninguno de los dos: la frase y el dato se codifican, y sólo el resaltado del dato es marcado.
/// </summary>
/// <remarks>
/// <para><b>Lo que reemplaza</b> (#193): <c>Html.Raw(string.Format(frase, dato))</c>. La frase la
/// edita quien tenga la sección Translation del backoffice, así que con <c>Html.Raw</c> un
/// <c>&lt;script&gt;</c> en la clave corría en la página de restablecer contraseña, que lleva el
/// token en un campo oculto. Y una llave suelta en la frase (<c>{email}</c>) era un
/// <c>FormatException</c>: 500.</para>
///
/// <para>El marcado del resaltado lo pone el código, no la clave: la frase dice dónde va el dato y
/// nada más.</para>
/// </remarks>
public static class FraseConDato
{
    /// <summary>La frase con <c>{0}</c> reemplazado por el dato en negrita; ambos codificados.</summary>
    public static IHtmlContent Resaltado(string frase, string? dato)
    {
        var codificada = HtmlEncoder.Default.Encode(frase);
        var resaltado = $"<strong>{HtmlEncoder.Default.Encode(dato ?? string.Empty)}</strong>";
        return new HtmlString(codificada.Replace("{0}", resaltado, StringComparison.Ordinal));
    }
}
