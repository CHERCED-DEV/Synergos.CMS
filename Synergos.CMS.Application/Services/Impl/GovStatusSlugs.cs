using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Traducción entre el enum interno <see cref="CaseStatus"/> del expediente y los slugs
/// de cara pública que consume la UI (contrato estricto — la lección de la Ola 7):
/// <c>submitted | in-review | info-requested | approved | rejected</c>. Vive en
/// Application (lógica pura, ADR 0002) para que el tracking (filtro por estado) y el
/// workflow (mapeo de outcome) compartan la MISMA tabla sin duplicarla.
/// </summary>
public static class GovStatusSlugs
{
    /// <summary>Slug público estable del estado del expediente.</summary>
    public static string ToSlug(CaseStatus status) => status switch
    {
        CaseStatus.Radicado => "submitted",
        CaseStatus.EnRevision => "in-review",
        CaseStatus.Subsanacion => "info-requested",
        CaseStatus.Resuelto => "approved",
        CaseStatus.Rechazado => "rejected",
        _ => status.ToString().ToLowerInvariant(),
    };

    /// <summary>
    /// Parsea un slug público a estado; devuelve false si no matchea ninguno conocido.
    /// </summary>
    public static bool TryParse(string? slug, out CaseStatus status)
    {
        switch (slug?.Trim().ToLowerInvariant())
        {
            case "submitted":
            case "radicado":
                status = CaseStatus.Radicado;
                return true;
            case "in-review":
            case "en-revision":
                status = CaseStatus.EnRevision;
                return true;
            case "info-requested":
            case "subsanacion":
                status = CaseStatus.Subsanacion;
                return true;
            case "approved":
            case "resuelto":
                status = CaseStatus.Resuelto;
                return true;
            case "rejected":
            case "rechazado":
                status = CaseStatus.Rechazado;
                return true;
            default:
                status = default;
                return false;
        }
    }
}
