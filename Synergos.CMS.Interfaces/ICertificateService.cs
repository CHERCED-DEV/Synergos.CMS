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
/// Seam stub-first (ADR 0075): el default <c>StubCertificateService</c>
/// (Application, lógica pura — ADR 0002) compone el catálogo
/// (<see cref="ICourseCatalogProvider"/>, total de lecciones) y el progreso
/// (<see cref="IEnrollmentService"/>) para decidir si el alumno completó el curso,
/// y deriva un id ESTABLE de (curso, alumno) — re-emitir devuelve el mismo
/// certificado (idempotente). El adapter real firma/registra la credencial
/// (blockchain / PKI) sin tocar los consumidores. La verificación es PÚBLICA: no
/// requiere identificar al solicitante, solo el id del certificado.
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
    Task<Certificate?> VerifyAsync(string certificateId, CancellationToken cancellationToken = default);
}
