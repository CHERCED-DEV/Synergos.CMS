using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynShareBar</c> → <see cref="ShareBarProps"/>.</summary>
/// <remarks>
/// Las redes viajan como lista, con el nombre que el elemento les da; el destino, como la URL del
/// enlace del editor.
/// </remarks>
public sealed class ShareBarResolutor : IResolutorSynHost<ShareBarProps>
{
    /// <summary>
    /// Las redes que el DataType (<c>DTSelectSharePlatform</c>) y el elemento llaman distinto.
    /// Sólo los renombres: el vocabulario de redes es del elemento, que descarta las que no pinta.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NombreEnElElemento =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["twitter"] = "x",
        };

    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<ShareBarResolutor> _log;

    public ShareBarResolutor(IPublishedValueFallback fallback, ILogger<ShareBarResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<ShareBarProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);

        return new ElementoResuelto<ShareBarProps>(new ShareBarProps(
            Platforms: editor.Opciones("platforms")?
                .Select(red => NombreEnElElemento.TryGetValue(red, out var nombre) ? nombre : red.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            ShareLink: editor.Enlace("shareLink")?.Url,
            ShareTitle: editor.Texto("shareTitle")));
    }
}
