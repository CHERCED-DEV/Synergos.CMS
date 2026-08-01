using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
public sealed class CertificateSigningKeyProvider
{
    private const string ResourceType = "keys";

    /// <summary>Clave fija: es UNA llave, no una colección — se recupera por nombre.</summary>
    private const string KeyId = "certificate-signing-v1";
    private const string ProtectorPurpose = "Synergos.Academy.CertificateSigningKey.v1";
    private const int GeneratedKeyBytes = 32; // 256 bits, el tamaño natural para HMAC-SHA256

    private readonly IJsonEntityStore _store;
    private readonly IDataProtector _protector;
    private readonly IOptions<AcademySettings> _options;
    private readonly ILogger<CertificateSigningKeyProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _cached;

    public CertificateSigningKeyProvider(
        IJsonEntityStore store,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<AcademySettings> options,
        ILogger<CertificateSigningKeyProvider> logger)
    {
        _store = store;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _options = options;
        _logger = logger;
    }

    public async Task<byte[]> GetKeyAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var configured = _options.Value.CertificateSigningSecret;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                _cached = Encoding.UTF8.GetBytes(configured);
                return _cached;
            }

            // Sin secreto configurado: se reusa el que ya se generó, o se crea uno.
            var stored = await _store.ReadAsync(ResourceType, KeyId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                var recovered = TryUnprotect(stored);
                if (recovered is not null)
                {
                    _cached = recovered;
                    return _cached;
                }
                // Llave ilegible (keyring rotado, fichero corrupto): se genera otra. Los
                // certificados ya emitidos dejan de verificar — se registra fuerte para que
                // el operador sepa POR QUÉ, en vez de enterarse por un empleador.
                _logger.LogError(
                    "Educación: la llave de firma de certificados guardada no se pudo descifrar. Se generará una " +
                    "nueva y los certificados ya emitidos dejarán de verificar. Configure " +
                    "Synergos:Academy:CertificateSigningSecret.");
            }

            var generated = RandomNumberGenerator.GetBytes(GeneratedKeyBytes);
            await _store.WriteAsync(
                ResourceType,
                KeyId,
                JsonSerializer.Serialize(_protector.Protect(Convert.ToBase64String(generated))),
                cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Educación: no hay Synergos:Academy:CertificateSigningSecret — se generó una llave de firma de " +
                "certificados y se guardó cifrada. Configure el secreto para poder rotarlo y compartirlo entre instancias.");
            _cached = generated;
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    private byte[]? TryUnprotect(string storedJson)
    {
        try
        {
            var cipher = JsonSerializer.Deserialize<string>(storedJson);
            if (string.IsNullOrWhiteSpace(cipher))
            {
                return null;
            }
            return Convert.FromBase64String(_protector.Unprotect(cipher));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }
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
