using Synergos.Core;

namespace Synergos.Api.Workflow.Domain;

/// <summary>Lo que el proceso rechaza <b>solo</b>.</summary>
public static class WorkflowRules
{
    public const string CodePrefix = "workflow";

    /// <summary>Si la definición que llega es coherente.</summary>
    public static Rejection? CheckDefinition(string? key, string? initial, IReadOnlyList<string> finals, IReadOnlyList<TransitionRule> transitions)
    {
        if (string.IsNullOrWhiteSpace(key)) return Rejection.Invalid($"{CodePrefix}.key_required", "La definición necesita una clave estable.");
        if (string.IsNullOrWhiteSpace(initial)) return Rejection.Invalid($"{CodePrefix}.initial_required", "Hace falta el estado inicial.");
        if (transitions.Count == 0) return Rejection.Invalid($"{CodePrefix}.no_transitions", "Una máquina sin transiciones no avanza nunca.");

        var repetida = transitions
            .GroupBy(t => (t.Name.ToLowerInvariant(), t.From.ToLowerInvariant()))
            .FirstOrDefault(g => g.Count() > 1);
        if (repetida is not null)
        {
            // Dos transiciones con el mismo nombre desde el mismo estado harían que disparar la
            // acción llevara a un sitio u otro según el orden del fichero.
            return Rejection.Invalid($"{CodePrefix}.ambiguous_transition",
                $"La transición '{repetida.Key.Item1}' está definida dos veces desde '{repetida.Key.Item2}'.");
        }

        if (finals.Any(f => transitions.Any(t => string.Equals(t.From, f, StringComparison.OrdinalIgnoreCase))))
        {
            return Rejection.Invalid($"{CodePrefix}.transition_from_final",
                "Hay una transición que sale de un estado final. Un estado final es del que no se sale.");
        }

        // Si el inicial no aparece como origen de nada, ninguna instancia podría moverse — y el
        // error saldría al primer intento de avanzar, no al definir.
        return transitions.Any(t => string.Equals(t.From, initial, StringComparison.OrdinalIgnoreCase))
            ? null
            : Rejection.Invalid($"{CodePrefix}.initial_is_dead_end",
                $"Del estado inicial '{initial}' no sale ninguna transición.");
    }

    /// <summary>Busca la transición aplicable, o dice por qué no hay.</summary>
    public static Result<TransitionRule> Resolve(WorkflowDefinition def, WorkflowInstance instance, string? name, Actor actor)
    {
        if (def.IsFinal(instance.State))
        {
            return Rejection.Conflict($"{CodePrefix}.instance_closed",
                $"La instancia está en '{instance.State}', que es final.");
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            return Rejection.Invalid($"{CodePrefix}.transition_required", "Hace falta decir qué transición se dispara.");
        }

        var regla = def.Transitions.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.From, instance.State, StringComparison.OrdinalIgnoreCase));

        if (regla is null)
        {
            var posibles = def.Transitions
                .Where(t => string.Equals(t.From, instance.State, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Name);
            return Rejection.Conflict($"{CodePrefix}.transition_not_allowed",
                $"Desde '{instance.State}' no se puede '{name}'. Sí se puede: {string.Join(", ", posibles)}.");
        }

        // La guarda por rol es lo que hace que esta capacidad sirva a Gobierno: radicar lo hace
        // el ciudadano y aprobar el funcionario, y sin guarda serían la misma máquina sin
        // control de quién avanza qué.
        return regla.RequiredRoles.Count == 0 || actor.HasAnyRole(regla.RequiredRoles.ToArray())
            ? Result.Ok(regla)
            : Rejection.Forbidden($"{CodePrefix}.role_required",
                $"'{regla.Name}' exige alguno de estos roles: {string.Join(", ", regla.RequiredRoles)}.");
    }
}
