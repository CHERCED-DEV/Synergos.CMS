using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Composers;

public sealed partial class SeamComposer
{
    /// <summary>
    /// La reserva de un servicio por franja (011-booking-reserva): el CMS sólo abre la puerta hacia
    /// <c>Bff.Reservas</c>; acá va lo del sitio —si reserva en línea— que lee el resolver del elemento.
    /// </summary>
    private static void ComposeReservas(IUmbracoBuilder builder)
        => builder.Services.AddSeccionDeNegocio<ReservaFeatureSettings, NegocioDeReserva>(
            builder.Config.GetSection("Synergos:Features:Reserva"));
}
