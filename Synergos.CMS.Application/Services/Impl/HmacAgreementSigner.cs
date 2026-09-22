using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El sello de un contrato de alquiler: HMAC-SHA256 sobre lo que el contrato afirma.
/// </summary>
/// <remarks>
/// <b>NO se llama <c>Stub</c>, y no es un descuido</b> (#155): ésta es la implementación DE
/// VERDAD, no un doble de nada. Llamar provisional al firmante de un comprobante afirmaría algo
/// falso sobre lo único que lo hace valer.
/// </remarks>
public sealed class HmacAgreementSigner : IAgreementSigner
{
    private readonly byte[] _key;

    /// <summary>Construye el firmante con su llave.</summary>
    /// <param name="key">La llave simétrica; la custodia la resuelve quien lo construye.</param>
    public HmacAgreementSigner(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length == 0)
        {
            throw new ArgumentException("La llave de firma no puede estar vacía.", nameof(key));
        }

        _key = key;
    }

    /// <inheritdoc />
    public string Seal(AgreementSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        using var hmac = new HMACSHA256(_key);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(Payload(subject)));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <inheritdoc />
    public bool Matches(string? sealValue, AgreementSubject subject)
    {
        if (string.IsNullOrWhiteSpace(sealValue))
        {
            return false;
        }

        var esperado = Seal(subject);

        // Comparación en tiempo constante: comparar con == filtra por el primer byte distinto y
        // deja que alguien adivine el sello a fuerza de cronometrar.
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(esperado),
            Encoding.UTF8.GetBytes(sealValue.Trim().ToLowerInvariant()));
    }

    /// <summary>
    /// Lo sellado, con separador que no puede aparecer en ninguno de los campos.
    /// </summary>
    /// <remarks>
    /// Sin separador, <c>("ab","c")</c> y <c>("a","bc")</c> sellarían igual — y ahí un contrato
    /// valdría como comprobante de otro.
    /// </remarks>
    private static string Payload(AgreementSubject s)
        => string.Join('\u001f', s.RentalId, s.RenterId, s.EquipmentId,
            s.DepositHeld.ToString("0.####", CultureInfo.InvariantCulture));
}
