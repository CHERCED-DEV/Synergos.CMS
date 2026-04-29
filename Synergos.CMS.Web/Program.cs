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
// válidos; sino, no-op silent. Cache-Control immutable asume bundles
// fingerprinted (e.g. element-bundle.{hash}.js).
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
                ctx.Context.Response.Headers.CacheControl =
                    $"public, max-age={maxAge}, immutable";
                ctx.Context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            },
        });
        app.Logger.LogInformation(
            "Local CDN mounted: path={Path} → route={Route} (cache {MaxAge}s)",
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
