namespace Synergos.CMS.Interfaces;

/// <summary>
/// Emisión + verificación del certificado de finalización de un curso (LMS —
/// dominio Educación, app-spec §2/§3). Seam DEDICADO (separado del motor de
/// matrícula) para la credencial verificable: el alumno la obtiene SOLO al 100%
/// y cualquiera la verifica públicamente por su id estable. Reusa el patrón de
/// credencial (id determinista + URL de verificación pública, como el e-ticket
/// QR de Eventos o el radicado de Gobierno).
/// </summary>
/// <remarks>
/// <para>Seam stub-first (ADR 0075): el default <c>StubCertificateService</c>
/// (Application, lógica pura — ADR 0002) compone el catálogo
/// (<see cref="ICourseCatalogProvider"/>) y el motor
/// (<see cref="IEnrollmentService"/>) para decidir si el alumno completó el curso,
/// y deriva un id ESTABLE de (curso, alumno) — re-emitir devuelve el mismo
/// certificado (idempotente). La verificación es PÚBLICA: no requiere identificar al
/// solicitante, solo el id del certificado.</para>
///
/// <para><b>El id ya está firmado (ADR 0124).</b> Lo deriva
/// <see cref="ICertificateIdSigner"/> con una llave del servidor: sin ella no se puede
/// calcular el id de nadie. Antes era un FNV-1a de 31 bits sin secreto, y este XML-doc
/// ya prometía "verificación pública" que ningún controller servía — abrirla habría
/// convertido el certificado en un padrón enumerable de quién estudió qué. Hoy la sirve
/// <c>GET /academy/verify/{certificateId}</c>, anónimo y con respuesta uniforme para
/// todo lo que no sea una credencial válida.</para>
///
/// <para>Los certificados emitidos son durables (<see cref="IJsonEntityStore"/>,
/// ADR 0105, familia <c>certificates</c>): un QR impreso sigue verificando tras un
/// reinicio. El adapter real firma/registra la credencial (blockchain / PKI) sin tocar
/// los consumidores.</para>
/// </remarks>
public interface ICertificateService
{
    /// <summary>
    /// Devuelve el certificado del curso para el alumno cuando su progreso = 100%,
    /// o <c>null</c> si aún no lo completó (o el curso no existe). Idempotente:
    /// re-emitir devuelve el mismo certificado (id estable, verify público).
    /// </summary>
    Task<Certificate?> GetAsync(string student, string courseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifica públicamente un certificado por su id — devuelve la credencial si
    /// existe y sigue siendo válida (el alumno completó el curso), o <c>null</c> si
    /// el id no corresponde a ningún certificado emitido. No requiere identificar al
    /// solicitante: es la cara pública de la credencial (QR → esta verificación).
    /// </summary>
    /// <remarks>
    /// <b>Un solo <c>null</c> para todo.</b> Id malformado, id desconocido, registro
    /// fabricado en el almacén cuyo id no cuadra con la llave, o alumno que ya no está
    /// al 100%: todos devuelven <c>null</c>, sin distinguirlos. Quien pregunta no puede
    /// aprender nada de la diferencia — ni siquiera si el curso o el alumno existen.
    /// Devolver la credencial es la ÚNICA respuesta que afirma algo.
    /// </remarks>
    Task<Certificate?> VerifyAsync(string certificateId, CancellationToken cancellationToken = default);
}
