using Synergos.Core;

namespace Synergos.Api.Engagement.Domain;

/// <summary>Qué clase de opinión es.</summary>
/// <remarks>
/// <b>Las cuatro son la misma capacidad.</b> Los comentarios de Social, las reseñas de Shop, las
/// valoraciones de Academy y los favoritos de Realty estaban escritos cuatro veces con cuatro
/// nombres, y todos son lo mismo: <i>un actor opina sobre un Ref</i> (doc 08 §2). Lo que cambia
/// entre ellos es qué campos exige, y eso son cuatro líneas de regla.
/// </remarks>
public enum EngagementKind
{
    /// <summary>Texto libre. Exige cuerpo.</summary>
    Comment,

    /// <summary>Una reacción con nombre —<c>like</c>, <c>aplauso</c>—. Una por actor y objetivo.</summary>
    Reaction,

    /// <summary>Estrellas. Exige puntaje.</summary>
    Rating,

    /// <summary>Marcar para después. Sin cuerpo ni puntaje.</summary>
    Favorite,
}

/// <summary>Una opinión de alguien sobre algo.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Actor">Quién opina — opaco.</param>
/// <param name="Target">Sobre qué — opaco.</param>
/// <param name="Kind">De qué clase.</param>
/// <param name="Body">El texto, si es comentario.</param>
/// <param name="Rating">Las estrellas, si es valoración.</param>
/// <param name="ReactionCode">Qué reacción, si es reacción.</param>
/// <param name="ParentId">A qué comentario responde, si responde.</param>
/// <param name="Visible">Si se muestra. Lo baja la moderación, que es otra capacidad.</param>
/// <param name="AtUtc">Cuándo.</param>
public sealed record Engagement(
    string Id,
    Ref Actor,
    Ref Target,
    EngagementKind Kind,
    string? Body,
    int? Rating,
    string? ReactionCode,
    string? ParentId,
    bool Visible,
    DateTimeOffset AtUtc);
