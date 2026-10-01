using System.Text.Json.Nodes;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynBreadcrumb</c> → <see cref="BreadcrumbProps"/>, y su JSON-LD <c>BreadcrumbList</c>
/// cuando el editor lo encendió. Parsea en el servidor el JSON de pasos que el editor escribe en
/// <c>itemsJson</c>.
/// </summary>
/// <remarks>
/// <para>Acepta lo que documenta el ElementType (<c>url</c>) y el nombre que el elemento lee
/// (<c>href</c>), y sale con el del elemento. Un paso sin texto no viaja y se anota.</para>
///
/// <para><b>El JSON-LD lo emite el CMS, no el elemento</b> (Synergos.UI#90). El elemento lo
/// intentaba con un <c>&lt;script&gt;</c> en su plantilla, y el compilador de Angular los quita: con
/// el interruptor encendido no llegaba nunca al DOM. Su sitio es el SSR —un buscador que no ejecuta
/// JavaScript sólo ve eso— y va JUNTO al tag, no en el respaldo de dentro: la hidratación vacía el
/// host. Se arma de los MISMOS pasos que viajan en el <c>config</c>, así que lo que el buscador lee
/// y lo que el visitante ve no pueden divergir.</para>
/// </remarks>
public sealed class BreadcrumbResolutor : IResolutorSynHost<BreadcrumbProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<BreadcrumbResolutor> _log;

    public BreadcrumbResolutor(IPublishedValueFallback fallback, ILogger<BreadcrumbResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<BreadcrumbProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var props = new BreadcrumbProps(Items: Pasos(editor, editor.Texto("itemsJson")));

        return new ElementoResuelto<BreadcrumbProps>(
            props,
            DatosEstructurados: editor.Interruptor("includeStructuredData") ? MigasDeSchemaOrg(props.Items) : null);
    }

    private static IReadOnlyList<BreadcrumbStep>? Pasos(LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson("itemsJson", json);
        if (lista is null)
        {
            return null;
        }

        var pasos = new List<BreadcrumbStep>();
        foreach (var entrada in lista)
        {
            var texto = LectorDelEditor.Cadena(entrada, "label");
            if (texto is null)
            {
                editor.NoEsValido("itemsJson", entrada.GetRawText(), "un paso con label");
                continue;
            }

            pasos.Add(new BreadcrumbStep(
                texto,
                LectorDelEditor.Cadena(entrada, "url") ?? LectorDelEditor.Cadena(entrada, "href")));
        }

        return pasos.Count > 0 ? pasos : null;
    }

    /// <summary>
    /// El <c>BreadcrumbList</c> de schema.org con <paramref name="pasos"/>; <c>null</c> sin pasos.
    /// </summary>
    /// <remarks>
    /// El último paso va sin <c>item</c>: es la página actual —el elemento también le quita el
    /// enlace— y schema.org no lo exige ahí (el buscador toma la URL de la página).
    /// </remarks>
    private static string? MigasDeSchemaOrg(IReadOnlyList<BreadcrumbStep>? pasos)
    {
        if (pasos is null)
        {
            return null;
        }

        var lista = new JsonArray();
        for (var i = 0; i < pasos.Count; i++)
        {
            var paso = new JsonObject
            {
                ["@type"] = "ListItem",
                ["position"] = i + 1,
                ["name"] = pasos[i].Label,
            };
            if (i < pasos.Count - 1 && pasos[i].Href is { } enlace)
            {
                paso["item"] = enlace;
            }

            lista.Add(paso);
        }

        return new JsonObject
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BreadcrumbList",
            ["itemListElement"] = lista,
        }.ToJsonString();
    }
}
