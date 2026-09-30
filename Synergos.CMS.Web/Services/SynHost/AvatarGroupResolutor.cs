using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynAvatarGroup</c> → <see cref="AvatarGroupProps"/>.</summary>
/// <remarks>
/// Parsea en el servidor el JSON de integrantes: <c>url</c> (la foto, según el ElementType) sale
/// como <c>src</c> y <c>name</c> como <c>name</c>. El tope de visibles viaja como número.
/// </remarks>
public sealed class AvatarGroupResolutor : IResolutorSynHost<AvatarGroupProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<AvatarGroupResolutor> _log;

    public AvatarGroupResolutor(IPublishedValueFallback fallback, ILogger<AvatarGroupResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<AvatarGroupProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<AvatarGroupProps>(new AvatarGroupProps(
            Avatars: Integrantes(editor),
            MaxVisible: editor.Entero("maxVisible"),
            Label: editor.Texto("ariaLabel")));
    }

    private static IReadOnlyList<AvatarGroupMember>? Integrantes(LectorDelEditor editor)
    {
        var lista = editor.ListaJson("avatarsJson", editor.Texto("avatarsJson"));
        if (lista is null)
        {
            return null;
        }

        var integrantes = new List<AvatarGroupMember>();
        foreach (var entrada in lista)
        {
            var nombre = LectorDelEditor.Cadena(entrada, "name");
            var foto = LectorDelEditor.Cadena(entrada, "url");
            if (nombre is null && foto is null)
            {
                editor.NoEsValido("avatarsJson", entrada.GetRawText(), "un integrante con name o url");
                continue;
            }

            integrantes.Add(new AvatarGroupMember(nombre, foto));
        }

        return integrantes.Count > 0 ? integrantes : null;
    }
}
