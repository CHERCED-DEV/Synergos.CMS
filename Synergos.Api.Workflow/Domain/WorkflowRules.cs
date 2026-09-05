using Synergos.Core;
using Synergos.Shared;

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
    public static Result<TransitionRule> Resolve(
        WorkflowDefinition def, WorkflowInstance instance, string? name, Actor actor,
        bool verified = false, bool requireVerified = false)
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

        if (regla.RequiredRoles.Count == 0) return Result.Ok(regla);

        // LA GUARDA VALE LO QUE VALGA SU PRUEBA (defecto #48).
        //
        // Este comentario decía que la guarda «hace que esta capacidad sirva a Gobierno: radicar
        // lo hace el ciudadano y aprobar el funcionario». Era falso: los roles llegaban en el
        // CUERPO de la petición, así que cualquiera con la llave compartida se ascendía a
        // funcionario escribiendo una línea de JSON. La regla estaba bien; la fuente del dato
        // estaba mal — la misma forma del defecto #42.
        //
        // Hoy: si el rol viene de un token verificado, la guarda guarda. Si viene declarado, es
        // una guarda contra el ACCIDENTE, no contra alguien que quiera saltársela — y un
        // despliegue que ya tenga identidad lo puede exigir con RequireVerifiedRoles.
        if (requireVerified && !verified)
        {
            return Rejection.Forbidden($"{CodePrefix}.roles_not_verified",
                $"'{regla.Name}' exige rol y este despliegue sólo acepta roles de un token de identidad verificado.");
        }

        return actor.HasAnyRole(regla.RequiredRoles.ToArray())
            ? Result.Ok(regla)
            : Rejection.Forbidden($"{CodePrefix}.role_required",
                $"'{regla.Name}' exige alguno de estos roles: {string.Join(", ", regla.RequiredRoles)}.");
    }

    /// <summary>
    /// De dónde salen los roles de quien dispara: del token si lo hay, del cuerpo si no.
    /// </summary>
    /// <remarks>
    /// <para><b>El token GANA sobre lo declarado</b> (defecto #48). Con los dos presentes, creerle
    /// al cuerpo dejaría que un llamador se ascendiera presentando un token honesto y pidiendo
    /// además el rol que le falta.</para>
    ///
    /// <para><b>Y el sujeto del token tiene que ser quien actúa.</b> Sin esa comprobación, el
    /// token de una persona serviría para actuar como otra y sería decoración — es la lección de
    /// la HU #14 rebanada 3, aplicada acá.</para>
    ///
    /// <para>Presentar un token donde nadie puede comprobarlo se <b>rechaza</b>, no se ignora:
    /// ignorarlo dejaría a quien lo manda creyendo que probó algo.</para>
    /// </remarks>
    /// <para><b>La comprobación en sí ya no vive acá</b> (#72). Nació en esta capacidad con el
    /// defecto #48 y volvió a hacer falta, igual, en <c>Api.Audit</c>: es el SEGUNDO consumidor,
    /// que es cuando <c>CLAUDE.md</c> §0.B.17 dice que algo sube a una capa compartida. Con una
    /// tercera copia, la comprobación del token derivaría entre capacidades y el mismo token
    /// valdría distinto según a quién se le presente.</para>
    ///
    /// <para>Lo que se queda es la <b>firma</b>: <c>Verified</c> en vez de la afirmación, porque
    /// esta capacidad decide con ella (<c>RequireVerifiedRoles</c>) y no la anota.</para>
    public static Result<(Actor Actor, bool Verified)> ResolveActor(
        IdentityTokenGate gate, string? rawToken, Ref principal,
        IReadOnlyList<string>? declaredRoles, DateTimeOffset now)
    {
        // Esta capacidad no tiene campo `assertion` en su contrato: sin token, lo que hay es la
        // palabra de quien llama, que es exactamente lo que CmsSession significa. Declararlo acá
        // mantiene el comportamiento de siempre —sin token se sigue adelante— sin repetir la
        // regla de qué se acepta sin prueba.
        var resuelto = IdentityAssertions.ResolveActor(
            gate, rawToken, principal, declaredRoles, IdentityAssertion.CmsSession, now, CodePrefix);

        return resuelto.IsOk
            ? Result.Ok((resuelto.Value.Actor, resuelto.Value.Assertion == IdentityAssertion.IdentityToken))
            : Result.Rejected<(Actor, bool)>(resuelto.Rejection!);
    }
}
