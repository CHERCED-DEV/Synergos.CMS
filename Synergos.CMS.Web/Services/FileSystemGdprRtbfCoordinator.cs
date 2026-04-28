using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Implementación filesystem-backed del flujo GDPR RTBF (Olas 233-235).
/// Coordina <see cref="IMemberRosterWriter"/> + anonimización inline
/// de los stores filesystem ya existentes
/// (<c>App_Data/syn-comments/</c>, <c>App_Data/syn-form-submissions/</c>)
/// + un audit event terminal.
/// </summary>
/// <remarks>
/// La operación es <b>irreversible</b>. El caller (admin endpoint) debe
/// confirmar con el operador antes de invocar — mismo pattern que el
/// dialog de Members.delete.
///
/// Anonimización (no hard-delete) en comments + forms para preservar
/// la integridad del thread / audit trail de la submission. Hard-delete
/// solo del Member record.
///
/// Para stores alternos (DB, externo), implementar contra esos
/// backends. La interfaz <see cref="IGdprRtbfCoordinator"/> es la única
/// dependencia del admin endpoint.
/// </remarks>
public sealed class FileSystemGdprRtbfCoordinator : IGdprRtbfCoordinator
{
    private const string DeletedAuthorPlaceholder = "[deleted]";
    private const string DeletedEmailPlaceholder = "[deleted]@gdpr.local";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder
            .UnsafeRelaxedJsonEscaping,
    };

    private readonly IMemberService _memberService;
    private readonly IMemberRosterWriter _rosterWriter;
    private readonly IAuditTrailWriter _audit;
    private readonly IHostEnvironment _environment;
    private readonly IOptions<CommentsSettings> _commentsOptions;
    private readonly IOptions<FormsSettings> _formsOptions;
    private readonly ILogger<FileSystemGdprRtbfCoordinator> _logger;

    public FileSystemGdprRtbfCoordinator(
        IMemberService memberService,
        IMemberRosterWriter rosterWriter,
        IAuditTrailWriter audit,
        IHostEnvironment environment,
        IOptions<CommentsSettings> commentsOptions,
        IOptions<FormsSettings> formsOptions,
        ILogger<FileSystemGdprRtbfCoordinator> logger)
    {
        _memberService = memberService;
        _rosterWriter = rosterWriter;
        _audit = audit;
        _environment = environment;
        _commentsOptions = commentsOptions;
        _formsOptions = formsOptions;
        _logger = logger;
    }

    public async Task<RtbfResult> ProcessRequestAsync(
        Guid memberKey,
        string actorEmail,
        CancellationToken cancellationToken)
    {
        var member = _memberService.GetByKey(memberKey);
        if (member is null)
        {
            await AuditAsync(memberKey, actorEmail, originalEmail: "",
                comments: 0, forms: 0, outcome: "failure",
                detail: "member-not-found", cancellationToken);
            return new RtbfResult(
                MemberDeleted: false,
                OriginalEmail: string.Empty,
                CommentsAnonymized: 0,
                FormSubmissionsAnonymized: 0,
                AuditPreserved: true,
                FailureReason: "member-not-found");
        }

        var originalEmail = member.Email ?? string.Empty;
        var memberKeyString = memberKey.ToString();

        // Anonimizar primero — si el delete fall a, los stores ya quedan
        // limpios (la lógica de anonymize es idempotent y el operator
        // puede reintentar el delete por separado).
        var commentsCount = await AnonymizeCommentsAsync(memberKeyString, cancellationToken);
        var formsCount = await AnonymizeFormSubmissionsAsync(originalEmail, cancellationToken);

        var deleted = await _rosterWriter.DeleteAsync(memberKey, cancellationToken);
        if (!deleted)
        {
            // El reader devolvió no-null pero el writer falló — race con
            // un delete concurrente en backoffice. Caller decide reintentar.
            await AuditAsync(memberKey, actorEmail, originalEmail,
                commentsCount, formsCount, outcome: "partial",
                detail: $"delete-failed,comments={commentsCount},forms={formsCount}",
                cancellationToken);
            return new RtbfResult(
                MemberDeleted: false,
                OriginalEmail: originalEmail,
                CommentsAnonymized: commentsCount,
                FormSubmissionsAnonymized: formsCount,
                AuditPreserved: true,
                FailureReason: "delete-failed");
        }

        await AuditAsync(memberKey, actorEmail, originalEmail,
            commentsCount, formsCount, outcome: "success",
            detail: $"originalEmail={originalEmail},comments={commentsCount},forms={formsCount}",
            cancellationToken);

        _logger.LogWarning(
            "GDPR RTBF processed: memberKey={Key} originalEmail={Email} comments={Comments} forms={Forms} actor={Actor}",
            memberKey, originalEmail, commentsCount, formsCount, actorEmail);

        return new RtbfResult(
            MemberDeleted: true,
            OriginalEmail: originalEmail,
            CommentsAnonymized: commentsCount,
            FormSubmissionsAnonymized: formsCount,
            AuditPreserved: true,
            FailureReason: null);
    }

    private async Task<int> AnonymizeCommentsAsync(string memberKeyString, CancellationToken cancellationToken)
    {
        var root = Path.Combine(_environment.ContentRootPath, _commentsOptions.Value.StorageRoot);
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var changed = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<Comment>? comments;
            try
            {
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                comments = JsonSerializer.Deserialize<List<Comment>>(bytes, SerializerOptions);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                _logger.LogWarning(ex, "Skipping unreadable comments file during RTBF: {Path}", path);
                continue;
            }
            if (comments is null || comments.Count == 0)
            {
                continue;
            }

            var dirty = false;
            for (var i = 0; i < comments.Count; i++)
            {
                var c = comments[i];
                if (string.Equals(c.MemberKey, memberKeyString, StringComparison.OrdinalIgnoreCase))
                {
                    comments[i] = c with
                    {
                        MemberKey = null,
                        AuthorName = DeletedAuthorPlaceholder,
                    };
                    dirty = true;
                    changed++;
                }
            }
            if (dirty)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(comments, SerializerOptions);
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            }
        }
        return changed;
    }

    private async Task<int> AnonymizeFormSubmissionsAsync(string originalEmail, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(originalEmail))
        {
            return 0;
        }

        var root = Path.Combine(_environment.ContentRootPath, _formsOptions.Value.StorageRoot);
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var changed = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            JsonObject? root2;
            try
            {
                var text = await File.ReadAllTextAsync(path, cancellationToken);
                root2 = JsonNode.Parse(text)?.AsObject();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                _logger.LogWarning(ex, "Skipping unreadable form submission file during RTBF: {Path}", path);
                continue;
            }
            if (root2 is null || root2["fields"] is not JsonObject fields)
            {
                continue;
            }

            var dirty = false;
            foreach (var field in fields.ToList())
            {
                if (field.Value is JsonValue value &&
                    value.TryGetValue<string>(out var asString) &&
                    string.Equals(asString, originalEmail, StringComparison.OrdinalIgnoreCase))
                {
                    fields[field.Key] = DeletedEmailPlaceholder;
                    dirty = true;
                }
            }
            if (dirty)
            {
                var json = root2.ToJsonString(SerializerOptions);
                await File.WriteAllTextAsync(path, json, cancellationToken);
                changed++;
            }
        }
        return changed;
    }

    private Task AuditAsync(
        Guid memberKey,
        string actorEmail,
        string originalEmail,
        int comments,
        int forms,
        string outcome,
        string detail,
        CancellationToken cancellationToken)
    {
        var evt = new AuditEvent(
            Id: Guid.NewGuid().ToString("N"),
            OccurredAtUtc: DateTime.UtcNow,
            ActorEmail: actorEmail,
            ActorName: actorEmail,
            Action: "gdpr.rtbf-processed",
            Resource: $"memberKey={memberKey}",
            Outcome: outcome,
            Detail: detail);
        return _audit.WriteAsync(evt, cancellationToken);
    }
}
