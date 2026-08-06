namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:Members</c>. Configura el comportamiento del runtime de
/// miembros (gating, redirects, cookies futuros).
/// </summary>
/// <remarks>
/// Mantenida deliberadamente mínima. Nuevos campos solo cuando un
/// consumidor concreto los necesita (ADR 0010 spirit, ADR 0009).
/// Binding hecho por <c>OptionsComposer</c>; este ensamblado no
/// referencia <c>Microsoft.Extensions.Options</c> — el composer
/// extrae <c>.Value</c> y pasa el POCO al consumidor.
/// </remarks>
public sealed class MembersSettings
{
    /// <summary>
    /// Path al que se redirige a un visitante anónimo o sin role
    /// adecuado cuando intenta acceder a una página con
    /// <c>compMemberGating.requiresAuth=true</c>. Default: <c>/login</c>.
    /// El path se concatena con <c>?returnUrl=...</c> automáticamente.
    /// </summary>
    public string LoginPath { get; init; } = "/login";

    /// <summary>
    /// Si true (Ola 83), el AccountController.Register NO firma sesión
    /// inmediata; envía email de confirmación con token y redirige a
    /// <c>/account/registered</c> ("revisa tu email"). El miembro debe
    /// hacer click en el link del email para activar la cuenta.
    /// Default false — opt-in. Para sites que necesitan friction baja
    /// (registro casual), dejar en false.
    /// </summary>
    public bool RequireEmailConfirmation { get; init; } = false;
}
