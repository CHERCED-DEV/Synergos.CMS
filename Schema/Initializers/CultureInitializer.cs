using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 0 — Ensures the platform languages exist before any content type
/// that uses ContentVariation.Culture is created or patched.
///
/// Languages created:
///   es-CO  Spanish (Colombia) — default + mandatory
///   en-US  English (US)       — non-default
///
/// Legacy cleanup: removes es-ES (Spanish Spain) if it was previously created.
///
/// Idempotent: runs guards on each startup but only creates/updates when needed.
/// </summary>
internal sealed class CultureInitializer : ISchemaInitializer
{
    private readonly ILocalizationService _localization;

    public CultureInitializer(ILocalizationService localization)
    {
        _localization = localization;
    }

    public void Initialize()
    {
        // Remove legacy es-ES if it was created in a previous run.
        RemoveLanguage("es-ES");

        // es-CO must be created first so it becomes the default.
        EnsureLanguage("es-CO", "Spanish (Colombia)",        isDefault: true,  isMandatory: true);
        EnsureLanguage("en-US", "English (United States)",   isDefault: false, isMandatory: false);
    }

    private void RemoveLanguage(string isoCode)
    {
        var lang = _localization.GetLanguageByIsoCode(isoCode);
        if (lang is not null)
            _localization.Delete(lang);
    }

    private void EnsureLanguage(string isoCode, string cultureName, bool isDefault, bool isMandatory)
    {
        var existing = _localization.GetLanguageByIsoCode(isoCode);

        if (existing is not null)
        {
            var dirty = false;
            if (isDefault   && !existing.IsDefault)   { existing.IsDefault   = true; dirty = true; }
            if (isMandatory && !existing.IsMandatory) { existing.IsMandatory = true; dirty = true; }
            if (dirty) _localization.Save(existing);
            return;
        }

        var lang = new Language(isoCode, cultureName)
        {
            IsDefault   = isDefault,
            IsMandatory = isMandatory
        };
        _localization.Save(lang);
    }
}
