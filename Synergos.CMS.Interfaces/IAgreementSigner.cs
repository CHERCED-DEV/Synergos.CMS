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
/// <param name="Currency">En qué moneda: «400000» sin ella no es una cifra que se pueda discutir (#204).</param>
public sealed record AgreementSubject(
    string RentalId, string RenterId, string EquipmentId, decimal DepositHeld, string Currency);

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

/// <summary>Lo que devuelve sellar un contrato: el sello y quién lo selló, o «pendiente».</summary>
/// <param name="Valor">El sello, opaco; vacío si quedó pendiente.</param>
/// <param name="SelladoPor">
/// Quién lo selló (<c>local</c>, <c>api:{llave}</c>), o quién tiene que sellarlo si quedó pendiente (<c>api</c>). Es lo
/// que el contrato registra y con lo que se comprueba siempre.
/// </param>
/// <param name="Pendiente">
/// El servicio de firma no contestó: el contrato sale sin sello y se sella al volver, con el mismo que debía sellarlo.
/// </param>
public sealed record SelloDelContrato(string Valor, string SelladoPor, bool Pendiente = false);

/// <summary>
/// Sella y comprueba contratos de alquiler con la custodia que el despliegue eligió (research R11 de la línea 005).
/// </summary>
/// <remarks>
/// <para><b>Asíncrono y con tres respuestas al comprobar</b>, al revés que <see cref="IAgreementSigner"/>: cuando la llave
/// la custodia <c>Api.Signing</c>, «no sé» (el servicio no contestó) es una respuesta distinta de «no cuadra», y darla por
/// <c>false</c> diría que un contrato bueno es falso.</para>
///
/// <para><b>Cuál gana</b>: para sellar, el del interruptor <c>Synergos:Alquiler:Agreement:Mode</c>; para comprobar, el que
/// registra el contrato en <c>SelladoPor</c> (vacío = local, el de todos los contratos anteriores al interruptor). Así
/// conmutar no deja sin comprobar nada ya emitido.</para>
/// </remarks>
public interface ISelladorDeContratos
{
    /// <summary>Quién es: lo que <c>SelladoPor</c> lleva antes de los dos puntos (<c>local</c>, <c>api</c>).</summary>
    string Nombre { get; }

    /// <summary>Sella el contrato.</summary>
    /// <param name="subject">Lo que queda afirmado.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns>El sello y quién lo selló, o pendiente si el servicio de firma no contestó.</returns>
    Task<SelloDelContrato> SellarAsync(AgreementSubject subject, CancellationToken cancellationToken = default);

    /// <summary>¿Este sello corresponde a este contrato?</summary>
    /// <param name="sello">El sello que el contrato registra.</param>
    /// <param name="subject">Contra qué se comprueba.</param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <returns><c>true</c> si cuadra, <c>false</c> si no, <c>null</c> si no se pudo comprobar.</returns>
    Task<bool?> ComprobarAsync(string? sello, AgreementSubject subject, CancellationToken cancellationToken = default);
}
