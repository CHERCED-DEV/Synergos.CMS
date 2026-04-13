namespace Synergos.CMS.Domain.Compositions.Behavior;

/// <summary>
/// Contrato de comunicación con API externa: endpoint y método HTTP.
/// El renderer ejecuta la llamada en el momento apropiado del ciclo de vida.
/// </summary>
public sealed record BehaviorAsyncModel(
    string? ApiEndpoint,
    string? Method);
