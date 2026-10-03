using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynEhr</c> → <see cref="EhrProps"/>: dónde vive la API para el sitio de la petición
/// (ADR 0137). Del bloque no se lee nada.
/// </summary>
/// <remarks>
/// El paciente de demo que ponía este resolver (<c>pat-jorge-medina</c>) ya no viaja (#197): el
/// servidor lo saca de la sesión del miembro.
/// </remarks>
public sealed class EhrResolutor : IResolutorSynHost<EhrProps>
{
    private readonly INegocioDelSitio<NegocioDeEhr> _negocio;

    public EhrResolutor(INegocioDelSitio<NegocioDeEhr> negocio)
    {
        _negocio = negocio;
    }

    public ElementoResuelto<EhrProps> Resolver(IPublishedElement elemento)
        => new(new EhrProps(ApiBase: _negocio.Actual().ApiBase));
}
