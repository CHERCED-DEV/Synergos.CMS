namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:Forms</c>. Configura el path interno de form submissions
/// (ADR 0018 + ADR 0030): storage, validación, anti-spam y rate limit.
/// </summary>
/// <remarks>
/// El path externo (formEndpoint) no usa estos settings — el editor pega
/// el endpoint y el browser hace el POST directo. Estos settings solo
/// gobiernan submissions hacia <c>/api/forms/{formKey}/submit</c>.
/// </remarks>
public sealed class FormsSettings
{
    /// <summary>
    /// Subdirectorio bajo <c>ContentRootPath</c> donde se persisten los
    /// JSON de cada submission. Default <c>"App_Data/syn-form-submissions/"</c>
    /// — fuera de <c>wwwroot</c>, no servido por el static file middleware.
    /// </summary>
    public string StorageRoot { get; init; } = "App_Data/syn-form-submissions/";

    /// <summary>
    /// Longitud máxima en chars de cualquier valor de campo individual.
    /// Default 5000. Defensa contra payloads abusivos.
    /// </summary>
    public int MaxFieldLengthChars { get; init; } = 5000;

    /// <summary>
    /// Máximo de campos distintos aceptados en una submission.
    /// Default 50. Defensa contra POSTs con miles de campos artificiales.
    /// </summary>
    public int MaxFieldsPerSubmission { get; init; } = 50;

    /// <summary>
    /// Nombre del campo honeypot. El renderer emite un input hidden con
    /// este nombre fuera de la viewport; bots que rellenan todo lo
    /// fillan, humanos no. Si llega no-vacío, el controller responde
    /// success-fake (303 al referrer) y descarta. Default <c>"syn_hp"</c>.
    /// </summary>
    public string HoneypotFieldName { get; init; } = "syn_hp";

    /// <summary>
    /// Máximo de submissions por hora desde la misma IP. Default 10.
    /// Excedido devuelve 429 Too Many Requests. Sliding window in-memory.
    /// </summary>
    public int MaxSubmissionsPerHourPerIp { get; init; } = 10;

    /// <summary>
    /// Nombre del query param que el controller añade al referrer
    /// post-success. El renderer lo lee del request actual para mostrar
    /// el mensaje de éxito. Default <c>"submitted"</c>.
    /// </summary>
    public string SuccessQueryParam { get; init; } = "submitted";

    /// <summary>
    /// Nombre del query param que el controller añade al referrer
    /// post-error. El valor será el <c>ErrorCode</c> del result. Default
    /// <c>"form-error"</c>.
    /// </summary>
    public string ErrorQueryParam { get; init; } = "form-error";
}
