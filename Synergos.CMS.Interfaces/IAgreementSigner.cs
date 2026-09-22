namespace Synergos.CMS.Interfaces;

/// <summary>Lo que el sello de un contrato de alquiler AFIRMA.</summary>
/// <remarks>
/// El titular va DENTRO de lo sellado a propósito: un sello sobre el identificador solo
/// probaría que el contrato existe, no de quién es — y la discusión por una garantía es
/// exactamente sobre de quién era.
/// </remarks>
/// <param name="RentalId">Qué alquiler.</param>
/// <param name="RenterId">El seudónimo de quien lo firmó.</param>
/// <param name="EquipmentId">Qué equipo salió.</param>
/// <param name="DepositHeld">Cuánto se retuvo. Es la cifra que alguien va a discutir.</param>
public sealed record AgreementSubject(
    string RentalId, string RenterId, string EquipmentId, decimal DepositHeld);

/// <summary>
/// Sella un contrato de alquiler para que alguien de FUERA pueda comprobarlo sin creernos.
/// </summary>
/// <remarks>
/// <para><b>La CUARTA pregunta del doc 12 §3.1 se contesta que SÍ</b>: quien alquiló se lleva un
/// papel que dice cuánto se le retuvo, y el día que discuta la devolución ese papel tiene que
/// valer contra nosotros. Sin sello sería nuestra palabra contra la suya, que es justo lo que un
/// comprobante existe para no ser.</para>
///
/// <para><b>Es determinista y NO vence</b>, como el id de un diploma (#45) y al revés que el
/// token de <c>/v1/signatures</c>: un contrato se re-emite igual y se consulta años después.</para>
/// </remarks>
public interface IAgreementSigner
{
    /// <summary>Sella el contrato.</summary>
    /// <param name="subject">Lo que queda afirmado.</param>
    /// <returns>El sello, opaco.</returns>
    string Seal(AgreementSubject subject);

    /// <summary>¿Este sello corresponde a este contrato?</summary>
    /// <param name="sealValue">El sello que alguien presenta.</param>
    /// <param name="subject">Contra qué se comprueba.</param>
    /// <returns><c>true</c> sólo si coinciden.</returns>
    bool Matches(string? sealValue, AgreementSubject subject);
}
