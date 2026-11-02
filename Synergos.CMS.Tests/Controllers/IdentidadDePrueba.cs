using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La cara pública de verdad (<see cref="IdentidadPublicaDeMiembros"/>) sobre un almacén en memoria y unos miembros de
/// mentira: lo que la red muestra de una persona se prueba con la regla que corre en producción, no con un doble.
/// </summary>
internal static class IdentidadDePrueba
{
    /// <summary>Con estos miembros (correo y nombre de su ficha); cualquier otro correo no tiene ficha.</summary>
    public static IdentidadPublicaDeMiembros Con(params (string Correo, string? Nombre)[] miembros)
        => Con(new InMemoryJsonEntityStore(), miembros);

    /// <summary>Lo mismo, sobre un almacén que la prueba también mira.</summary>
    public static IdentidadPublicaDeMiembros Con(InMemoryJsonEntityStore store, params (string Correo, string? Nombre)[] miembros)
    {
        var servicio = Substitute.For<IMemberService>();
        foreach (var (correo, nombre) in miembros)
        {
            // Cada sustituto se arma ANTES de su Returns: armarlo dentro de otro Returns lo rompe.
            var miembro = Substitute.For<IMember>();
            miembro.Name.Returns(nombre);
            servicio.GetByEmail(correo).Returns(miembro);
        }
        return new IdentidadPublicaDeMiembros(servicio, store);
    }
}
