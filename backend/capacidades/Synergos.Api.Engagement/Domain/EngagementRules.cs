using Synergos.Core;

namespace Synergos.Api.Engagement.Domain;

/// <summary>Lo que la opinión rechaza <b>sola</b>.</summary>
public static class EngagementRules
{
    public const string CodePrefix = "engagement";

    public const int MinRating = 1;
    public const int MaxRating = 5;
    public const int MaxBodyLength = 4_000;

    /// <summary>Si la opinión que llega tiene lo que su clase exige.</summary>
    public static Rejection? CheckShape(EngagementKind kind, string? body, int? rating, string? reactionCode) => kind switch
    {
        EngagementKind.Comment when string.IsNullOrWhiteSpace(body)
            => Rejection.Invalid($"{CodePrefix}.body_required", "Un comentario sin texto no dice nada."),
        EngagementKind.Comment when body!.Length > MaxBodyLength
            => Rejection.Invalid($"{CodePrefix}.body_too_long", $"El comentario pasa de {MaxBodyLength} caracteres."),

        EngagementKind.Rating when rating is null
            => Rejection.Invalid($"{CodePrefix}.rating_required", "Una valoración necesita puntaje."),
        EngagementKind.Rating when rating < MinRating || rating > MaxRating
            => Rejection.Invalid($"{CodePrefix}.rating_out_of_range",
                $"El puntaje va entre {MinRating} y {MaxRating}, y llegó {rating}."),

        EngagementKind.Reaction when string.IsNullOrWhiteSpace(reactionCode)
            => Rejection.Invalid($"{CodePrefix}.reaction_code_required", "Una reacción necesita decir cuál."),

        _ => null,
    };

    /// <summary>
    /// Si esta clase admite repetirse del mismo actor sobre el mismo objetivo.
    /// </summary>
    /// <remarks>
    /// <b>Comentar varias veces es normal; valorar dos veces no.</b> Una persona puede comentar
    /// un hilo diez veces, pero si pudiera valorar diez veces el promedio dejaría de significar
    /// nada — y esa es precisamente la métrica que la gente mira para decidir.
    /// </remarks>
    public static bool AdmiteRepetir(EngagementKind kind) => kind == EngagementKind.Comment;

    /// <summary>Si la repetición choca con algo que ya está.</summary>
    public static Rejection? CheckDuplicate(EngagementKind kind, Engagement? existente)
        => existente is null || AdmiteRepetir(kind)
            ? null
            : Rejection.Conflict($"{CodePrefix}.already_{kind.ToString().ToLowerInvariant()}",
                $"Ese actor ya registró un {kind} sobre ese objetivo. Para cambiarlo hay que retirarlo primero.");
}
