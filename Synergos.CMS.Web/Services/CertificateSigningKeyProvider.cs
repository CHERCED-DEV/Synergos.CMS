using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Resuelve la llave con la que se deriva el id de los certificados de Educación
/// (ADR 0124), en este orden: el secreto configurado
/// (<c>Synergos:Academy:CertificateSigningSecret</c>) y, si no hay, una llave aleatoria
/// generada UNA vez, cifrada con <see cref="IDataProtector"/> y guardada en
/// <see cref="IJsonEntityStore"/> bajo una clave conocida.
/// </summary>
/// <remarks>
/// <para><b>Por qué persistir y no generar en memoria.</b> El id del certificado se
/// imprime en un diploma y se publica en un QR. Una llave efímera por proceso haría que
/// ese QR dejara de verificar en el primer reinicio — el mismo bug que T9 corrigió para
/// las entradas (<c>TicketSigningKeyProvider</c>), aquí con peores consecuencias: una
/// entrada vale una noche, un diploma se presenta años después.</para>
///
/// <para><b>Por qué no fail-closed cuando no hay secreto configurado.</b> Fail-closed
/// protege un secreto ADIVINABLE; aquí no hay ninguno que proteger. Lo que hace falsificable
/// un id es que se pueda calcular sin la llave, y una llave aleatoria de 256 bits persistida
/// cifrada cumple eso igual de bien que una configurada. Negarse a emitir certificados en una
/// instalación limpia no compraría seguridad: apagaría la capacidad. Lo que sí es fail-closed
/// —y donde importa— es el firmante: <c>HmacCertificateIdSigner</c> rechaza una llave vacía,
/// así que un cableado roto explota en vez de emitir ids que cualquiera recalcula.</para>
///
/// <para><b>Duplicación reconocida.</b> Este proveedor es casi gemelo de
/// <see cref="TicketSigningKeyProvider"/> (~40 líneas). No se generalizó ahora por dos
/// razones: la regla del repo es abstraer al TERCER caso, no al segundo, y hacerlo obligaría
/// a re-registrar el firmante de tickets en el composer, mezclando un refactor con esta
/// corrección. Cuando aparezca el tercer firmante, aquí está la evidencia para extraer un
/// <c>PersistentSigningKeyProvider(keyId, purpose, configuredSecret)</c>.</para>
///
/// <para>La llave se resuelve <b>perezosamente</b> y se cachea: cero I/O en el arranque
/// (ADR 0013). Va cifrada porque <see cref="IJsonEntityStore"/> guarda JSON en claro.</para>
/// </remarks>
public sealed class CertificateSigningKeyProvider : IDisposable
{
    private readonly CustodiaDeLlaveDeFirma _custodia;

    /// <summary>Construye la custodia de esta llave.</summary>
    /// <param name="store">Dónde se guarda cifrada.</param>
    /// <param name="dataProtectionProvider">Con qué se cifra.</param>
    /// <param name="options">La sección del vertical.</param>
    /// <param name="logger">Dónde se avisa.</param>
    public CertificateSigningKeyProvider(
        IJsonEntityStore store,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<AcademySettings> options,
        ILogger<CertificateSigningKeyProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _custodia = new CustodiaDeLlaveDeFirma(
            store,
            dataProtectionProvider,
            keyId: "certificate-signing-v1",
            protectorPurpose: "Synergos.Academy.CertificateSigningKey.v1",
            secretoConfigurado: () => options.Value.CertificateSigningSecret,
            sujeto: "Educación",
            claveDeConfiguracion: "Synergos:Academy:CertificateSigningSecret",
            consecuenciaDePerderla: "los certificados ya emitidos dejarán de verificar",
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
/// <see cref="ICertificateIdSigner"/> que resuelve la llave perezosamente vía
/// <see cref="CertificateSigningKeyProvider"/> y delega en
/// <c>HmacCertificateIdSigner</c>.
/// </summary>
/// <remarks>
/// Existe por la misma razón que <c>LazyTicketSigner</c>: la llave es asíncrona de
/// resolver (puede haber que leerla o crearla) y <see cref="ICertificateIdSigner"/> es
/// síncrono a propósito — derivar y comparar un id es puro cómputo, y volverlo async
/// contagiaría async a toda la emisión.
/// </remarks>
public sealed class LazyCertificateIdSigner : ICertificateIdSigner
{
    private readonly CertificateSigningKeyProvider _keys;
    private readonly Lazy<ICertificateIdSigner> _inner;

    public LazyCertificateIdSigner(CertificateSigningKeyProvider keys)
    {
        _keys = keys;
        _inner = new Lazy<ICertificateIdSigner>(
            () => new Synergos.CMS.Application.Services.Impl.HmacCertificateIdSigner(
                _keys.GetKeyAsync().GetAwaiter().GetResult()),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Sign(CertificateSubject subject) => _inner.Value.Sign(subject);

    public bool Matches(string? certificateId, CertificateSubject subject)
        => _inner.Value.Matches(certificateId, subject);
}
