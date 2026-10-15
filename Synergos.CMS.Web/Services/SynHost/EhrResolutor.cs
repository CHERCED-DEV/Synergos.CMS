using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynEhr</c> → <see cref="EhrProps"/>: dónde vive la API para el sitio de la petición
/// (ADR 0137) y por dónde agenda la cita. Del bloque no se lee nada.
/// </summary>
/// <remarks>
/// <para>El paciente de demo que ponía este resolver (<c>pat-jorge-medina</c>) ya no viaja (#197): el
/// servidor lo saca de la sesión del miembro.</para>
///
/// <para><b>El camino de la cita lo decide acá el CMS</b> (003-salud-cita, <see cref="CaminoDeCompra.Decidir"/>):
/// el override del sitio si lo hay; si no, la puerta cuando abre <see cref="FlujoDeLaCita"/>; si no, la ruta
/// directa. Es la misma pregunta con que la puerta contesta 503, así que el elemento nunca va a una puerta
/// cerrada ni deja de ir a una abierta.</para>
/// </remarks>
public sealed class EhrResolutor : IResolutorSynHost<EhrProps>
{
    /// <summary>El flujo de la cita: el que coloca <c>elementSynEhr.cshtml</c> alrededor del elemento.</summary>
    public const string FlujoDeLaCita = "salud.cita";

    private readonly INegocioDelSitio<NegocioDeEhr> _negocio;
    private readonly IPuertaDeLosFlujos _puerta;

    public EhrResolutor(INegocioDelSitio<NegocioDeEhr> negocio, IPuertaDeLosFlujos puerta)
    {
        _negocio = negocio;
        _puerta = puerta;
    }

    public ElementoResuelto<EhrProps> Resolver(IPublishedElement elemento)
    {
        var negocio = _negocio.Actual();
        return new(new EhrProps(
            ApiBase: negocio.ApiBase,
            AppointmentPath: CaminoDeCompra.Decidir(negocio.AppointmentPath, _puerta.Abre(FlujoDeLaCita))));
    }
}
