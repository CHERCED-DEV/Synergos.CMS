using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;
using Synergos.CMS.Domain.Shared;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Application.Services;

/// <summary>
/// Resolves FormDefinition content nodes into renderable FormDefinitionModel.
///
/// Two resolution modes:
///   1. ByAlias — finds FormDefinition by formAlias across all SharedContentFolder nodes
///   2. ByContent — converts a given IPublishedContent directly
///
/// Used by:
///   - FormEmbed element mapper (content picker → resolved form)
///   - FormSubmissionController (alias lookup for validation + routing)
/// </summary>
public sealed class FormRenderService
{
    private readonly IContentContextAccessor _accessor;

    public FormRenderService(IContentContextAccessor accessor) => _accessor = accessor;

    /// <summary>
    /// Resolves a FormDefinition content node into a renderable model.
    /// Returns null if the content is not a FormDefinition.
    /// </summary>
    public FormDefinitionModel? ResolveFromContent(IPublishedContent? content)
    {
        if (content is null || content.ContentType.Alias != ContentTypeKeys.Aliases.FormDefinition)
            return null;

        return BuildModel(content);
    }

    /// <summary>
    /// Finds a FormDefinition by its formAlias property across all published content.
    /// Returns null if not found.
    /// </summary>
    public FormDefinitionModel? ResolveByAlias(string formAlias)
    {
        if (string.IsNullOrWhiteSpace(formAlias)) return null;

        // Walk root content → find SharedContentFolder → find FormDefinition by alias
        foreach (var root in _accessor.GetAtRoot())
        {
            var formNode = FindFormByAlias(root, formAlias, depth: 0);
            if (formNode is not null)
                return BuildModel(formNode);
        }

        return null;
    }

    private static IPublishedContent? FindFormByAlias(IPublishedContent node, string alias, int depth)
    {
        if (depth > 4) return null; // safety guard

        if (node.ContentType.Alias == ContentTypeKeys.Aliases.FormDefinition)
        {
            var nodeAlias = node.Value<string>("formAlias");
            if (string.Equals(nodeAlias, alias, StringComparison.OrdinalIgnoreCase))
                return node;
        }

        foreach (var child in node.Children ?? [])
        {
            var found = FindFormByAlias(child, alias, depth + 1);
            if (found is not null) return found;
        }

        return null;
    }

    private static FormDefinitionModel BuildModel(IPublishedContent content)
    {
        var fields = new List<FormFieldModel>();

        var blockList = content.Value<BlockListModel>("formFields");
        if (blockList is not null)
        {
            foreach (var block in blockList)
            {
                var el = block.Content;
                fields.Add(new FormFieldModel(
                    Label:             el.Value<string>("fieldLabel") ?? string.Empty,
                    Name:              el.Value<string>("fieldName") ?? string.Empty,
                    Type:              el.Value<string>("fieldType") ?? "text",
                    Placeholder:       el.Value<string>("fieldPlaceholder"),
                    Required:          el.Value<bool>("fieldRequired"),
                    Options:           el.Value<string>("fieldOptions"),
                    ValidationPattern: el.Value<string>("fieldValidation"),
                    Width:             el.Value<string>("fieldWidth") ?? "full"));
            }
        }

        var recipientEmail  = content.Value<string>("recipientEmail");
        var thankYouMessage = content.Value<string>("thankYouMessage");

        // Site-level fallbacks: walk ancestors to find the siteRoot's SiteSettings.
        // Applies when the form has no per-form recipient or thank-you message configured.
        if (string.IsNullOrWhiteSpace(recipientEmail) || string.IsNullOrWhiteSpace(thankYouMessage))
        {
            var siteRoot     = content.AncestorsOrSelf().FirstOrDefault(n => n.ContentType.Alias == ContentTypeKeys.Aliases.SiteRoot);
            var siteSettings = siteRoot?.FirstChild(ContentTypeKeys.Aliases.SiteSettingsAlias);
            if (siteSettings is not null)
            {
                if (string.IsNullOrWhiteSpace(recipientEmail))
                    recipientEmail = siteSettings.Value<string>("formRecipientEmail");
                if (string.IsNullOrWhiteSpace(thankYouMessage))
                    thankYouMessage = siteSettings.Value<string>("defaultThankYouMessage");
            }
        }

        return new FormDefinitionModel(
            FormName:            content.Value<string>("formName") ?? content.Name,
            FormAlias:           content.Value<string>("formAlias") ?? string.Empty,
            SubmitLabel:         content.Value<string>("submitLabel") ?? "Enviar",
            RecipientEmail:      recipientEmail,
            WebhookUrl:          content.Value<string>("webhookUrl"),
            ThankYouMessage:     thankYouMessage,
            ThankYouRedirectUrl: content.Value<string>("thankYouRedirectUrl"),
            IntegrationProvider: content.Value<string>("formIntegrationProvider"),
            IntegrationId:       content.Value<string>("formIntegrationId"),
            Fields:              fields);
    }
}
