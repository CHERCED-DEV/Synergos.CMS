using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Resuelve la llave con la que se firman los QR de las entradas (T9), en este orden:
/// el secreto configurado (<c>Synergos:Events:TicketSigningSecret</c>) y, si no hay, una
/// llave aleatoria generada UNA vez, cifrada con <see cref="IDataProtector"/> y guardada
/// en <see cref="IJsonEntityStore"/> bajo una clave conocida.
/// </summary>
/// <remarks>
/// <para><b>Por qué no basta con generar una llave en memoria:</b> el bug que T9 corrige
/// es exactamente ese. El QR anterior derivaba de <c>String.GetHashCode()</c>, que en
/// .NET Core está randomizado por proceso, así que cambiaba en cada reinicio. Una llave
/// efímera repetiría la historia: las entradas emitidas ayer dejarían de validar hoy.</para>
/// <para><b>Por qué NO se usa <see cref="IPrivateFileStore"/> (ADR 0109), que era la
/// opción obvia:</b> ese almacén <b>genera su propio id opaco</b> y descarta el nombre
/// que le pasa el llamador — es justo lo que lo hace seguro para documentos subidos
/// (nadie elige un id adivinable ni pisa un fichero ajeno), y justo lo que lo inhabilita
/// para un singleton que hay que <b>recuperar por nombre</b>. Se intentó, y la llave se
/// guardó bajo un GUID: al reiniciar no se encontraba y se generaba otra, reproduciendo
/// el bug que T9 venía a cerrar. Lo detectó la verificación en vivo, no los tests.
/// <see cref="IJsonEntityStore"/> sí acepta la clave del llamador, que es lo que hace
/// falta aquí.</para>
/// <para><b>Por qué no un default en el repo:</b> un secreto commiteado es un secreto
/// conocido, y firmar con él daría tokens falsificables con apariencia de seguros.</para>
/// <para>La llave se resuelve <b>perezosamente</b> y se cachea: cero I/O en el arranque
/// (ADR 0013). Va cifrada porque <see cref="IJsonEntityStore"/> guarda JSON en claro.</para>
/// </remarks>
public sealed class TicketSigningKeyProvider
{
    private const string ResourceType = "keys";
    /// <summary>Clave fija: es UNA llave, no una colección — se recupera por nombre.</summary>
    private const string KeyId = "ticket-signing-v1";
    private const string ProtectorPurpose = "Synergos.Tickets.SigningKey.v1";
    private const int GeneratedKeyBytes = 32; // 256 bits, el tamaño natural para HMAC-SHA256

    private readonly IJsonEntityStore _store;
    private readonly IDataProtector _protector;
    private readonly IOptions<EventsSettings> _options;
    private readonly ILogger<TicketSigningKeyProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private byte[]? _cached;

    public TicketSigningKeyProvider(
        IJsonEntityStore store,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<EventsSettings> options,
        ILogger<TicketSigningKeyProvider> logger)
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

            var configured = _options.Value.TicketSigningSecret;
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
                // Llave ilegible (keyring rotado, fichero corrupto): se genera otra. Las
                // entradas ya emitidas dejan de validar — se registra fuerte para que el
                // operador sepa POR QUÉ, en vez de descubrirlo en la puerta del evento.
                _logger.LogError(
                    "Tickets: la llave de firma guardada no se pudo descifrar. Se generará una nueva y " +
                    "los QR ya emitidos dejarán de ser válidos. Configure Synergos:Events:TicketSigningSecret.");
            }

            var generated = RandomNumberGenerator.GetBytes(GeneratedKeyBytes);
            await _store.WriteAsync(
                ResourceType,
                KeyId,
                JsonSerializer.Serialize(_protector.Protect(Convert.ToBase64String(generated))),
                cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Tickets: no hay Synergos:Events:TicketSigningSecret — se generó una llave de firma y se guardó cifrada. " +
                "Configure el secreto para poder rotarlo y compartirlo entre instancias.");
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
/// <see cref="ITicketSigner"/> que resuelve la llave perezosamente vía
/// <see cref="TicketSigningKeyProvider"/> y delega en <c>HmacTicketSigner</c>.
/// </summary>
/// <remarks>
/// Existe porque la llave es asíncrona de resolver (puede haber que leerla o crearla) y
/// <see cref="ITicketSigner"/> es síncrono a propósito: firmar y verificar son puro
/// cómputo, y hacer async la puerta de un evento por una llave que se resuelve una vez
/// contagiaría async a todo el camino del check-in.
/// </remarks>
public sealed class LazyTicketSigner : ITicketSigner
{
    private readonly TicketSigningKeyProvider _keys;
    private readonly Lazy<ITicketSigner> _inner;

    public LazyTicketSigner(TicketSigningKeyProvider keys)
    {
        _keys = keys;
        _inner = new Lazy<ITicketSigner>(
            () => new Synergos.CMS.Application.Services.Impl.HmacTicketSigner(
                _keys.GetKeyAsync().GetAwaiter().GetResult()),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Sign(TicketToken token) => _inner.Value.Sign(token);

    public TicketToken? Verify(string? rawToken) => _inner.Value.Verify(rawToken);
}
