using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Bff.Gobierno — el trámite de gobierno por la puerta (009-gobierno-tramite): radicar con la tasa que
// publicó la entidad y, después, los actos sobre el expediente, como flujos declarados del caso común sobre los
// pasos de procesos del núcleo (workflow.*). No referencia ninguna capacidad: todo por HTTP.
//
// ANTES DE DESPLEGAR: Gobierno:ApiKey.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);
builder.AddCorrelation();

var app = builder.Build();
app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Gobierno:ApiKey"]);
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
