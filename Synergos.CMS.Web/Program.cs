using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Middlewares;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

// Cross-cutting middlewares — wired before Umbraco so every request
// (including backoffice and content) carries a correlation id and is
// bounded by the timeout. See Ola 3 of the migration plan.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<TimeoutMiddleware>();

// Ola 76 — re-execute /error/{statusCode} cuando ASP.NET responde con
// un error code (404, 500, 503...). El ErrorController busca un
// transversalErrorPage publicado matching y lo renderiza; si no
// encuentra, fallback inline. Preserva el status code original via
// Response.StatusCode = statusCode dentro del controller.
app.UseStatusCodePagesWithReExecute("/error/{0}");

// Olas 281-282 — Local CDN static files (ADR 0089 Batch A). Sirve
// bundles desde un directorio físico (ej. C:\LOCAL_CDN) bajo el
// RoutePath configurado. Useful cuando la CDN remota (ADR 0012)
// no está publicada todavía. Auto-detecta si Enabled+LocalPath son
// válidos; sino, no-op silent.
//
// Smart cache control:
// - Paths versionados semver (e.g. /synergos-column/angular/1.0.5/main.js)
//   → Cache-Control: public, max-age=1y, immutable.
// - Paths pointers mutables (latest/, v0/, v1/, ...)
//   → Cache-Control: no-cache, must-revalidate (browser revalida con
//     server cada request, devuelve 304 si no cambió).
//
// Esto evita el bug "1 year cached version inmovible" cuando el
// CDN team publica una versión nueva bajo un pointer mutable.
{
    var cdnSettings = app.Services.GetRequiredService<IOptions<LocalCdnSettings>>().Value;
    if (cdnSettings.Enabled &&
        !string.IsNullOrWhiteSpace(cdnSettings.LocalPath) &&
        Directory.Exists(cdnSettings.LocalPath))
    {
        var maxAge = Math.Max(0, cdnSettings.CacheControlMaxAgeSeconds);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(cdnSettings.LocalPath),
            RequestPath = cdnSettings.RoutePath.TrimEnd('/'),
            ServeUnknownFileTypes = false,
            OnPrepareResponse = ctx =>
            {
                var path = ctx.Context.Request.Path.Value ?? string.Empty;
                // Pointers mutables (no-cache):
                //   /latest/    → global "current"
                //   /v0/, /v1/, /v2/, ... → major-version pointer
                // Pointers inmutables (cache 1y):
                //   /1.0.5/, /0.1.0/, ... → semver exacto
                var isMutablePointer =
                    path.Contains("/latest/", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("/latest", StringComparison.OrdinalIgnoreCase) ||
                    System.Text.RegularExpressions.Regex.IsMatch(
                        path,
                        @"/v\d+(/|$)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                ctx.Context.Response.Headers.CacheControl = isMutablePointer
                    ? "public, no-cache, must-revalidate"
                    : $"public, max-age={maxAge}, immutable";
                ctx.Context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            },
        });
        app.Logger.LogInformation(
            "Local CDN mounted: path={Path} → route={Route} (versioned cache {MaxAge}s, latest no-cache)",
            cdnSettings.LocalPath, cdnSettings.RoutePath, maxAge);
    }
    else if (cdnSettings.Enabled)
    {
        app.Logger.LogWarning(
            "Local CDN Enabled=true but LocalPath missing or invalid: {Path} — skipping mount",
            cdnSettings.LocalPath);
    }
}

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        // Attribute-routed controllers first (e.g. HealthController
        // at /_health) so their explicit routes take precedence over
        // Umbraco's catch-all website router.
        u.EndpointRouteBuilder.MapControllers();
        u.UseInstallerEndpoints();
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

await app.RunAsync();
