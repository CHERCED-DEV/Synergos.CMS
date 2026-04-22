using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// On first save of a blank <c>pageBase</c>, pre-populates the
/// <c>sections</c> property with a minimal layout scaffold so the
/// editor starts with a baseline (Hero + 2ColEven) instead of a
/// blank page.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in via <c>Synergos:LayoutComposer:EnableStarterScaffold</c>.
/// Defaults to <c>false</c> to preserve surprise-free UX — a deploy
/// that prefers blank pages doesn't need to do anything.
/// </para>
/// <para>
/// Trigger condition (all three must hold):
/// <list type="number">
///   <item>The feature flag is <c>true</c>.</item>
///   <item>The saved entity's ContentType is <c>pageBase</c>.</item>
///   <item>The <c>sections</c> property is empty (idempotent — won't
///         clobber an editor who cleared it intentionally more than
///         once; if concerned, disable the flag).</item>
/// </list>
/// </para>
/// <para>
/// Scaffold: one <c>elementLayoutHero</c> full-bleed followed by one
/// <c>elementLayout2ColEven</c>. Each preset starts empty (no nested
/// content blocks inside areas). The editor fills the Hero with copy
/// and the 2ColEven with text/media. Ambos heredan los defaults del
/// <c>LayoutPresetDefaults</c> handler (containerType/theme/spacing).
/// </para>
/// </remarks>
public sealed class LayoutComposerStarterScaffold
    : INotificationHandler<ContentSavingNotification>
{
    // pageBase ContentType key from uSync/v9/ContentTypes/page-base.config
    private static readonly Guid PageBaseContentTypeKey =
        Guid.Parse("b4df602f-1c16-4ad7-8b57-69aa64ff6ad1");

    // Layout Preset ContentType keys used in the scaffold.
    private const string HeroContentTypeKey = "fef510f5-59c8-4ae8-b499-2188017df5c1";
    private const string TwoColEvenContentTypeKey = "e8baf208-35d5-4c9f-9aa4-967fa5e070bf";

    // Area keys from DTBlockGridSections.config — must match the
    // declared areas for each preset, or the Block Grid editor
    // discards the layout entries as orphaned.
    private const string HeroContentAreaKey = "3b7fd963-7c46-4ae3-9141-c17d52bcd47a";
    private const string TwoColLeftAreaKey = "ac1dd87e-62ec-4ed1-bb74-3082d86baf59";
    private const string TwoColRightAreaKey = "10834317-e57a-4628-90f6-41211d07da7b";

    private readonly IOptions<LayoutComposerSettings> _settings;

    public LayoutComposerStarterScaffold(IOptions<LayoutComposerSettings> settings) =>
        _settings = settings;

    public void Handle(ContentSavingNotification notification)
    {
        if (!_settings.Value.EnableStarterScaffold)
        {
            return;
        }

        foreach (var content in notification.SavedEntities)
        {
            if (content.ContentType.Key != PageBaseContentTypeKey)
            {
                continue;
            }

            // Guard: only seed when sections is empty. Checking all
            // stored values (culture/segment variants) keeps the
            // scaffold from triggering if any culture has content.
            var anyValue = content.Properties["sections"]?.Values
                .Select(v => (v.EditedValue as string) ?? (v.PublishedValue as string))
                .Any(s => !string.IsNullOrWhiteSpace(s));
            if (anyValue == true)
            {
                continue;
            }

            content.SetValue("sections", BuildScaffoldJson());
        }
    }

    internal static string BuildScaffoldJson()
    {
        var heroUdi = $"umb://element/{Guid.NewGuid():N}";
        var twoColUdi = $"umb://element/{Guid.NewGuid():N}";

        return $$"""
        {
          "layout": {
            "Umbraco.BlockGrid": [
              {
                "contentUdi": "{{heroUdi}}",
                "columnSpan": 12,
                "rowSpan": 1,
                "areas": [
                  { "key": "{{HeroContentAreaKey}}", "items": [] }
                ]
              },
              {
                "contentUdi": "{{twoColUdi}}",
                "columnSpan": 12,
                "rowSpan": 1,
                "areas": [
                  { "key": "{{TwoColLeftAreaKey}}", "items": [] },
                  { "key": "{{TwoColRightAreaKey}}", "items": [] }
                ]
              }
            ]
          },
          "contentData": [
            {
              "contentTypeKey": "{{HeroContentTypeKey}}",
              "udi": "{{heroUdi}}"
            },
            {
              "contentTypeKey": "{{TwoColEvenContentTypeKey}}",
              "udi": "{{twoColUdi}}"
            }
          ],
          "settingsData": [],
          "expose": []
        }
        """;
    }
}
