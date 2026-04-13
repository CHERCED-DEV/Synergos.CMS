using Synergos.CMS.Domain.Compositions.Behavior;
using Synergos.CMS.Domain.Compositions.Dom;

namespace Synergos.CMS.Application.Components;

/// <summary>
/// Base de todos los Component ViewModels.
///
/// Expone las composiciones DOM y Behavior comunes a cualquier componente.
/// Las subclases declaran las composiciones Content específicas de su tipo.
///
/// Contrato:
///   ViewName  → ruta del partial Razor (ej: "Components/Hero")
///   BlockClass → clase BEM del bloque contenedor (ej: "sg-component--hero")
///
/// Ninguna propiedad lanza excepción — todas son nullable.
/// La vista debe manejar cada propiedad con null checks o @?.
/// </summary>
public abstract class BaseComponentViewModel
{
    public abstract string ViewName   { get; }
    public abstract string BlockClass { get; }

    /// <summary>
    /// Alias del Element Type de Umbraco que generó este ViewModel.
    /// Ejemplo: "elementTextHeading", "elementCompHero".
    /// Usado por PageConfigAssembler para el contrato JSON → Angular.
    /// </summary>
    public abstract string Alias { get; }

    // ── DOM ───────────────────────────────────────────────────────────────
    public DomClassModel?      DomClass      { get; init; }
    public DomAttributesModel? DomAttributes { get; init; }
    public DomVariantModel?    DomVariant    { get; init; }
    public DomLayoutModel?     DomLayout     { get; init; }
    public DomSpacingModel?    DomSpacing    { get; init; }
    public DomVisibilityModel? DomVisibility { get; init; }

    // ── Behavior ──────────────────────────────────────────────────────────
    public BehaviorTrackingModel?    Tracking    { get; init; }
    public BehaviorNavigationModel?  Navigation  { get; init; }
    public BehaviorInteractionModel? Interaction { get; init; }
}
