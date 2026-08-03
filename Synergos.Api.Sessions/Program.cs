using Synergos.Api.Sessions;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Sessions — el servicio de señales de sesión.
//
// Existe porque la analítica no tiene por qué vivir dentro del CMS: el CMS sirve
// páginas, y acumular ahí el rastro de comportamiento lo satura sin darle nada a
// cambio. Aquí entra por HTTP, se persiste aparte, y el CMS solo pregunta.
//
// v1 cubre BÚSQUEDA. El contrato está pensado para crecer a otras señales sin
// romperse: /v1/search-events es un tipo de evento, no el único posible.
//
// NO referencia Synergos.CMS.*: el acople es el contrato HTTP. Es lo que permite
// que este proyecto se mude a su propio repo el día que convenga. Sí referencia
// Synergos.Shared, que es fontanería de host sin dominio y viaja con él.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SearchEventStore>();
builder.Services.AddHostedService<RetentionSweeper>();

var app = builder.Build();

// La llave, la comparación en tiempo constante, la exención de /health y el aviso
// a gritos cuando no hay llave viven en Synergos.Shared: son las mismas para toda
// API interna, y copiadas a mano se pierde una decisión sutil por copia.
var apiKey = app.Configuration["Sessions:ApiKey"];
app.UseSharedKeyAuth(apiKey);

// ── Ingesta ─────────────────────────────────────────────────────────────────
app.MapPost("/v1/search-events", (SearchEventRequest req, SearchEventStore store) =>
{
    // El origen NO decide el reloj. Si lo hiciera, un CMS con la hora corrida
    // escribiría en el fichero equivocado y las ventanas del dashboard mentirían.
    var evt = new SearchEvent(req.Query ?? string.Empty, req.ResultCount, req.ElapsedMs, DateTime.UtcNow);
    var stored = store.Record(evt);

    // 202 y no 201: esto es fire-and-forget. Un query vacío o un fallo de disco NO
    // son un error del llamador —el CMS ya sirvió su búsqueda— y devolverle un 4xx
    // o un 5xx solo lograría que reintentara algo que no debe reintentar.
    return Results.Accepted(value: new { stored });
});

// ── Consulta ────────────────────────────────────────────────────────────────
app.MapGet("/v1/search/top", (DateTime? from, DateTime? to, int? limit, SearchEventStore store) =>
    Results.Ok(store.TopQueries(QueryWindow.From(from), QueryWindow.To(to), QueryWindow.Limit(limit))));

app.MapGet("/v1/search/no-results", (DateTime? from, DateTime? to, int? limit, SearchEventStore store) =>
    Results.Ok(store.TopNoResultQueries(QueryWindow.From(from), QueryWindow.To(to), QueryWindow.Limit(limit))));

app.MapGet("/health", (SearchEventStore store) =>
{
    var (written, dropped) = store.Counters;
    return Results.Ok(new { status = "ok", written, dropped, authRequired = !string.IsNullOrWhiteSpace(apiKey) });
});

app.Run();

/// <summary>Lo que un origen reporta al registrar una búsqueda.</summary>
public sealed record SearchEventRequest(string? Query, int ResultCount, long ElapsedMs);

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
