using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ITicketSigner"/> (T9) — firma el token del QR con HMAC-SHA256.
/// </summary>
/// <remarks>
/// <para>Formato: <c>SYN-TKT-{eventId}-{ticketId}-v{qrVersion}.{hex}</c>. El payload se
/// deja <b>legible a propósito</b>: un operador puede leer de qué evento y ticket habla
/// un QR sin descifrar nada, y eso no debilita la firma (el secreto no está ahí). Lo que
/// no se puede es <b>fabricarlo</b> sin la llave.</para>
/// <para>Se separa a propósito la firma del payload con <c>'.'</c> y se parte por el
/// ÚLTIMO punto: los ids del dominio no llevan puntos hoy, pero si algún día los llevan,
/// partir por el primero rompería la verificación en silencio.</para>
/// <para><b>Por qué no se reusa <c>WebhookSigner</c>:</b> aquel firma
/// <c>"{timestamp}.{body}"</c> con una ventana de replay de ±5 minutos, porque un webhook
/// que llega tarde es sospechoso. Una entrada es lo contrario: se compra semanas antes,
/// se imprime, y debe seguir valiendo el día del evento. Meterle un timestamp la
/// invalidaría sola. Comparten el primitivo (HMAC-SHA256 del BCL) y la convención de
/// formato, no la política.</para>
/// <para>Sin llave no se firma ni se verifica: el ctor rechaza una llave vacía. Un
/// firmante con llave vacía produciría tokens que cualquiera puede recalcular — peor que
/// no firmar, porque parecería que sí.</para>
/// </remarks>
public sealed class HmacTicketSigner : ITicketSigner
{
    private const string Prefix = "SYN-TKT-";
    private const char SignatureSeparator = '.';

    private readonly byte[] _key;

    public HmacTicketSigner(byte[] key)
    {
        if (key is null || key.Length == 0)
        {
            throw new ArgumentException(
                "La llave de firma de tickets es obligatoria: sin ella el token sería falsificable.",
                nameof(key));
        }
        _key = key;
    }

    public string Sign(TicketToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var payload = BuildPayload(token);
        return payload + SignatureSeparator + ComputeHex(payload);
    }

    public TicketToken? Verify(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var trimmed = rawToken.Trim();
        var separator = trimmed.LastIndexOf(SignatureSeparator);
        if (separator <= 0 || separator == trimmed.Length - 1)
        {
            // Sin firma. Incluye el caso importante: alguien tecleando el `ticketId`
            // suelto, que es justo lo que la UI imprime bajo el QR.
            return null;
        }

        var payload = trimmed[..separator];
        var providedHex = trimmed[(separator + 1)..];

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(providedHex);
        }
        catch (FormatException)
        {
            return null;
        }

        var expected = ComputeHash(payload);
        // Tiempo constante — nunca `==` de string (evita timing attack). Misma regla que
        // PaymentWebhookVerifier.
        if (provided.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(provided, expected))
        {
            return null;
        }

        return ParsePayload(payload);
    }

    // ── Formato ──────────────────────────────────────────────────────────────────

    private static string BuildPayload(TicketToken token) =>
        $"{Prefix}{token.EventId}-{token.TicketId}-v{token.QrVersion.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Deshace el payload. Se parte desde la DERECHA (la versión primero, luego el
    /// ticketId) porque el <c>eventId</c> puede llevar guiones y partir desde la
    /// izquierda lo trocearía mal.
    /// </summary>
    private static TicketToken? ParsePayload(string payload)
    {
        if (!payload.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var body = payload[Prefix.Length..];

        var versionAt = body.LastIndexOf("-v", StringComparison.Ordinal);
        if (versionAt <= 0)
        {
            return null;
        }
        if (!int.TryParse(body[(versionAt + 2)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var qrVersion))
        {
            return null;
        }

        var head = body[..versionAt];
        var ticketAt = head.LastIndexOf('-');
        if (ticketAt <= 0 || ticketAt == head.Length - 1)
        {
            return null;
        }

        var eventId = head[..ticketAt];
        var ticketId = head[(ticketAt + 1)..];
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(ticketId))
        {
            return null;
        }

        return new TicketToken(eventId, ticketId, qrVersion);
    }

    private byte[] ComputeHash(string payload) =>
        HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));

    private string ComputeHex(string payload) =>
        Convert.ToHexString(ComputeHash(payload)).ToLowerInvariant();
}
