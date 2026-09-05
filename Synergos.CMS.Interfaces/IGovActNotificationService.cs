namespace Synergos.CMS.Interfaces;

/// <summary>
/// Un acto administrativo puesto en conocimiento de un ciudadano.
/// </summary>
/// <param name="Id">Identificador de la notificación.</param>
/// <param name="CaseId">De qué expediente.</param>
/// <param name="Radicado">El número que el ciudadano reconoce.</param>
/// <param name="Title">Qué acto es — «Resolución 1234 de 2026».</param>
/// <param name="Body">El texto que se le pone en conocimiento.</param>
/// <param name="DocumentRef">El PDF del acto, si lo hay. <b>Una referencia, no bytes.</b></param>
/// <param name="NotifiedAtUtc">Cuándo la entidad lo puso a disposición.</param>
/// <param name="AcknowledgeBeforeUtc">Hasta cuándo se admite registrar el acceso.</param>
/// <param name="OpenedAtUtc">Cuándo lo abrió el ciudadano. <b>Nulo mientras no lo abra.</b></param>
/// <param name="OpenedBy">Quién lo abrió — la llave del Member, no un correo del formulario.</param>
/// <param name="OpenedWith">Con qué fuerza se afirmó que era él (<see cref="IdentityAssertions"/>).</param>
/// <remarks>
/// <para><b>El término empieza en <see cref="OpenedAtUtc"/>, no en <see cref="NotifiedAtUtc"/></b>,
/// y ésa es la razón de que esto exista. Un correo enviado prueba que salió del servidor, no que
/// llegó a quien tenía que llegar; lo que la entidad tiene que poder sostener el día que alguien
/// recurre tarde es <i>cuándo accedió</i> y <i>cómo se supo que era él</i>.</para>
///
/// <para><b>Abrir dos veces no mueve nada.</b> El primer acceso es el que cuenta: si el segundo
/// pisara la fecha, el término se correría solo cada vez que el ciudadano vuelve a mirar su
/// expediente — y correría a favor de quien lo mira, que es al revés de lo que la ley pretende.</para>
/// </remarks>
public sealed record GovActNotification(
    string Id,
    string CaseId,
    string Radicado,
    string Title,
    string Body,
    string? DocumentRef,
    DateTimeOffset NotifiedAtUtc,
    DateTimeOffset? AcknowledgeBeforeUtc = null,
    DateTimeOffset? OpenedAtUtc = null,
    Guid? OpenedBy = null,
    string? OpenedWith = null)
{
    /// <summary>Si el ciudadano ya lo abrió — o sea, si el término ya empezó.</summary>
    public bool Opened => OpenedAtUtc is not null;
}

/// <summary>
/// Alguien intentó abrir un acto que no le fue notificado.
/// </summary>
/// <remarks>
/// <para><b>Es un tipo y no un mensaje</b> porque el borde tiene que distinguirlo de «se pasó el
/// plazo» —403 y 409 le dicen al ciudadano cosas opuestas sobre qué hacer— y las dos
/// implementaciones del seam lo producen. Mirar el texto de un <c>InvalidOperationException</c>
/// habría atado ese código de estado a una frase en español que cualquiera reescribe sin
/// enterarse de que estaba decidiendo una respuesta HTTP.</para>
///
/// <para>Hacia FUERA no se distingue de «no existe»: el radicado es secuencial, así que decirle a
/// quien tantea que el acto existe pero es de otro ya es información. Hacia dentro sí, porque el
/// rastro tiene que poder decir que alguien intentó abrir lo ajeno.</para>
/// </remarks>
public sealed class GovActNotAddresseeException : InvalidOperationException
{
    public GovActNotAddresseeException()
        : base("La notificación no es de quien intenta abrirla.") { }
}

/// <summary>
/// Notificar un acto administrativo y registrar cuándo lo abre el ciudadano.
/// </summary>
/// <remarks>
/// <para><b>Es el primer consumidor de <c>Api.Messaging</c></b> (HU #62). Esa capacidad ya
/// modelaba todo esto —hilo, mensaje con adjuntos por referencia, plazo de acuse, y el acuse con
/// su afirmación de identidad verificada (HU #14)— y llevaba meses sin nadie que la usara.</para>
///
/// <para><b>El expediente se queda de este lado</b>: qué acto es, de qué trámite, quién es el
/// ciudadano dueño y la vista de «mis notificaciones». Una capacidad de mensajería no sabe qué es
/// un radicado, y meterle ese sustantivo la inutilizaría para el siguiente dominio
/// (<c>CLAUDE.md</c> §12).</para>
///
/// <para><b>Quién abre sale de la SESIÓN, nunca del cuerpo.</b> El radicado es secuencial
/// (<c>SG-2026-000001</c>), así que un identificador que viaje en la petición se enumera contando.
/// Es la misma decisión que ya tomó la cara de Gobierno para radicar y para listar expedientes
/// (ADR 0103).</para>
/// </remarks>
public interface IGovActNotificationService
{
    /// <summary>
    /// Pone un acto en conocimiento del ciudadano dueño del expediente.
    /// </summary>
    /// <remarks>
    /// Idempotente por <paramref name="caseId"/> + <paramref name="title"/>: re-notificar el mismo
    /// acto devuelve la notificación que ya existe, sin abrir una segunda ni reiniciar su plazo.
    /// Un acto se notifica una vez.
    /// </remarks>
    Task<GovActNotification> NotifyAsync(
        string caseId,
        string radicado,
        Guid citizenMemberKey,
        string title,
        string body,
        string? documentRef = null,
        DateTimeOffset? acknowledgeBeforeUtc = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registra que <paramref name="memberKey"/> abrió la notificación: el término empieza acá.
    /// </summary>
    /// <returns>La notificación con su acceso registrado.</returns>
    /// <remarks>
    /// <b>Lanza si no puede certificar el acceso</b> —no existe, no es de quien la abre, o se pasó
    /// el plazo— en vez de devolver la notificación sin abrir. Contestar «acá la tienes» sin haber
    /// registrado nada dejaría al ciudadano leyendo un acto cuyo término nadie empezó a contar, y
    /// a la entidad sin poder sostener que se notificó.
    /// </remarks>
    Task<GovActNotification> AcknowledgeAsync(
        string notificationId,
        Guid memberKey,
        CancellationToken cancellationToken = default);

    /// <summary>Las notificaciones de un expediente, de la más reciente a la más vieja.</summary>
    Task<IReadOnlyList<GovActNotification>> GetForCaseAsync(
        string caseId, CancellationToken cancellationToken = default);

    /// <summary>Las de un ciudadano — la bandeja de «mis notificaciones».</summary>
    Task<IReadOnlyList<GovActNotification>> GetForCitizenAsync(
        Guid memberKey, CancellationToken cancellationToken = default);
}
