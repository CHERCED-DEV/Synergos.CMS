using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
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
    /// <summary>El secreto de la llave pública en las pruebas, configurado: la llave sale igual en cada corrida.</summary>
    public const string Secreto = "el-secreto-de-las-pruebas-de-012";

    /// <summary>La llave pública que da <see cref="Secreto"/> a este correo.</summary>
    public static string LlaveDe(string correo) => SeudonimoDePersona.Publico(correo, Encoding.UTF8.GetBytes(Secreto));

    /// <summary>Con estos miembros (correo y nombre de su ficha); cualquier otro correo no tiene ficha.</summary>
    public static IdentidadPublicaDeMiembros Con(params (string Correo, string? Nombre)[] miembros)
        => Con(new InMemoryJsonEntityStore(), miembros);

    /// <summary>Lo mismo, sobre un almacén que la prueba también mira.</summary>
    public static IdentidadPublicaDeMiembros Con(InMemoryJsonEntityStore store, params (string Correo, string? Nombre)[] miembros)
        => Con(store, SecretoConfigurado(store, Secreto), miembros);

    /// <summary>Lo mismo, con este secreto de la llave pública.</summary>
    public static IdentidadPublicaDeMiembros Con(
        InMemoryJsonEntityStore store, SecretoDeLaCaraPublica secreto, params (string Correo, string? Nombre)[] miembros)
    {
        var servicio = Substitute.For<IMemberService>();
        foreach (var (correo, nombre) in miembros)
        {
            // Cada sustituto se arma ANTES de su Returns: armarlo dentro de otro Returns lo rompe.
            var miembro = Substitute.For<IMember>();
            miembro.Name.Returns(nombre);
            servicio.GetByEmail(correo).Returns(miembro);
        }
        return new IdentidadPublicaDeMiembros(servicio, store, secreto);
    }

    /// <summary>El secreto puesto en <c>Synergos:IdentidadPublica:Secreto</c>.</summary>
    public static SecretoDeLaCaraPublica SecretoConfigurado(IJsonEntityStore store, string secreto)
        => Custodia(store, new EphemeralDataProtectionProvider(), secreto);

    /// <summary>Sin secreto configurado: la custodia genera uno y lo guarda cifrado con este protector.</summary>
    public static SecretoDeLaCaraPublica SecretoGenerado(IJsonEntityStore store, IDataProtectionProvider protector)
        => Custodia(store, protector, string.Empty);

    private static SecretoDeLaCaraPublica Custodia(IJsonEntityStore store, IDataProtectionProvider protector, string secreto)
        => new(store, protector, Options.Create(new IdentidadPublicaSettings { Secreto = secreto }), NullLogger<SecretoDeLaCaraPublica>.Instance);
}
