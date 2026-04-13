using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Domain.Compositions.Content;

/// <summary>
/// Call-to-action editorial: etiqueta, destino, comportamiento de apertura y accesibilidad.
/// CtaLink resuelve URL y target. CtaAriaLabel complementa la accesibilidad cuando la
/// etiqueta visual no describe el destino con suficiente precisión.
/// </summary>
public sealed record ContentCtaModel(
    string? CtaLabel,
    Link?   CtaLink,
    string? CtaTarget,
    string? CtaAriaLabel);
