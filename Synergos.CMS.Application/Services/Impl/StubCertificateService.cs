using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ICertificateService"/> — credencial verificable de finalización
/// del LMS (dominio Educación). Seam DEDICADO, separado del motor de matrícula:
/// compone el catálogo (<see cref="ICourseCatalogProvider"/>) y el motor
/// (<see cref="IEnrollmentService"/>) para decidir si el alumno completó el curso al
/// 100% y con qué nombre, y deriva el id con <see cref="ICertificateIdSigner"/>.
/// </summary>
/// <remarks>
/// <para>Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002).</para>
///
/// <para><b>Qué cambió y por qué (ADR 0124).</b> El id era
/// <c>"cert-" + FNV-1a-32(curso|alumno)</c>: sin secreto y de 31 bits, o sea calculable
/// por cualquiera que supiera un id de curso (público) y adivinara un correo. El índice
/// de emitidos vivía además en un <c>ConcurrentDictionary</c> del proceso, así que un
/// reinicio dejaba sin verificar un diploma ya impreso. Ahora: el id lo firma
/// <see cref="ICertificateIdSigner"/> (HMAC con llave del servidor) y el índice vive tras
/// <see cref="IJsonEntityStore"/> (ADR 0105, familia <c>certificates</c>).</para>
///
/// <para><b>El índice NO es la autoridad; la llave sí.</b> <see cref="VerifyAsync"/> lee
/// el registro guardado y RECALCULA el id desde el sujeto que ese registro afirma. Quien
/// consiga escribir en el almacén puede inventar un fichero con el id que quiera y el
/// nombre de quien quiera; no le sirve de nada, porque el id no cuadrará.</para>
///
/// <para><b>El firmante es obligatorio.</b> No hay ctor que construya este servicio sin
/// él: un certificado con id derivable no es una credencial, y dejar esa puerta abierta
/// "para tests" es exactamente cómo se cuela a producción.</para>
///
/// <para><b>Nota sobre <see cref="IJsonEntityStore.ListAsync"/>:</b> aquí no se usa, y es
/// deliberado — devuelve los DOCUMENTOS, no las claves, y pasarle uno a
/// <see cref="IJsonEntityStore.ReadAsync"/> devuelve <c>null</c> para siempre en silencio.
/// La verificación va por clave directa (el id ES la clave), que además es lo correcto:
/// no hay ninguna operación de este seam que necesite recorrer el padrón de emitidos.</para>
///
/// <para>ADR 0075 (seam con tests canónicos).</para>
/// </remarks>
public sealed class StubCertificateService : ICertificateService
{
    /// <summary>Familia de entidades en el store genérico (→ App_Data/syn-certificates/).</summary>
    private const string ResourceType = "certificates";

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ICourseCatalogProvider _catalog;
    private readonly IEnrollmentService _enrollments;
    private readonly ICertificateIdSigner _signer;
    private readonly IJsonEntityStore _store;
    private readonly Func<DateTimeOffset> _now;

    // Serializa el read-modify-write de la emisión. lock{} no sirve: el cuerpo hace await.
    private readonly SemaphoreSlim _mutate = new(1, 1);

    public StubCertificateService(
        ICourseCatalogProvider catalog,
        IEnrollmentService enrollments,
        ICertificateIdSigner signer)
        : this(catalog, enrollments, signer, null, null)
    {
    }

    /// <summary>
    /// Ctor completo: <paramref name="now"/> es el time source inyectable (determinismo en
    /// tests) y <paramref name="store"/> el backing store durable — null cae a
    /// <see cref="InMemoryJsonEntityStore"/>, que es lo que quiere un test unitario y NO lo
    /// que quiere un diploma.
    /// </summary>
    public StubCertificateService(
        ICourseCatalogProvider catalog,
        IEnrollmentService enrollments,
        ICertificateIdSigner signer,
        Func<DateTimeOffset>? now,
        IJsonEntityStore? store)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _enrollments = enrollments ?? throw new ArgumentNullException(nameof(enrollments));
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _store = store ?? new InMemoryJsonEntityStore();
    }

    public async Task<Certificate?> GetAsync(string student, string courseId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(student) || string.IsNullOrWhiteSpace(courseId))
        {
            return null;
        }

        var course = courseId.Trim();
        var who = student.Trim();

        var detail = await _catalog.GetCourseAsync(course, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        // El certificado SOLO se emite al 100%. Se le pide al MOTOR, que es quien sabe si
        // el alumno terminó Y con qué nombre está matriculado. De lo que devuelve se usan
        // esas dos cosas; su id —que sigue derivándose sin firma ahí dentro— se DESCARTA:
        // desde ADR 0124 el id de una credencial tiene exactamente una fuente, el firmante.
        var completed = await _enrollments.GetCertificateAsync(course, who, cancellationToken);
        if (completed is null)
        {
            return null;
        }

        var subject = new CertificateSubject(course, who);
        var certId = _signer.Sign(subject);

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Idempotente de verdad: si ya se emitió, se devuelve el registro TAL CUAL —
            // incluida su fecha de emisión. Re-emitir no puede mover la fecha impresa en
            // un diploma que ya está en la pared de alguien.
            var existing = await LoadAsync(certId, cancellationToken).ConfigureAwait(false);
            if (existing is not null && _signer.Matches(certId, new CertificateSubject(existing.CourseId, existing.Student)))
            {
                return ToCertificate(existing);
            }

            var issued = new PersistedCertificate(
                Id: certId,
                CourseId: course,
                Student: who,
                StudentName: completed.StudentName,
                IssuedAt: _now());
            await _store.WriteAsync(ResourceType, certId, JsonSerializer.Serialize(issued, _json), cancellationToken)
                .ConfigureAwait(false);
            return ToCertificate(issued);
        }
        finally
        {
            _mutate.Release();
        }
    }

    public async Task<Certificate?> VerifyAsync(string certificateId, CancellationToken cancellationToken = default)
    {
        // Un id con otra forma no llega ni al almacén: además de ahorrar I/O, evita que un
        // id inventado se convierta en una clave de fichero arbitraria.
        if (!LooksLikeCertificateId(certificateId))
        {
            return null;
        }

        var id = certificateId.Trim();
        var issued = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
        if (issued is null)
        {
            return null;
        }

        // El registro guardado NO es la autoridad: se recalcula el id desde el sujeto que
        // afirma. Un registro fabricado (id elegido + nombre ajeno) no sobrevive a esto.
        if (!_signer.Matches(id, new CertificateSubject(issued.CourseId, issued.Student)))
        {
            return null;
        }

        // Y la credencial sigue siendo un reflejo del estado actual, no un sello mudo: si
        // el curso o el progreso cambiaron y ya no está al 100%, no valida.
        var progress = await _enrollments.GetProgressAsync(issued.CourseId, issued.Student, cancellationToken);
        return progress.Percent >= 100 ? ToCertificate(issued) : null;
    }

    private async Task<PersistedCertificate?> LoadAsync(string certificateId, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(ResourceType, certificateId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var stored = JsonSerializer.Deserialize<PersistedCertificate>(json, _json);
            // Un registro sin sujeto no se puede re-verificar contra la llave → se ignora.
            return stored is null
                   || string.IsNullOrWhiteSpace(stored.CourseId)
                   || string.IsNullOrWhiteSpace(stored.Student)
                ? null
                : stored;
        }
        catch (JsonException)
        {
            // Un certificado corrupto no puede tumbar la verificación de los demás.
            return null;
        }
    }

    private static Certificate ToCertificate(PersistedCertificate stored) => new(
        Id: stored.Id,
        CourseId: stored.CourseId,
        StudentName: stored.StudentName,
        IssuedAt: stored.IssuedAt,
        VerifyUrl: $"/academy/verify/{stored.Id}");

    /// <summary>
    /// <c>cert-</c> + 32 hex minúscula, que es lo que produce el firmante. No es una
    /// comprobación de seguridad (no valida la firma), es un filtro de forma.
    /// </summary>
    private static bool LooksLikeCertificateId(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }
        var id = candidate.Trim();
        const string prefix = "cert-";
        if (id.Length != prefix.Length + 32 || !id.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }
        foreach (var c in id.AsSpan(prefix.Length))
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// La forma SERIALIZADA de un certificado emitido. Top-level (no anidado) para round-trip
/// limpio con System.Text.Json — los records posicionales deserializan por ctor.
/// </summary>
/// <remarks>
/// Guarda el SUJETO (<see cref="CourseId"/> + <see cref="Student"/>) además del id, porque
/// la verificación recalcula el id desde el sujeto: sin él, el fichero sería un sello que
/// se cree a sí mismo. <see cref="Student"/> es el identificador de la cuenta y NUNCA sale
/// en la respuesta pública — quien la sirve decide qué se publica.
/// </remarks>
public sealed record PersistedCertificate(
    string Id,
    string CourseId,
    string Student,
    string StudentName,
    DateTimeOffset IssuedAt);
