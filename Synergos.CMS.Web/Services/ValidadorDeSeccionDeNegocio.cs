using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Una sección <c>Synergos:Features:&lt;X&gt;</c> falla al arrancar si trae algo que nadie lee o un
/// valor que no sirve, en los valores base o en cualquier sitio (ADR 0137).
/// </summary>
/// <remarks>
/// <para><b>Falla, no avisa.</b> El precedente de la casa —el validador de la resiliencia de los
/// webhooks— sólo escribe un aviso salvo que se encienda un modo estricto. Acá el valor es de negocio:
/// una clave mal escrita que se queda en su valor por defecto aplica otra regla sin que nadie lo note.
/// Se registra con <c>ValidateOnStart</c>, así que el CMS no arranca.</para>
///
/// <para>Lo que nadie lee lo mira acá <see cref="ClavesDeConfiguracion"/>, porque el binder lo tira
/// antes de que exista el POCO; lo demás —rangos, formas, <c>Key</c> de cada sitio— lo sabe la propia
/// sección (<see cref="ISeccionDeNegocio.Problemas"/>), que es dominio y vive en Application.</para>
/// </remarks>
public sealed class ValidadorDeSeccionDeNegocio<TSeccion> : IValidateOptions<TSeccion>
    where TSeccion : class, ISeccionDeNegocio
{
    private readonly IConfigurationSection _seccion;

    /// <param name="seccion">La sección viva: después de una recarga devuelve lo nuevo.</param>
    public ValidadorDeSeccionDeNegocio(IConfigurationSection seccion) => _seccion = seccion;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TSeccion options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var fallos = ClavesDeConfiguracion.QueNadieLee(_seccion, typeof(TSeccion))
            .Concat(options.Problemas().Select(p => $"{_seccion.Path}:{p}"))
            .ToList();

        return fallos.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(fallos);
    }
}
