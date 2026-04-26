namespace Synergos.CMS.Interfaces;

/// <summary>
/// Render layer encima de <see cref="IEmailService"/>: combina un
/// template Razor con un model y devuelve el HTML listo para enviar.
/// Permite a los consumers (AccountController, FormSubmissionsController,
/// etc.) componer emails con branding consistente sin string concat
/// inline.
/// </summary>
/// <remarks>
/// Implementación por defecto en
/// <c>Synergos.CMS.Web.Services.RazorEmailTemplateRenderer</c> usando
/// <c>IRazorViewEngine</c> + <c>ITempDataProvider</c> de ASP.NET Core
/// para compilar y ejecutar las views <c>Views/Emails/*.cshtml</c>.
///
/// Convención de nombres: viewName sin prefijo "Emails/" — el renderer
/// lo agrega. Ej. caller pasa "PasswordReset", renderer busca
/// <c>Views/Emails/PasswordReset.cshtml</c>.
/// </remarks>
public interface IEmailTemplateRenderer
{
    /// <summary>
    /// Renderiza el template indicado con el model y devuelve el HTML.
    /// Lanza si la view no existe — fail-fast en dev, mejor que email
    /// silently roto en producción.
    /// </summary>
    Task<string> RenderAsync<TModel>(string viewName, TModel model, CancellationToken cancellationToken);
}
