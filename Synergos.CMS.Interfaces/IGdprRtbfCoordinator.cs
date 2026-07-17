namespace Synergos.CMS.Interfaces;

/// <summary>
/// Orchestra el flujo "Right to be Forgotten" (GDPR Art. 17) para un
/// Member: hard-deletes el record, anonimiza comments y form
/// submissions linkeadas, audita la operación y devuelve un resumen
/// agregado para reportarle al requester. Olas 233-235.
/// </summary>
/// <remarks>
/// Reemplaza el procedimiento manual documentado en
/// <c>docs/hardening/gdpr-rtbf.md</c>. Cada surface se anonimiza con
/// "[deleted]" placeholders cuando el record debe persistir por
/// integridad referencial; el Member record sí se hard-deletes.
///
/// Audit events que mencionen al Member NO se borran: GDPR Art. 17(3)
/// exime el processing requerido para "compliance with a legal
/// obligation". El coordinator audita una entrada
/// <c>gdpr.rtbf-processed</c> con counts agregados.
///
/// La impl por defecto (<c>FileSystemGdprRtbfCoordinator</c>) opera
/// sobre los stores filesystem ya existentes
/// (<c>App_Data/syn-comments/</c>, <c>App_Data/syn-forms/</c>). Para
/// stores alternos (DB, externo), implementar contra esos backends.
/// </remarks>
public interface IGdprRtbfCoordinator
{
    /// <summary>
    /// Procesa una solicitud RTBF para el <paramref name="memberKey"/>.
    /// El método es <b>irreversible</b> — el caller debe haber confirmado
    /// con el operator antes de invocar (mismo pattern del Members.delete
    /// dialog).
    /// </summary>
    /// <param name="memberKey">Key del Member a borrar.</param>
    /// <param name="actorEmail">Email del admin que ejecuta la
    ///   operación. Se persiste en el audit event.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>Resumen agregado de lo procesado.</returns>
    Task<RtbfResult> ProcessRequestAsync(
        Guid memberKey,
        string actorEmail,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resumen del procesamiento RTBF — útil para enviar email de
/// confirmación al requester con counts concretos.
/// </summary>
/// <param name="MemberDeleted">true si el record del Member fue
///   borrado. false si el Member no existía (idempotent).</param>
/// <param name="OriginalEmail">Email del Member antes del delete —
///   capturado para reportar al requester. Vacío si MemberDeleted=false.</param>
/// <param name="CommentsAnonymized">Cantidad de comments cuyo
///   <c>AuthorName</c> + <c>MemberKey</c> fueron reemplazados por
///   placeholder <c>[deleted]</c>.</param>
/// <param name="FormSubmissionsAnonymized">Cantidad de form
///   submissions cuyo field <c>email</c> (o equivalente) matched el
///   email del Member y fue reemplazado por <c>[deleted]@gdpr.local</c>.</param>
/// <param name="AuditPreserved">Siempre true — los eventos de audit
///   que mencionen al Member se preservan por GDPR Art. 17(3). Campo
///   incluido para hacer explícito el compliance choice en el
///   reporting.</param>
/// <param name="FailureReason">Slug machine-readable del error si
///   <c>MemberDeleted</c> falló — ej. <c>"member-not-found"</c>,
///   <c>"delete-failed"</c>. Null en happy path.</param>
public sealed record RtbfResult(
    bool MemberDeleted,
    string OriginalEmail,
    int CommentsAnonymized,
    int FormSubmissionsAnonymized,
    bool AuditPreserved,
    string? FailureReason);
