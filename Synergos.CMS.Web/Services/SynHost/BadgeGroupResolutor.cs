using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynBadgeGroup</c> → <see cref="BadgeGroupProps"/>. Parsea en el servidor el JSON de
/// insignias que el editor escribe en <c>badgesJson</c>.
/// </summary>
/// <remarks>
/// Acepta lo que documenta el ElementType (<c>color</c>) y el nombre que el elemento lee
/// (<c>tone</c>), y sale con el del elemento, en minúsculas. Una insignia sin texto no viaja y se
/// anota.
/// </remarks>
public sealed class BadgeGroupResolutor : IResolutorSynHost<BadgeGroupProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<BadgeGroupResolutor> _log;

    public BadgeGroupResolutor(IPublishedValueFallback fallback, ILogger<BadgeGroupResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<BadgeGroupProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<BadgeGroupProps>(new BadgeGroupProps(
            Badges: Insignias(editor, editor.Texto("badgesJson")),
            Layout: editor.Texto("layout")?.ToLowerInvariant()));
    }

    private static IReadOnlyList<BadgeGroupItem>? Insignias(LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson("badgesJson", json);
        if (lista is null)
        {
            return null;
        }

        var insignias = new List<BadgeGroupItem>();
        foreach (var entrada in lista)
        {
            var texto = LectorDelEditor.Cadena(entrada, "label");
            if (texto is null)
            {
                editor.NoEsValido("badgesJson", entrada.GetRawText(), "una insignia con label");
                continue;
            }

            insignias.Add(new BadgeGroupItem(
                texto,
                (LectorDelEditor.Cadena(entrada, "color") ?? LectorDelEditor.Cadena(entrada, "tone"))?.ToLowerInvariant()));
        }

        return insignias.Count > 0 ? insignias : null;
    }
}
