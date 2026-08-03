using Synergos.Core;

namespace Synergos.Api.Audit.Domain;

/// <summary>
/// Lo que la bitácora rechaza <b>sola</b>.
/// </summary>
/// <remarks>
/// Una bitácora parece un sumidero que acepta todo, y esa lectura es la que la vuelve inútil: una
/// entrada sin actor o sin acción ocupa espacio y no contesta ninguna pregunta el día que hay que
/// responder "¿quién hizo esto?". Rechazar en la escritura es lo único que mantiene la bitácora
/// consultable.
/// </remarks>
public static class AuditRules
{
    public const string CodePrefix = "audit";

    /// <summary>Largo máximo de una acción. Acotado porque va a un índice y a cada consulta.</summary>
    public const int MaxActionLength = 120;

    /// <summary>Tope de pares de detalle. Sin tope, una entrada puede traer un volcado entero.</summary>
    public const int MaxDetails = 24;

    /// <summary>Largo máximo de un valor de detalle.</summary>
    public const int MaxDetailLength = 512;

    /// <summary>Si la entrada que llega merece guardarse.</summary>
    public static Rejection? CheckWritable(Actor? actor, string? action, Ref? target,
        IReadOnlyDictionary<string, string>? details)
    {
        if (actor is null || actor.IsAnonymous)
        {
            // Una bitácora de acciones anónimas no responde la única pregunta que se le hace.
            // Si de verdad actuó un proceso sin persona detrás, el origen tiene que decir CUÁL
            // — un Ref("system","cron-retencion") es un actor perfectamente válido.
            return Rejection.Invalid($"{CodePrefix}.actor_required",
                "Una entrada sin actor no contesta '¿quién hizo esto?'. Un proceso también es un actor: dilo.");
        }

        if (string.IsNullOrWhiteSpace(action) || action!.Length > MaxActionLength)
        {
            return Rejection.Invalid($"{CodePrefix}.bad_action",
                $"La acción es obligatoria y no pasa de {MaxActionLength} caracteres.");
        }

        if (target is null)
        {
            return Rejection.Invalid($"{CodePrefix}.target_required", "Hace falta decir sobre qué se actuó.");
        }

        if (details is not null)
        {
            if (details.Count > MaxDetails)
            {
                return Rejection.Invalid($"{CodePrefix}.too_many_details",
                    $"Hasta {MaxDetails} pares de detalle. Una bitácora no es un volcado.");
            }
            var largo = details.FirstOrDefault(d => d.Value.Length > MaxDetailLength);
            if (largo.Key is not null)
            {
                return Rejection.Invalid($"{CodePrefix}.detail_too_long",
                    $"El detalle '{largo.Key}' pasa de {MaxDetailLength} caracteres.");
            }
        }

        return null;
    }

    /// <summary>Si una consulta trae filtro suficiente.</summary>
    /// <remarks>
    /// Sin filtro, una consulta es un volcado de la bitácora por HTTP — lento, caro, y la forma
    /// más cómoda de exfiltrar el rastro entero de una operación.
    /// </remarks>
    public static Rejection? CheckQuery(Ref? target, string? actorId)
        => target is not null || !string.IsNullOrWhiteSpace(actorId)
            ? null
            : Rejection.Invalid($"{CodePrefix}.filter_required",
                "Hace falta filtrar por target o por actor: sin filtro esto es un volcado de la bitácora.");
}
