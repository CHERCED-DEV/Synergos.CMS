using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Middlewares;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ── La raíz del CDN local, resuelta ANTES de que nadie enlace opciones (#132) ──
//
// Las dos rutas del CDN de disco se pueden escribir RELATIVAS —y el default de Development lo
// es, `../../Synergos.UI/public`, que es la disposición de repos hermanos que documenta
// docs/onboarding/arrancar-los-dos-arboles.md—. Se resuelven contra la raíz de CONTENIDO, no
// contra el directorio de trabajo: `dotnet run` lo deja en el proyecto y `dotnet exec
// bin/…/Web.dll` lo deja donde se tecleó, así que un default que dependiera del `cwd` sería el
// mismo defecto con otra cara.
//
// Va acá, y no dentro del composer ni del cliente, porque lo leen LOS DOS: el composer decide si
// lanzar mirando la configuración y el cliente la recibe por IOptions. Resuelto en uno solo, el
// otro seguiría viendo el relativo y los dos hablarían de carpetas distintas.
//
// ── Y desde el #137 lo mismo para el CERTIFICADO y el BUZÓN de correo ──
//
// Las tres familias comparten el defecto, no sólo el algoritmo: `appsettings.Development.json`
// traía `C:\LOCAL_CDN\synergos-dev.crt` y `C:\Users\HITMA\Desktop\synergos-maildrop`, que son
// rutas de UNA máquina en un fichero versionado. El gate del #132 no las vio porque perseguía las
// claves del BundleRegistry en vez del literal; hoy el gate recorre TODAS las claves de todos los
// appsettings y estas tres se resuelven por el mismo camino.
//
// Kestrel lee su certificado al construir el host, antes de que corra un solo composer, así que la
// resolución tiene que pasar ACÁ o no pasa: puesta en un composer llegaría tarde y .NET habría
// buscado ya la ruta relativa contra el cwd.
{
    var raiz = builder.Environment.ContentRootPath;
    var resueltas = new Dictionary<string, string?>();

    var relativas = new[]
    {
        "Synergos:BundleRegistry:LocalPath",
        "Synergos:LocalCdn:LocalPath",
        "Umbraco:CMS:Global:Smtp:PickupDirectoryLocation",
    }.Concat(CertificadoDeDesarrollo.Claves);

    foreach (var clave in relativas)
    {
        var crudo = builder.Configuration[clave];
        if (string.IsNullOrWhiteSpace(crudo)) continue;
        resueltas[clave] = RutaRelativaAlContenido.Resolver(crudo, raiz);
    }
    if (resueltas.Count > 0) builder.Configuration.AddInMemoryCollection(resueltas);

    // El par del HTTPS: si el appsettings lo declara, tiene que estar. Se exige ACÁ y no se deja
    // fallar a Kestrel porque su mensaje dice «no se encontró el fichero» y el de acá dice qué
    // teclear — que es toda la diferencia cuando la dependencia no la nombra ningún documento.
    if (!string.IsNullOrWhiteSpace(builder.Configuration[CertificadoDeDesarrollo.Claves[0]]))
    {
        CertificadoDeDesarrollo.Exigir(
            builder.Configuration[CertificadoDeDesarrollo.Claves[0]],
            builder.Configuration[CertificadoDeDesarrollo.Claves[1]]);
    }

    // El buzón sí se CREA en vez de exigirse: es salida de desarrollo, como una carpeta de logs, y
    // un `SpecifiedPickupDirectory` hacia una carpeta ausente falla al MANDAR el primer correo —
    // o sea lejos de acá y con una traza que habla de SMTP.
    var buzon = resueltas.GetValueOrDefault("Umbraco:CMS:Global:Smtp:PickupDirectoryLocation");
    if (!string.IsNullOrWhiteSpace(buzon)) Directory.CreateDirectory(buzon);
}

// La contraseña del administrador sale del ENTORNO, nunca de un appsettings (#150). Se exige acá,
// antes de construir el host: el instalador desatendido de Umbraco también falla sin ella, pero su
// excepción no nombra la variable ni de dónde sale — la misma distinción del certificado de #137.
CredencialDelAdministrador.Exigir(
    builder.Configuration[CredencialDelAdministrador.ClaveDelInterruptor],
    builder.Configuration[CredencialDelAdministrador.ClaveDeLaContrasena],
    builder.Environment.EnvironmentName);

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .Build();

// PERF: cache de estáticos hasheados de wwwroot. El UseStaticFiles (parameterless) de
// Umbraco resuelve estos StaticFileOptions de DI. Los assets con ?v=<hash> (CSS/JS via
// asp-append-version) son inmutables por contenido → cache 1 año; el hash cambia al
// editar → cache-bust natural (seguro incluso en dev). Sin esto, el navegador revalidaba
// ~9 CSS por navegación. Solo toca peticiones con ?v= (no afecta el CDN, que pasa
// StaticFileOptions explícitos, ni assets sin versionar).
builder.Services.Configure<StaticFileOptions>(o =>
{
    var prev = o.OnPrepareResponse;
    o.OnPrepareResponse = ctx =>
    {
        prev?.Invoke(ctx);
        if (ctx.Context.Request.Query.ContainsKey("v"))
        {
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    };
});

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
// Cache-Control, en PoliticaDeCacheDelCdnLocal (ADR 0140):
// - Pointers mutables (latest/, v0/, v1/, ...) → no-cache, must-revalidate
//   (el navegador revalida cada vez y recibe 304 si no cambió).
// - El runtime compartido: su carpeta lleva la versión del FRAMEWORK
//   (runtime/angular/21.1.6/ es Angular), no la de su contenido, y
//   publish:runtime la reescribe cuando cambia libs/shared. Sólo la huella
//   del nombre (sg-shared.<16 hex>.js) prueba el contenido: ése es
//   immutable; el plano y su import-map.json se revalidan.
// - Lo demás versionado (e.g. /synergos/column/angular/1.0.5/main.js)
//   → public, max-age=1y, immutable.
//
// Esto evita el bug "1 year cached version inmovible" cuando se publica
// algo nuevo bajo una ruta que se mueve.
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
                ctx.Context.Response.Headers.CacheControl =
                    Synergos.CMS.Web.Services.PoliticaDeCacheDelCdnLocal.CacheControl(path, maxAge);
                ctx.Context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            },
        });
        app.Logger.LogInformation(
            "Local CDN mounted: path={Path} -> route={Route} (runtime con huella y versionados immutable {MaxAge}s; latest, vN y runtime plano no-cache)",
            cdnSettings.LocalPath, cdnSettings.RoutePath, maxAge);
    }
    else if (cdnSettings.Enabled)
    {
        app.Logger.LogWarning(
            "Local CDN Enabled=true but LocalPath missing or invalid: {Path} - skipping mount",
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
