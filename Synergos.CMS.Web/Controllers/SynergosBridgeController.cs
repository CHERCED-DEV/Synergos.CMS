using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Diccionario;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Sirve <c>/synergos-bridge.js</c> — payload <c>window.synergos = {...}</c>
/// como external script para sites con CSP estricto sin
/// <c>'unsafe-inline'</c> en <c>script-src</c>. Cap-260 Batch A
/// (Olas 251-253). Activar via
/// <see cref="Synergos.CMS.Application.Configuration.HostBridgeSettings.CspStrictMode"/>.
/// </summary>
/// <remarks>
/// El endpoint genera el payload por request — el bridge contiene
/// member/theme/page context que varía. <c>Cache-Control: private,
/// no-store</c> evita compartir entre members + obliga al navegador
/// a fetch fresh en cada navegación. Trade-off vs inline: 1 RTT extra
/// por page load, eliminando la necesidad de relajar CSP. La latencia
/// es minimal (mismo session/cookies que el HTML parent).
///
/// Si el builder lanza, retorna shape mínimo para que UI no rompa
/// (mismo failure path que el partial inline).
/// </remarks>
[ApiController]
public sealed class SynergosBridgeController : Controller
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IHostBridgeContextBuilder _bridge;
    private readonly SeccionesDeLaPagina _secciones;
    private readonly ILogger<SynergosBridgeController> _logger;

    public SynergosBridgeController(
        IHostBridgeContextBuilder bridge,
        SeccionesDeLaPagina secciones,
        ILogger<SynergosBridgeController> logger)
    {
        _bridge = bridge;
        _secciones = secciones;
        _logger = logger;
    }

    /// <param name="secciones">Las secciones de diccionario que piden los elementos de la página
    /// que pide este script (ADR 0136): las escribe <c>_SynergosBridge.cshtml</c> en la URL, porque
    /// esta petición no ve qué emitió aquélla. La cultura viaja igual, en <c>culture</c>/<c>ui-culture</c>.</param>
    [HttpGet("/synergos-bridge.js")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Get([FromQuery] string? secciones = null)
    {
        _secciones.Anotar(SeccionesDeLaPagina.DeLaConsulta(secciones));

        string json;
        try
        {
            var ctx = _bridge.Build();
            json = JsonSerializer.Serialize(ctx, SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Synergos bridge build failed; serving minimal fallback payload");
            json = HostBridgeFallback.Json;
        }

        var sb = new StringBuilder(json.Length + 256);
        sb.Append("// /synergos-bridge.js — host bridge canónico CMS↔UI (CSP-strict).\n");
        sb.Append("window.synergos = ");
        sb.Append(json);
        sb.Append(";\nwindow.synergos.i18n.t = function(k, f) {\n");
        sb.Append("    if (this.keys && this.keys[k] !== undefined) return this.keys[k];\n");
        sb.Append("    return f !== undefined ? f : k;\n");
        sb.Append("};\n");

        // Cache-Control: private, no-store — payload es per-member/per-page.
        // Compartirlo entre users sería un privacy leak.
        Response.Headers.CacheControl = "private, no-store";

        return Content(sb.ToString(), "application/javascript", Encoding.UTF8);
    }
}
