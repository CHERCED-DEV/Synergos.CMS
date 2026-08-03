using Synergos.Core;

namespace Synergos.Api.Moderation.Domain;

/// <summary>Lo que la moderación rechaza <b>sola</b>.</summary>
public static class ModerationRules
{
    public const string CodePrefix = "moderation";

    public const int MaxReasonLength = 500;

    /// <summary>Si el reporte que llega sirve.</summary>
    public static Rejection? CheckReport(string? reason)
        => !string.IsNullOrWhiteSpace(reason) && reason!.Length <= MaxReasonLength
            ? null
            : Rejection.Invalid($"{CodePrefix}.bad_reason",
                $"El motivo es obligatorio y no pasa de {MaxReasonLength} caracteres. Un reporte sin motivo no se puede revisar.");

    /// <summary>Si el caso admite una decisión.</summary>
    public static Rejection? CheckDecidable(ModerationCase caso, Ref? decidedBy, string? note)
    {
        if (caso.Status == CaseStatus.Decided)
        {
            // Conflict y visible: si redecidir fuera silencioso, dos moderadores podrían resolver
            // lo mismo al revés y el último ganaría sin que nadie lo notara.
            return Rejection.Conflict($"{CodePrefix}.already_decided",
                $"El caso ya se resolvió como {caso.Decision} en {caso.DecidedAtUtc:O}.");
        }
        if (decidedBy is null)
        {
            return Rejection.Invalid($"{CodePrefix}.moderator_required",
                "Hace falta decir quién decide: una moderación anónima es indistinguible de un borrado arbitrario.");
        }
        return string.IsNullOrWhiteSpace(note)
            ? Rejection.Invalid($"{CodePrefix}.note_required", "Hace falta decir por qué se decidió así.")
            : null;
    }
}
