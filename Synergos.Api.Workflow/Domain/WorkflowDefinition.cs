using Synergos.Core;

namespace Synergos.Api.Workflow.Domain;

/// <summary>Una transición permitida entre dos estados.</summary>
/// <param name="Name">Cómo se llama la acción: <c>radicar</c>, <c>aprobar</c>.</param>
/// <param name="From">Desde qué estado.</param>
/// <param name="To">A qué estado.</param>
/// <param name="RequiredRoles">Quién puede dispararla. Vacío = cualquiera.</param>
public sealed record TransitionRule(string Name, string From, string To, IReadOnlyList<string> RequiredRoles);

/// <summary>
/// La máquina de estados de un proceso, definida como dato.
/// </summary>
/// <param name="Id">Identificador.</param>
/// <param name="Key">Nombre estable con el que un dominio la pide: <c>gov.tramite</c>.</param>
/// <param name="InitialState">Dónde arranca toda instancia.</param>
/// <param name="FinalStates">Estados de los que no se sale.</param>
/// <param name="Transitions">Lo que se puede hacer y desde dónde.</param>
/// <remarks>
/// <para><b>Los rótulos los pone el dominio; las transiciones válidas y la historia las guarda
/// esta capacidad</b> (doc 07 §2). Un trámite que avanza por estados, una ruta clínica, el
/// cumplimiento de un pedido y una matrícula son la misma máquina con distintos nombres — y esa
/// máquina estaba copiada cuatro veces.</para>
///
/// <para><b>Definida como DATO y no como código.</b> Si las transiciones fueran un <c>switch</c>
/// en C#, agregar un estado a un trámite gubernamental exigiría desplegar esta capacidad — y
/// entonces cada dominio esperaría turno para cambiar su propio proceso.</para>
/// </remarks>
public sealed record WorkflowDefinition(
    string Id,
    string Key,
    string InitialState,
    IReadOnlyList<string> FinalStates,
    IReadOnlyList<TransitionRule> Transitions)
{
    /// <summary>Todos los estados que la definición menciona.</summary>
    public IReadOnlySet<string> States => Transitions
        .SelectMany(t => new[] { t.From, t.To })
        .Append(InitialState)
        .Concat(FinalStates)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public bool IsFinal(string state) => FinalStates.Contains(state, StringComparer.OrdinalIgnoreCase);
}

/// <summary>Un paso dado, con quién y cuándo.</summary>
public sealed record HistoryEntry(string Transition, string From, string To, Ref By, string? Note, DateTimeOffset AtUtc);

/// <summary>Un proceso concreto en marcha.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="DefinitionKey">Qué máquina sigue.</param>
/// <param name="Subject">Sobre qué corre — opaco.</param>
/// <param name="State">Dónde está ahora.</param>
/// <param name="History">Cómo llegó hasta acá.</param>
/// <param name="StartedAtUtc">Cuándo arrancó.</param>
public sealed record WorkflowInstance(
    string Id, string DefinitionKey, Ref Subject, string State,
    IReadOnlyList<HistoryEntry> History, DateTimeOffset StartedAtUtc);
