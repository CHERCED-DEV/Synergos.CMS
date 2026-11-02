using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El secreto de la llave pública de una persona (<see cref="SeudonimoDePersona.Publico"/>): el configurado, o uno
/// generado una vez y guardado cifrado.
/// </summary>
/// <remarks>
/// Delega en <see cref="CustodiaDeLlaveDeFirma"/>, la custodia de las entradas, los contratos y los diplomas: la misma
/// política —el secreto, o la llave guardada, o generar y avisar— y otro uso de la llave (012-blogs-social, I0.b). Por
/// eso va en el respaldo como aquéllas: la llave cifrada en el almacén, y con qué descifrarla en <c>cms-dpkeys</c>.
/// </remarks>
public sealed class SecretoDeLaCaraPublica : IDisposable
{
    private readonly CustodiaDeLlaveDeFirma _custodia;

    /// <summary>Construye la custodia de este secreto.</summary>
    /// <param name="store">Dónde se guarda cifrado.</param>
    /// <param name="dataProtectionProvider">Con qué se cifra.</param>
    /// <param name="options">La sección <c>Synergos:IdentidadPublica</c>.</param>
    /// <param name="logger">Dónde se avisa.</param>
    public SecretoDeLaCaraPublica(
        IJsonEntityStore store,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<IdentidadPublicaSettings> options,
        ILogger<SecretoDeLaCaraPublica> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _custodia = new CustodiaDeLlaveDeFirma(
            store,
            dataProtectionProvider,
            keyId: "identidad-publica-v1",
            protectorPurpose: "Synergos.IdentidadPublica.Llave.v1",
            secretoConfigurado: () => options.Value.Secreto,
            sujeto: "La cara pública",
            claveDeConfiguracion: "Synergos:IdentidadPublica:Secreto",
            consecuenciaDePerderla: "la llave y el alias públicos de cada miembro cambiarán, y su perfil nuevo no se abrirá hasta que vuelva a actuar",
            logger: logger);
    }

    /// <summary>El secreto, resuelto una vez y cacheado.</summary>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>Los bytes del secreto.</returns>
    public Task<byte[]> GetKeyAsync(CancellationToken cancellationToken = default)
        => _custodia.GetKeyAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _custodia.Dispose();
}
