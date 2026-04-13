namespace Synergos.CMS.Domain.Compositions.Behavior;

/// <summary>
/// Navegación declarativa: destino y tipo de navegación.
/// internal → router SPA, external → nueva pestaña, anchor → scroll a ID.
/// </summary>
public sealed record BehaviorNavigationModel(
    string? NavigateTo,
    string? NavigationType);
