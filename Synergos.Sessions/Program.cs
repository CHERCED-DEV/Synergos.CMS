using Synergos.Sessions;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Sessions — el servicio de señales de sesión.
//
// Existe porque la analítica no tiene por qué vivir dentro del CMS: el CMS sirve
// páginas, y acumular ahí el rastro de comportamiento lo satura sin darle nada a
// cambio. Aquí entra por HTTP, se persiste aparte, y el CMS solo pregunta.
//
// v1 cubre BÚSQUEDA. El contrato está pensado para crecer a otras señales sin
// romperse: /v1/search-events es un tipo de evento, no el único posible.
//
// NO referencia Synergos.CMS.*: el acople es el contrato HTTP. Es lo que permite
// que este proyecto se mude a su propio repo el día que convenga.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SearchEventStore>();
builder.Services.AddHostedService<RetentionSweeper>();

var app = builder.Build();

var apiKey = app.Configuration["Sessions:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
{
    // Degradar A GRITOS, no en silencio. Sin llave el servicio arranca —si no, un
    // `dotnet run` recién clonado no levantaría y eso empuja a poner la llave en el
    // repo— pero queda dicho que la ingesta está abierta: cualquiera puede envenenar
    // el "top queries" del dashboard.
    app.Logger.LogWarning(
        "Sessions:ApiKey NO está configurada: la ingesta queda ABIERTA. " +
        "Sirve para desarrollo; en cualquier despliegue alcanzable es un agujero.");
}

app.Use(async (ctx, next) =>
{
    // /health queda fuera a propósito: un chequeo de vida que exige credenciales no
    // sirve como chequeo de vida.
    if (string.IsNullOrWhiteSpace(apiKey) || ctx.Request.Path.StartsWithSegments("/health"))
    {
        await next();
        return;
    }

    var sent = ctx.Request.Headers[SessionsApi.ApiKeyHeader].ToString();
    // Comparación de longitud fija: una comparación normal filtra el prefijo correcto
    // por tiempo de respuesta.
    if (!FixedTimeEquals(sent, apiKey))
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});

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
    Results.Ok(store.TopQueries(From(from), To(to), Limit(limit))));

app.MapGet("/v1/search/no-results", (DateTime? from, DateTime? to, int? limit, SearchEventStore store) =>
    Results.Ok(store.TopNoResultQueries(From(from), To(to), Limit(limit))));

app.MapGet("/health", (SearchEventStore store) =>
{
    var (written, dropped) = store.Counters;
    return Results.Ok(new { status = "ok", written, dropped, authRequired = !string.IsNullOrWhiteSpace(apiKey) });
});

app.Run();

// Ventanas por defecto sensatas: sin `from` se asumen 7 días, que es lo que mira el
// dashboard. Sin tope, una petición sin `limit` podría devolver el catálogo entero.
static DateTime From(DateTime? v) => v?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-7);
static DateTime To(DateTime? v) => v?.ToUniversalTime() ?? DateTime.UtcNow;
static int Limit(int? v) => Math.Clamp(v ?? 10, 1, SessionsApi.MaxLimit);

static bool FixedTimeEquals(string? a, string? b)
{
    var x = System.Text.Encoding.UTF8.GetBytes(a ?? string.Empty);
    var y = System.Text.Encoding.UTF8.GetBytes(b ?? string.Empty);
    return x.Length == y.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(x, y);
}

/// <summary>Lo que un origen reporta al registrar una búsqueda.</summary>
public sealed record SearchEventRequest(string? Query, int ResultCount, long ElapsedMs);

/// <summary>Constantes del contrato HTTP, compartidas con los tests.</summary>
public static class SessionsApi
{
    /// <summary>Cabecera con la llave compartida.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    /// <summary>Tope duro de filas por consulta.</summary>
    public const int MaxLimit = 500;
}

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
