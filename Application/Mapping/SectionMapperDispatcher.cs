using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Models.Blocks;
using Synergos.CMS.Domain.Sections;
using Synergos.CMS.Application.Output;

namespace Synergos.CMS.Application.Mapping;

/// <summary>
/// Orquestador que resuelve el mapper correcto por alias de Content Type
/// y envuelve la sección resultante con sus atributos de presentación (DOM).
///
/// Los atributos DOM (id, clases CSS) son extraídos aquí y no en el mapper
/// de cada sección porque son una responsabilidad transversal de presentación,
/// no del dominio de la sección. Cada ISectionMapper devuelve ISection puro.
///
/// El resultado es un SectionView: el contrato entre Application y Presentation.
/// </summary>
public sealed class SectionMapperDispatcher
{
    private readonly IReadOnlyDictionary<string, ISectionMapper> _mappers;
    private readonly GenericElementMapper?                       _fallback;
    private readonly ILogger<SectionMapperDispatcher>            _logger;

    public SectionMapperDispatcher(
        IEnumerable<ISectionMapper>      mappers,
        ILogger<SectionMapperDispatcher> logger,
        GenericElementMapper?            fallback = null)
    {
        // El diccionario se construye una sola vez en el ciclo de vida del servicio.
        _mappers  = mappers.ToDictionary(m => m.SupportedAlias, StringComparer.OrdinalIgnoreCase);
        _fallback = fallback;
        _logger   = logger;
    }

    public SectionView? Dispatch(BlockListItem block) => DispatchElement(block.Content);

    public SectionView? Dispatch(BlockGridItem item) => DispatchElement(item.Content);

    public SectionView? DispatchElement(Umbraco.Cms.Core.Models.PublishedContent.IPublishedElement element)
    {
        var alias = element.ContentType.Alias;

        if (!_mappers.TryGetValue(alias, out var mapper))
        {
            if (_fallback is not null)
            {
                // No specific mapper found — fall back to the generic composition-driven mapper.
                _logger.LogDebug(
                    "SectionMapperDispatcher: no specific mapper for alias '{Alias}'. Using GenericElementMapper fallback.",
                    alias);
                return _fallback.Map(element);
            }

            _logger.LogWarning(
                "SectionMapperDispatcher: no mapper registered for alias '{Alias}'. Block will be skipped.",
                alias);
            return null;
        }

        var section = mapper.Map(element);
        if (section is null) return null;

        var (elementId, cssClasses) = ReadDomAttributes(element);
        return new SectionView(section, elementId, cssClasses);
    }

    /// <summary>Returns the mapper for the given alias, or null if none registered.</summary>
    public ISectionMapper? FindMapper(string alias)
        => _mappers.TryGetValue(alias, out var m) ? m : null;

    private static (string? ElementId, string? CssClasses) ReadDomAttributes(
        Umbraco.Cms.Core.Models.PublishedContent.IPublishedElement element)
        => DomAttributeReader.Read(element);
}
