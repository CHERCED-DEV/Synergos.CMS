using System.Security.Cryptography;
using System.Text;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Las dos firmas de Wompi: la de <b>integridad</b> (sale con la transacción) y
/// el <b>checksum de evento</b> (llega con el webhook).
/// </summary>
/// <remarks>
/// <para>Aparte del provider a propósito: es lógica pura, sin red, y es donde
/// se concentran los errores de integración. La investigación de PSPs lo pone
/// primero en la lista de lo que más falla en producción — el algoritmo de
/// concatenación es frágil y un carácter de más deja la firma inválida sin
/// ningún mensaje útil del otro lado.</para>
/// <para>El secreto vive SOLO server-side. Nada de esto puede calcularse en el
/// navegador.</para>
/// </remarks>
public static class WompiSignature
{
    /// <summary>
    /// Firma de integridad de una transacción:
    /// <c>SHA256(referencia + montoEnCentavos + moneda + secreto)</c>, y con
    /// <c>SHA256(referencia + montoEnCentavos + moneda + expiración + secreto)</c>
    /// cuando la transacción expira.
    /// </summary>
    /// <param name="reference">Referencia del comercio. Es la que después
    ///   permite reconciliar; acá va tal cual, sin normalizar.</param>
    /// <param name="amountInCents">Monto en CENTAVOS. Wompi no acepta decimales:
    ///   mandar pesos donde espera centavos cobra cien veces menos y la firma
    ///   igual valida, porque el monto entra en el hash tal como se envía.</param>
    /// <param name="currency">ISO 4217 en mayúsculas (<c>"COP"</c>).</param>
    /// <param name="integritySecret">Secreto de integridad del comercio.</param>
    /// <param name="expiresAtIso8601">Expiración en ISO-8601, si la hay. Cuando
    ///   se manda, entra en el hash ANTES del secreto — y si se manda en la
    ///   transacción pero no en la firma (o al revés), Wompi la rechaza.</param>
    public static string Integrity(
        string reference,
        long amountInCents,
        string currency,
        string integritySecret,
        string? expiresAtIso8601 = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(integritySecret);
        if (amountInCents <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountInCents),
                "El monto en centavos debe ser mayor a cero.");
        }

        var sb = new StringBuilder()
            .Append(reference)
            .Append(amountInCents.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(currency.ToUpperInvariant());

        if (!string.IsNullOrWhiteSpace(expiresAtIso8601))
        {
            sb.Append(expiresAtIso8601);
        }

        return Sha256Hex(sb.Append(integritySecret).ToString());
    }

    /// <summary>
    /// Checksum de un evento entrante (header <c>X-Event-Checksum</c>):
    /// <c>SHA256(valores de las propiedades firmadas, en orden + timestamp + secreto)</c>.
    /// </summary>
    /// <param name="signedPropertyValues">Los valores que Wompi declara haber
    ///   firmado, EN EL ORDEN en que los declara. El evento trae la lista de
    ///   nombres en <c>signature.properties</c>, y la documentación advierte
    ///   explícitamente que no se hardcodee: varía por tipo de evento. Por eso
    ///   este método recibe los VALORES ya resueltos y no adivina la forma del
    ///   payload — quien lee el JSON decide, y este cálculo sólo concatena.</param>
    /// <param name="timestamp">El <c>timestamp</c> UNIX del propio evento.</param>
    /// <param name="eventsSecret">Secreto de eventos del comercio.</param>
    public static string EventChecksum(
        IEnumerable<string?> signedPropertyValues,
        long timestamp,
        string eventsSecret)
    {
        ArgumentNullException.ThrowIfNull(signedPropertyValues);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventsSecret);

        var sb = new StringBuilder();
        foreach (var value in signedPropertyValues)
        {
            // Una propiedad ausente concatena vacío, no "null": la ausencia es
            // parte del mensaje firmado y hay que reproducirla igual.
            sb.Append(value ?? string.Empty);
        }

        return Sha256Hex(sb
            .Append(timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Append(eventsSecret)
            .ToString());
    }

    /// <summary>
    /// Compara dos checksums en tiempo constante.
    /// </summary>
    /// <remarks>
    /// Con <c>==</c> el tiempo de comparación depende de cuántos caracteres
    /// coinciden, y eso deja filtrar el valor esperado byte a byte. Es el mismo
    /// criterio que ya aplica <c>WebhookSigner</c> en este repo.
    /// </remarks>
    public static bool ChecksumMatches(string? expected, string? received)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(received))
        {
            return false;
        }
        var a = Encoding.UTF8.GetBytes(expected.Trim().ToLowerInvariant());
        var b = Encoding.UTF8.GetBytes(received.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Pasa un monto en la moneda a CENTAVOS enteros, que es lo único que Wompi
    /// acepta.
    /// </summary>
    /// <remarks>
    /// Redondea a la unidad más cercana en vez de truncar: truncar regala
    /// sistemáticamente hasta un centavo por transacción, y sobre volumen eso
    /// deja de ser un redondeo y pasa a ser un descuadre.
    /// </remarks>
    public static long ToCents(decimal amount)
        => (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    private static string Sha256Hex(string input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
