using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// La llave con la que se sellan los contratos de alquiler.
/// </summary>
/// <remarks>
/// Delega en <see cref="CustodiaDeLlaveDeFirma"/>, que es la misma custodia que usan las entradas
/// y los diplomas — promovida al TERCER consumidor, que es éste (#147). El paso S8 del molde
/// dice «la custodia: la llave, cifrada con IDataProtector» como si cada vertical escribiera la
/// suya, y eso fabricaba la duplicación que §0.B.17 prohíbe.
/// </remarks>
public sealed class AgreementSigningKeyProvider : IDisposable
{
    private readonly CustodiaDeLlaveDeFirma _custodia;

    /// <summary>Construye la custodia de esta llave.</summary>
    /// <param name="store">Dónde se guarda cifrada.</param>
    /// <param name="dataProtectionProvider">Con qué se cifra.</param>
    /// <param name="options">La sección anidada <c>Synergos:Alquiler:Agreement</c>.</param>
    /// <param name="logger">Dónde se avisa.</param>
    public AgreementSigningKeyProvider(
        IJsonEntityStore store,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<AgreementSettings> options,
        ILogger<AgreementSigningKeyProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _custodia = new CustodiaDeLlaveDeFirma(
            store,
            dataProtectionProvider,
            keyId: "agreement-signing-v1",
            protectorPurpose: "Synergos.Alquiler.AgreementSigningKey.v1",
            secretoConfigurado: () => options.Value.SigningSecret,
            sujeto: "Alquiler",
            claveDeConfiguracion: "Synergos:Alquiler:Agreement:SigningSecret",
            consecuenciaDePerderla: "los contratos ya emitidos dejarán de comprobarse",
            logger: logger);
    }

    /// <summary>La llave, resuelta una vez y cacheada.</summary>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>Los bytes de la llave.</returns>
    public Task<byte[]> GetKeyAsync(CancellationToken cancellationToken = default)
        => _custodia.GetKeyAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose() => _custodia.Dispose();
}

/// <summary>
/// <see cref="IAgreementSigner"/> que resuelve la llave perezosamente y delega en
/// <c>HmacAgreementSigner</c>.
/// </summary>
/// <remarks>
/// Es la VARIACIÓN que NO entra en la custodia compartida: cada seam de sello tiene su forma, y
/// meterlas todas en un helper daría uno con banderas
/// (<c>the_same_algorithm_is_not_the_same_thing</c>). Existe porque la llave se resuelve async y
/// sellar es puro cómputo: contagiar async al camino del comprobante por una llave que se
/// resuelve una vez sería pagar dos veces.
/// </remarks>
public sealed class LazyAgreementSigner : IAgreementSigner
{
    private readonly Lazy<IAgreementSigner> _inner;

    /// <summary>Construye el envoltorio perezoso.</summary>
    /// <param name="keys">De dónde sale la llave.</param>
    public LazyAgreementSigner(AgreementSigningKeyProvider keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        _inner = new Lazy<IAgreementSigner>(
            () => new Synergos.CMS.Application.Services.Impl.HmacAgreementSigner(
                keys.GetKeyAsync().GetAwaiter().GetResult()));
    }

    /// <inheritdoc />
    public string Seal(AgreementSubject subject) => _inner.Value.Seal(subject);

    /// <inheritdoc />
    public bool Matches(string? sealValue, AgreementSubject subject)
        => _inner.Value.Matches(sealValue, subject);
}
