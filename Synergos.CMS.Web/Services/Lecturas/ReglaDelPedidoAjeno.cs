using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Lecturas;

/// <summary>
/// Quién lee un pedido de la tienda por su referencia (la misma regla de <c>ShopCatalogController</c>): la referencia
/// es la credencial de quien compró sin cuenta, pero un pedido con dueño no lo lee otro miembro, salvo el personal.
/// </summary>
internal static class ReglaDelPedidoAjeno
{
    /// <summary>Los roles del personal que declara el despliegue, como los pide <see cref="IMemberAccessGate.HasAnyRole"/>.</summary>
    internal static string Personal(IOptionsMonitor<TiendaSettings> tienda) => tienda.CurrentValue.PersonalCsv();

    /// <summary>Si quien lee es otro miembro, sin ser del personal.</summary>
    internal static bool EsAjeno(ShopOrder pedido, IMemberAccessGate sesion, string personal)
        => pedido.OwnerMemberKey is Guid dueno
            && sesion.IsAuthenticated
            && sesion.CurrentMemberKey != dueno
            && !sesion.HasAnyRole(personal);
}
