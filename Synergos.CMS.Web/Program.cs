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
