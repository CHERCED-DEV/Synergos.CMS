using Synergos.Api.Sessions.Endpoints;
using Synergos.Api.Sessions.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Sessions — la capacidad de señales de sesión y comportamiento.
//
// Existe porque la analítica no tiene por qué vivir dentro del CMS: el CMS sirve
// páginas, y acumular ahí el rastro de comportamiento lo satura sin darle nada a
// cambio. Aquí entra por HTTP, se persiste aparte, y el CMS solo pregunta.
//
// AGNÓSTICA de nacimiento: no sabe si lo que se buscó era un inmueble, un curso o
// un trámite. Fue la primera capacidad del sistema — solo que cuando se escribió
// (ADR 0130) todavía no teníamos la palabra.
//
// v1 cubre BÚSQUEDA. El contrato crece a otras señales sin romperse:
// /v1/search-events es UN tipo de evento, no el único posible.
//
// NO referencia Synergos.CMS.*: el acople es el contrato HTTP. Alineada al molde
// del doc 08 §4 — Contracts/ separado de Domain/, ruteo en Endpoints/, y este
// Program.cs con arranque y nada más.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.AddSingleton<SearchEventStore>();
builder.Services.AddHostedService<RetentionSweeper>();

// El reloj se inyecta también acá: quién decide el "ahora" de un evento es una
// decisión de diseño (el origen NO lo decide), y un test que tenga que esperar a
// que pase un día no es un test.
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// La llave, la comparación en tiempo constante, la exención de /health y el aviso
// a gritos cuando no hay llave viven en Synergos.Shared: son las mismas para toda
// API interna, y copiadas a mano se pierde una decisión sutil por copia.
var apiKey = app.Configuration["Sessions:ApiKey"];
app.UseCorrelation();
app.UseSharedKeyAuth(apiKey);

app.MapSessionsEndpoints();

app.MapGet("/health", (SearchEventStore store) =>
{
    var (written, dropped) = store.Counters;
    return Results.Ok(new { status = "ok", written, dropped, authRequired = !string.IsNullOrWhiteSpace(apiKey) });
});

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
