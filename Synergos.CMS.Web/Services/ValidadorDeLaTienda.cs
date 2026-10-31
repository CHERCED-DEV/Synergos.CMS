using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// <c>Synergos:Tienda</c> falla al arrancar si declara un rol de personal en blanco (013-seller-operaciones, T002).
/// </summary>
/// <remarks>
/// Un rol en blanco no es «ninguno» ni «todos»: es una línea de configuración a medio escribir, y
/// <c>HasAnyRole</c> lo leería como una coma de más. Se registra con <c>ValidateOnStart</c>, así que el CMS no
/// arranca y el mensaje dice dónde está.
/// </remarks>
public sealed class ValidadorDeLaTienda : IValidateOptions<TiendaSettings>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TiendaSettings options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var enBlanco = options.RolesDelPersonal
            .Select((rol, i) => (rol, i))
            .Where(r => string.IsNullOrWhiteSpace(r.rol))
            .Select(r => $"Synergos:Tienda:RolesDelPersonal:{r.i} está en blanco: un rol de personal es el nombre de un grupo de miembros.")
            .ToList();

        return enBlanco.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(enBlanco);
    }
}
