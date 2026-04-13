using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Components;

/// <summary>
/// Contratos de composición para ViewModels.
/// Permiten acceso fuertemente tipado a composiciones estándar
/// sin necesidad de conocer el tipo concreto del ViewModel.
///
/// Uso: partials genéricos, helpers de render, y mappers
/// que operan sobre familias de elementos en vez de tipos individuales.
/// </summary>

public interface IHasText
{
    ContentTextModel? Text { get; }
}

public interface IHasCollection
{
    ContentCollectionModel? Collection { get; }
}

public interface IHasCta
{
    ContentCtaModel? Cta { get; }
}

public interface IHasBadge
{
    ContentBadgeModel? Badge { get; }
}

public interface IHasMedia
{
    ContentMediaModel? Media { get; }
}
