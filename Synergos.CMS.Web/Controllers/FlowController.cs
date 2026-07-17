using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Flow engine endpoints (Ola 41 runtime for Ola 36 schema —
/// flowDefinition + flowStep + elementFlowTrigger/Progress).
/// </summary>
/// <remarks>
/// <para>
/// State model: a cookie <c>syn-flow-{flowKey}</c> stores the current
/// step index (0-based). Missing cookie = flow not started. This is
/// deliberately simple (no session backend dependency). The cookie is
/// HttpOnly + SameSite=Lax to keep it server-driven; trusted implicitly
/// for MVP. If editors need richer state (per-step responses,
/// conditional branching) it belongs in a future Flow v2 with
/// <c>IDistributedCache</c> storage.
/// </para>
/// <para>
/// Endpoints:
/// <list type="bullet">
///   <item><c>GET /flow/{flowKey}/start</c> — initialise state, redirect to step[0].</item>
///   <item><c>POST /flow/{flowKey}/next</c> — advance state, redirect to step[n+1] or finish.</item>
///   <item><c>POST /flow/{flowKey}/cancel</c> — clear state, redirect returnUrl.</item>
/// </list>
/// </para>
/// </remarks>
[ApiController]
[Route("flow")]
public sealed class FlowController : ControllerBase
{
    private readonly FlowResolver _flowResolver;

    public FlowController(FlowResolver flowResolver) =>
        _flowResolver = flowResolver;

    [HttpGet("{flowKey}/start")]
    [AllowAnonymous]
    public IActionResult Start(string flowKey, [FromQuery] string? returnUrl)
    {
        var descriptor = _flowResolver.Resolve(flowKey);
        if (descriptor is null || descriptor.Steps.Count == 0)
        {
            return Redirect(ResolveReturnUrl(returnUrl));
        }

        SetStepCookie(flowKey, 0);
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            SetReturnUrlCookie(flowKey, returnUrl);
        }

        return Redirect(descriptor.Steps[0].Url());
    }

    [HttpPost("{flowKey}/next")]
    [AllowAnonymous]
    public IActionResult Next(string flowKey)
    {
        var descriptor = _flowResolver.Resolve(flowKey);
        if (descriptor is null || descriptor.Steps.Count == 0)
        {
            return Redirect(ResolveReturnUrl(GetReturnUrlCookie(flowKey)));
        }

        var current = GetStepIndex(flowKey) ?? 0;
        var currentStep = current >= 0 && current < descriptor.Steps.Count
            ? descriptor.Steps[current]
            : null;

        var isTerminal = currentStep is not null && currentStep.Value<bool>("isTerminal");
        var nextIndex = current + 1;

        if (isTerminal || nextIndex >= descriptor.Steps.Count)
        {
            var returnUrl = GetReturnUrlCookie(flowKey);
            ClearStepCookie(flowKey);
            ClearReturnUrlCookie(flowKey);
            return Redirect(ResolveReturnUrl(returnUrl));
        }

        SetStepCookie(flowKey, nextIndex);
        return Redirect(descriptor.Steps[nextIndex].Url());
    }

    [HttpPost("{flowKey}/cancel")]
    [AllowAnonymous]
    public IActionResult Cancel(string flowKey)
    {
        var returnUrl = GetReturnUrlCookie(flowKey);
        ClearStepCookie(flowKey);
        ClearReturnUrlCookie(flowKey);
        return Redirect(ResolveReturnUrl(returnUrl));
    }

    private static string StepCookieName(string flowKey) => $"syn-flow-{flowKey}";
    private static string ReturnUrlCookieName(string flowKey) => $"syn-flow-{flowKey}-return";

    private void SetStepCookie(string flowKey, int index) =>
        Response.Cookies.Append(
            StepCookieName(flowKey),
            index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = true,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Path = "/",
            });

    private int? GetStepIndex(string flowKey)
    {
        if (!Request.Cookies.TryGetValue(StepCookieName(flowKey), out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        return int.TryParse(raw, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    private void ClearStepCookie(string flowKey) =>
        Response.Cookies.Delete(StepCookieName(flowKey), new Microsoft.AspNetCore.Http.CookieOptions { Path = "/" });

    private void SetReturnUrlCookie(string flowKey, string returnUrl) =>
        Response.Cookies.Append(
            ReturnUrlCookieName(flowKey),
            returnUrl,
            new Microsoft.AspNetCore.Http.CookieOptions
            {
                HttpOnly = true,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Path = "/",
            });

    private string? GetReturnUrlCookie(string flowKey) =>
        Request.Cookies.TryGetValue(ReturnUrlCookieName(flowKey), out var raw) ? raw : null;

    private void ClearReturnUrlCookie(string flowKey) =>
        Response.Cookies.Delete(ReturnUrlCookieName(flowKey), new Microsoft.AspNetCore.Http.CookieOptions { Path = "/" });

    private static string ResolveReturnUrl(string? returnUrl) =>
        LocalReturnUrl.Sanitize(returnUrl);
}
