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
        return SellarContenido(Payload(subject));
    }

    /// <inheritdoc />
    public bool Matches(string? sealValue, AgreementSubject subject)
    {
        ArgumentNullException.ThrowIfNull(subject);
        return Cuadra(sealValue, Payload(subject));
    }

    /// <inheritdoc />
    public string Seal(AgreementOutcomeSubject outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return SellarContenido(Payload(outcome));
    }

    /// <inheritdoc />
    public bool Matches(string? sealValue, AgreementOutcomeSubject outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return Cuadra(sealValue, Payload(outcome));
    }

    private string SellarContenido(string contenido)
    {
        using var hmac = new HMACSHA256(_key);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(contenido));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private bool Cuadra(string? sealValue, string contenido)
    {
        if (string.IsNullOrWhiteSpace(sealValue))
        {
            return false;
        }

        var esperado = SellarContenido(contenido);

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
            s.DepositHeld.ToString("0.####", CultureInfo.InvariantCulture), s.Currency);

    /// <summary>Lo sellado del desenlace, con su clase delante: nunca puede presentarse como el de un contrato.</summary>
    /// <remarks>
    /// El del contrato no lleva clase (es el de siempre, y cambiarlo invalidaría lo emitido); éste empieza por
    /// <c>desenlace</c> y lleva ocho campos, así que no hay contenido de contrato que dé el mismo.
    /// </remarks>
    private static string Payload(AgreementOutcomeSubject s)
        => string.Join('\u001f', "desenlace", s.RentalId, s.RenterId, s.EquipmentId,
            s.Damage.ToString("0.####", CultureInfo.InvariantCulture),
            s.DepositReleased?.ToString("0.####", CultureInfo.InvariantCulture) ?? string.Empty,
            s.Currency, s.StaffRef, s.ConfirmedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
}
