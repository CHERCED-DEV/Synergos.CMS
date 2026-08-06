using Synergos.Core;

namespace Synergos.Api.Moderation.Domain;

/// <summary>En qué punto está un caso.</summary>
public enum CaseStatus
{
    /// <summary>En cola, esperando a alguien.</summary>
    Open,

    /// <summary>Alguien lo miró y decidió. Estado final.</summary>
    Decided,
}

/// <summary>Qué se resolvió.</summary>
public enum Decision
{
    /// <summary>Se deja como está.</summary>
    Keep,

    /// <summary>Se retira de la vista.</summary>
    Remove,
}

/// <summary>Un reporte contra algo, y su resolución.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Target">Sobre qué — opaco.</param>
/// <param name="Reporter">Quién reportó. Nulo si lo levantó el propio sistema.</param>
/// <param name="Reason">Por qué, en un vocabulario del dominio que reporta.</param>
/// <param name="Status">Abierto o decidido.</param>
/// <param name="Decision">Qué se resolvió, si se resolvió.</param>
/// <param name="DecidedBy">Quién lo decidió.</param>
/// <param name="DecisionNote">Por qué se decidió así.</param>
/// <param name="ReportedAtUtc">Cuándo entró.</param>
/// <param name="DecidedAtUtc">Cuándo se resolvió.</param>
/// <remarks>
/// <para><b>El caso NO oculta nada por su cuenta.</b> Registra la decisión; ejecutarla es del
/// BFF, que llamará a <c>Api.Engagement</c>, a <c>Api.Documents</c> o a donde viva el contenido.
/// Si Moderation ocultara directamente, tendría que conocer dónde vive cada cosa moderable — y
/// ahí dejaría de servir a la siguiente.</para>
///
/// <para><b>Quién decidió y por qué son obligatorios.</b> Una moderación anónima y sin motivo es
/// indistinguible de un borrado arbitrario, y es lo primero que se pide cuando alguien reclama.</para>
/// </remarks>
public sealed record ModerationCase(
    string Id,
    Ref Target,
    Ref? Reporter,
    string Reason,
    CaseStatus Status,
    Decision? Decision,
    Ref? DecidedBy,
    string? DecisionNote,
    DateTimeOffset ReportedAtUtc,
    DateTimeOffset? DecidedAtUtc = null);
