using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// La custodia de una llave simétrica de firma: el secreto configurado si lo hay, y si no una
/// llave generada UNA vez, cifrada con <see cref="IDataProtector"/> y guardada por nombre.
/// </summary>
/// <remarks>
/// <para><b>Esto era DOS copias y el tercer vertical que sella lo convirtió en promoción</b>
/// (#147). `TicketSigningKeyProvider` (169 líneas) y `CertificateSigningKeyProvider` (180) eran
/// el mismo SUJETO —la custodia de una llave simétrica— con la misma POLÍTICA —secreto, o llave
/// guardada, o generar y avisar—: medido normalizando los sustantivos, el diff son la cadena del
/// protector, el tipo del POCO, el nombre de la propiedad y el texto del log. Ni una decisión
/// distinta. Con la tercera copia habría tres sitios donde arreglar lo que aprenda uno.</para>
///
/// <para><b>Lo que NO se traga esta clase son las VARIACIONES</b>
/// (<c>the_same_algorithm_is_not_the_same_thing</c>): el envoltorio perezoso de cada firmante se
/// queda en su vertical, porque cada seam tiene su forma —firmar un token de QR, firmar el id de
/// un diploma, sellar un contrato— y meterlas acá daría un helper con banderas.</para>
///
/// <para><b>Y el aviso nombra la CONSECUENCIA de cada vertical</b>, que tampoco es cosmético: a
/// quien lea el log le sirve saber que los QR dejan de validar en la puerta, no que «una llave
/// cambió».</para>
///
/// <para>La llave se resuelve <b>perezosamente</b> y se cachea: cero I/O en el arranque
/// (ADR 0013).</para>
/// </remarks>
public sealed class CustodiaDeLlaveDeFirma : IDisposable
{
    private const string ResourceType = "keys";
    private const int GeneratedKeyBytes = 32; // 256 bits, el tamaño natural para HMAC-SHA256

    private readonly IJsonEntityStore _store;
    private readonly IDataProtector _protector;
    private readonly Func<string?> _secretoConfigurado;
    private readonly string _keyId;
    private readonly string _sujeto;
    private readonly string _claveDeConfiguracion;
    private readonly string _consecuenciaDePerderla;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _cached;

    /// <summary>Construye la custodia de UNA llave.</summary>
    /// <param name="store">Dónde se guarda la llave cifrada, por nombre.</param>
    /// <param name="dataProtectionProvider">Con qué se cifra.</param>
    /// <param name="keyId">
    /// La clave fija bajo la que se guarda. Es UNA llave, no una colección: se recupera por
    /// nombre, y por eso no sirve <c>IPrivateFileStore</c>, que asigna un id opaco propio.
    /// </param>
    /// <param name="protectorPurpose">El propósito del protector; distingue una llave de otra.</param>
    /// <param name="secretoConfigurado">Cómo se lee el secreto del POCO del vertical.</param>
    /// <param name="sujeto">Con qué nombre se identifica el vertical en el log.</param>
    /// <param name="claveDeConfiguracion">La clave que el operador tiene que poner.</param>
    /// <param name="consecuenciaDePerderla">Qué deja de funcionar si la llave cambia.</param>
    /// <param name="logger">Dónde se avisa.</param>
    public CustodiaDeLlaveDeFirma(
        IJsonEntityStore store,
        IDataProtectionProvider dataProtectionProvider,
        string keyId,
        string protectorPurpose,
        Func<string?> secretoConfigurado,
        string sujeto,
        string claveDeConfiguracion,
        string consecuenciaDePerderla,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _protector = dataProtectionProvider.CreateProtector(protectorPurpose);
        _secretoConfigurado = secretoConfigurado ?? throw new ArgumentNullException(nameof(secretoConfigurado));
        _keyId = keyId;
        _sujeto = sujeto;
        _claveDeConfiguracion = claveDeConfiguracion;
        _consecuenciaDePerderla = consecuenciaDePerderla;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>La llave, resuelta una vez y cacheada.</summary>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>Los bytes de la llave.</returns>
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

            var configured = _secretoConfigurado();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                _cached = Encoding.UTF8.GetBytes(configured);
                return _cached;
            }

            var stored = await _store.ReadAsync(ResourceType, _keyId, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                var recovered = TryUnprotect(stored);
                if (recovered is not null)
                {
                    _cached = recovered;
                    return _cached;
                }

                // Llave ilegible (keyring rotado, fichero corrupto): se genera otra, y se avisa
                // FUERTE con la consecuencia del vertical — para que el operador sepa POR QUÉ en
                // vez de descubrirlo en la puerta del evento.
                _logger.LogError(
                    "{Sujeto}: la llave de firma guardada no se pudo descifrar. Se generará una nueva y {Consecuencia}. Configure {Clave}.",
                    _sujeto, _consecuenciaDePerderla, _claveDeConfiguracion);
            }

            var generated = RandomNumberGenerator.GetBytes(GeneratedKeyBytes);
            await _store.WriteAsync(
                ResourceType,
                _keyId,
                JsonSerializer.Serialize(_protector.Protect(Convert.ToBase64String(generated))),
                cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "{Sujeto}: no hay {Clave} — se generó una llave de firma y se guardó cifrada. Configure el secreto para poder rotarlo y compartirlo entre instancias.",
                _sujeto, _claveDeConfiguracion);
            _cached = generated;
            return _cached;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Suelta el semáforo.</summary>
    public void Dispose() => _gate.Dispose();

    private byte[]? TryUnprotect(string storedJson)
    {
        try
        {
            var cipher = JsonSerializer.Deserialize<string>(storedJson);
            return string.IsNullOrWhiteSpace(cipher)
                ? null
                : Convert.FromBase64String(_protector.Unprotect(cipher));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }
}
