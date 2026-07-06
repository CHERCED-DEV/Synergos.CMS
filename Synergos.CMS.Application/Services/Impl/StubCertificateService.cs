using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ICertificateService"/> — credencial verificable de
/// finalización del LMS (dominio Educación). Seam DEDICADO, separado del motor de
/// matrícula: compone el catálogo (<see cref="ICourseCatalogProvider"/>, total de
/// lecciones + título) y el progreso (<see cref="IEnrollmentService"/>) para
/// decidir si el alumno completó el curso al 100%, y deriva un id ESTABLE de
/// (curso, alumno). Reusa el patrón de credencial del motor (id determinista +
/// URL de verificación pública, como el e-ticket QR de Eventos).
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). <see cref="GetAsync"/> es idempotente: re-emitir
/// devuelve el mismo id (derivado por hash estable de (curso,alumno)) y registra el
/// certificado en un índice en memoria para que <see cref="VerifyAsync"/> lo
/// resuelva por id sin identificar al solicitante (verificación PÚBLICA). El
/// adapter real firma/registra la credencial (PKI/blockchain) sin tocar los
/// consumidores. ADR 0075 (seam con tests canónicos).
/// </remarks>
public sealed class StubCertificateService : ICertificateService
{
    private readonly ICourseCatalogProvider _catalog;
    private readonly IEnrollmentService _enrollments;
    private readonly Func<DateTimeOffset> _now;

    // Índice de certificados emitidos (id → cert + curso/alumno origen) para la
    // verificación pública por id. Poblado al emitir en GetAsync (al 100%).
    private readonly ConcurrentDictionary<string, IssuedCertificate> _issued = new(StringComparer.Ordinal);

    public StubCertificateService(ICourseCatalogProvider catalog, IEnrollmentService enrollments)
        : this(catalog, enrollments, null)
    {
    }

    /// <summary>Ctor con time source inyectable (<paramref name="now"/>) para determinismo en tests.</summary>
    public StubCertificateService(ICourseCatalogProvider catalog, IEnrollmentService enrollments, Func<DateTimeOffset>? now)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _enrollments = enrollments ?? throw new ArgumentNullException(nameof(enrollments));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<Certificate?> GetAsync(string student, string courseId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(student) || string.IsNullOrWhiteSpace(courseId))
        {
            return null;
        }

        var detail = await _catalog.GetCourseAsync(courseId, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        // El certificado SOLO se emite al 100% (progreso resuelto del motor).
        var progress = await _enrollments.GetProgressAsync(courseId, student, cancellationToken);
        if (progress.Percent < 100)
        {
            return null;
        }

        // Id estable derivado de (curso,alumno) → re-emitir es idempotente.
        var certId = "cert-" + StableHash($"{courseId.Trim()}|{student.Trim()}");
        var studentName = ResolveStudentName(student.Trim());
        var cert = new Certificate(
            Id: certId,
            CourseId: courseId.Trim(),
            StudentName: studentName,
            IssuedAt: _now(),
            VerifyUrl: $"/academy/verify/{certId}");

        // Registra (idempotente: mismo id → mismo registro) para la verificación pública.
        _issued.TryAdd(certId, new IssuedCertificate(cert, courseId.Trim(), student.Trim()));
        return cert;
    }

    public async Task<Certificate?> VerifyAsync(string certificateId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(certificateId) || !_issued.TryGetValue(certificateId.Trim(), out var issued))
        {
            return null;
        }

        // Re-verifica que la credencial siga siendo válida (el alumno completó el
        // curso). Si el catálogo o el progreso cambiaron y ya no está al 100%, no
        // valida — la credencial es un reflejo del estado actual, no un sello mudo.
        var progress = await _enrollments.GetProgressAsync(issued.CourseId, issued.Student, cancellationToken);
        return progress.Percent >= 100 ? issued.Certificate : null;
    }

    private string ResolveStudentName(string student)
    {
        // El motor conserva el nombre en la matrícula; si no hay o no es resoluble
        // desde aquí, se usa el propio identificador (email) como fallback estable.
        // (El nombre rico lo devuelve el certificado emitido por el propio motor.)
        return student;
    }

    // FNV-1a 32-bit (forzado positivo) → id de certificado estable y determinista
    // (mismo esquema que StubEnrollmentService para que ambos coincidan).
    private static string StableHash(string value)
    {
        const uint fnvOffset = 2166136261;
        const uint fnvPrime = 16777619;
        var hash = fnvOffset;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= fnvPrime;
        }
        return (hash & 0x7FFFFFFF).ToString("x8");
    }

    private sealed record IssuedCertificate(Certificate Certificate, string CourseId, string Student);
}
