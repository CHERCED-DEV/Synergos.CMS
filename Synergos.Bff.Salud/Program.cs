using Synergos.Bff.Salud.Clients;
using Synergos.Bff.Salud.Domain;
using Synergos.Bff.Salud.Endpoints;
using Synergos.Bff.Salud.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Bff.Salud — el primer orquestador.
//
// Lo que aporta y NINGUNA capacidad puede aportar: el ORDEN y la COMPENSACIÓN.
// Booking sabe apartar, Payments sabe cobrar, Consent sabe si hay permiso — y
// ninguno de los tres sabe que el permiso va antes del cupo, que el cupo va antes
// del cobro, y que si la confirmación falla hay que devolver la plata.
//
// NO referencia ninguna Synergos.Api.*: habla con ellas por HTTP, como el CMS.
//
// LA MÁQUINA DE SAGAS VIVE ACÁ DENTRO A PROPÓSITO. Es plumbing que los ocho BFF
// van a necesitar, pero con UN consumidor promoverla sería la abstracción
// prematura que CLAUDE.md §6 prohíbe. Se promueve a Synergos.Bff.Core cuando el
// segundo la necesite — la misma disciplina que se aplicó a JsonCollectionStore,
// que esperó a tener seis.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SaludStorageOptions>(builder.Configuration.GetSection("Salud:Storage"));
builder.Services.AddSingleton<ISagaStore, FileSystemSagaStore>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SaludCapabilities>();
builder.Services.AddSingleton<Compensator>();
builder.Services.AddSingleton<AppointmentFlow>();
builder.Services.AddHostedService<CompensationSweeper>();

// Un cliente nombrado POR capacidad: cada una con su URL, su llave y su timeout.
// Mezclarlas haría que subir el timeout de Payments se lo subiera a Consent.
foreach (var cap in new[] { SaludCapabilities.Consent, SaludCapabilities.Booking, SaludCapabilities.Pricing, SaludCapabilities.Payments })
{
    var seccion = builder.Configuration.GetSection($"Salud:Capabilities:{cap}");
    builder.Services.AddHttpClient(cap, http =>
    {
        http.BaseAddress = new Uri(seccion["BaseUrl"] ?? $"http://localhost/{cap}/");
        http.Timeout = TimeSpan.FromSeconds(double.TryParse(seccion["TimeoutSeconds"], out var s) ? s : 10);
        if (seccion["ApiKey"] is { Length: > 0 } llave)
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation(SharedKeyAuth.HeaderName, llave);
        }
    });
}

var app = builder.Build();

app.UseSharedKeyAuth(app.Configuration["Salud:ApiKey"]);
app.MapSaludEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
