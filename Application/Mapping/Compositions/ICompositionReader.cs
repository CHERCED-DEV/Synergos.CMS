using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Application.Mapping.Compositions;

/// <summary>
/// Lee las propiedades de UNA composición desde cualquier IPublishedElement.
///
/// Cada implementación conoce exactamente qué aliases de propiedad mapear.
/// Los readers son stateless y reutilizables — se registran como Singleton.
///
/// Contrato: nunca lanza excepción. Retorna el modelo con nulls si las
/// propiedades no existen (composición no aplicada al tipo de contenido).
/// </summary>
public interface ICompositionReader<T>
{
    T Read(IPublishedElement element);
}
