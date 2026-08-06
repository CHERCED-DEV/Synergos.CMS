using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Resolves a <c>flowDefinition</c> node and its ordered <c>flowStep</c>
/// children from the Umbraco content tree (Ola 36 schema). Used by
/// <see cref="Controllers.FlowController"/> and by the
/// <c>elementFlowProgress</c> renderer.
/// </summary>
/// <remarks>
/// Pure Umbraco content-tree query — lives in Web because Umbraco types
/// do not leak into <c>Synergos.CMS.Application</c> (ADR 0002). Steps
/// are ordered by their tree position (SortOrder) which is how editors
/// drag them in the backoffice; that matches the editorial flow order.
/// </remarks>
public sealed class FlowResolver
{
    private const string FlowDefinitionAlias = "flowDefinition";
    private const string FlowStepAlias = "flowStep";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;

    public FlowResolver(IUmbracoContextAccessor umbracoContextAccessor) =>
        _umbracoContextAccessor = umbracoContextAccessor;

    /// <summary>
    /// Returns the flow + its steps, or <c>null</c> when <paramref name="flowKey"/>
    /// doesn't match any published <c>flowDefinition</c>.
    /// </summary>
    public FlowDescriptor? Resolve(string flowKey)
    {
        if (string.IsNullOrWhiteSpace(flowKey))
        {
            return null;
        }

        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var ctx) || ctx.Content is null)
        {
            return null;
        }

        var definition = ctx.Content
            .GetAtRoot()
            .SelectMany(r => r.DescendantsOrSelfOfType(FlowDefinitionAlias))
            .FirstOrDefault(n => string.Equals(n.Value<string>("flowKey"), flowKey, StringComparison.OrdinalIgnoreCase));

        if (definition is null)
        {
            return null;
        }

        var steps = definition.Children
            .Where(c => string.Equals(c.ContentType.Alias, FlowStepAlias, StringComparison.Ordinal))
            .OrderBy(c => c.SortOrder)
            .ToList();

        return new FlowDescriptor(definition, steps);
    }
}

/// <summary>
/// Resolved flow: the definition node and its ordered steps.
/// </summary>
public sealed record FlowDescriptor(
    IPublishedContent Definition,
    IReadOnlyList<IPublishedContent> Steps);
