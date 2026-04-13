namespace Synergos.CMS.Domain.Compositions.Behavior;

/// <summary>
/// Script controlado desde CMS. Solo para scripts que son parte del contenido
/// editorial — nunca lógica de aplicación ni datos sensibles.
/// </summary>
public sealed record BehaviorScriptModel(
    string? ScriptType,
    string? ScriptContent);
