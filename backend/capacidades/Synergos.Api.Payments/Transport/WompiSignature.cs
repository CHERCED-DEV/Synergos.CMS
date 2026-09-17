using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Synergos.Api.Payments.Transport;

/// <summary>
/// Las dos firmas de Wompi: la de <b>integridad</b> (sale con la transacción) y el <b>checksum
/// de evento</b> (llega con el webhook).
/// </summary>
/// <remarks>
/// <para>Aparte del proveedor a propósito: es lógica pura, sin red, y es donde se concentran los
/// errores de esta integración. El algoritmo es una concatenación, así que un carácter de más
/// deja la firma inválida <b>sin ningún mensaje útil del otro lado</b> — se ve como «Wompi
/// rechazó la transacción» y no dice por qué.</para>
///
/// <para><b>Esto se re-escribió, no se copió</b> del gemelo que el CMS tiene en
/// <c>Synergos.CMS.Application</c> (ADR 0116): una capacidad no puede referenciar al CMS ni al
/// revés, y el vocabulario de destino es otro. Lo que sí se conserva es el algoritmo, que lo
/// define el proveedor y no nosotros.</para>
/// </remarks>
public static class WompiSignature
{
    /// <summary>
    /// Firma de integridad de una transacción:
    /// <c>SHA256(referencia + montoEnCentavos + moneda + secreto)</c>.
    /// </summary>
    /// <param name="reference">Referencia del comercio — la que después permite conciliar.</param>
    /// <param name="amountInCents">
    /// Monto en CENTAVOS. Wompi no acepta decimales, y mandar pesos donde espera centavos cobra
    /// cien veces menos <b>con la firma válida</b>, porque el monto entra en el hash tal como se
    /// envía. Es el error que no se ve hasta que alguien cuadra la caja.
    /// </param>
    /// <param name="currency">ISO 4217 en mayúsculas.</param>
    /// <param name="integritySecret">Secreto de integridad del comercio.</param>
    public static string Integrity(string reference, long amountInCents, string currency, string integritySecret)
        => Sha256Hex(new StringBuilder()
            .Append(reference)
            .Append(amountInCents.ToString(CultureInfo.InvariantCulture))
            .Append(currency.ToUpperInvariant())
            .Append(integritySecret)
            .ToString());

    /// <summary>
    /// Checksum de un evento entrante (cabecera <c>X-Event-Checksum</c>):
    /// <c>SHA256(valores firmados, en orden + timestamp + secreto)</c>.
    /// </summary>
    /// <param name="signedPropertyValues">
    /// Los valores que el evento declara haber firmado, <b>en el orden en que los declara</b>. El
    /// propio evento trae la lista de nombres en <c>signature.properties</c>, y la documentación
    /// advierte de no cablearla: varía por tipo de evento. Por eso acá llegan los valores ya
    /// resueltos y este cálculo sólo concatena.
    /// </param>
    /// <param name="timestamp">El <c>timestamp</c> UNIX del propio evento.</param>
    /// <param name="eventsSecret">Secreto de eventos del comercio.</param>
    public static string EventChecksum(IEnumerable<string?> signedPropertyValues, long timestamp, string eventsSecret)
    {
        var sb = new StringBuilder();
        foreach (var value in signedPropertyValues)
        {
            // Una propiedad ausente concatena vacío, no "null": la ausencia es parte del mensaje
            // firmado y hay que reproducirla igual.
            sb.Append(value ?? string.Empty);
        }

        return Sha256Hex(sb
            .Append(timestamp.ToString(CultureInfo.InvariantCulture))
            .Append(eventsSecret)
            .ToString());
    }

    /// <summary>
    /// Compara dos checksums en tiempo constante.
    /// </summary>
    /// <remarks>
    /// Con <c>==</c> el tiempo de comparación depende de cuántos caracteres coinciden, y eso deja
    /// filtrar el valor esperado carácter a carácter. Es el mismo criterio del verificador de
    /// <c>Api.Notifications</c>.
    /// </remarks>
    public static bool ChecksumMatches(string? expected, string? received)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(received)) return false;

        var a = Encoding.UTF8.GetBytes(expected.Trim().ToLowerInvariant());
        var b = Encoding.UTF8.GetBytes(received.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Pasa un monto a CENTAVOS enteros, que es lo único que Wompi acepta.
    /// </summary>
    /// <remarks>
    /// Redondea a la unidad más cercana en vez de truncar: truncar regala sistemáticamente hasta
    /// un centavo por transacción, y sobre volumen eso deja de ser un redondeo y pasa a ser un
    /// descuadre.
    /// </remarks>
    public static long ToCents(decimal amount) => (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    private static string Sha256Hex(string input)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
}
