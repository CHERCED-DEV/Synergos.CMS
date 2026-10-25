using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Bff.Propiedades.Domain;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Bff.Propiedades — la visita a un inmueble en una franja de la agenda (008-propiedades-visita), un
// orquestador del CASO COMÚN del núcleo (N1): su flujo es flujos/propiedades.visita.json y lo sirve el host
// genérico. No escribe saga, enlace, fachada ni adaptadores. La clase del inmueble la declara la definición (NR-1).
//
// Lo que NO es del caso común queda aparte y atribuido: el paso interino de la duración y la modalidad (NB-1/NR-4).
// El aviso es el común del núcleo (notifications.avisar con sus «valores», T091: NR-3 de 008).
//
// ANTES DE DESPLEGAR: los destinos de booking y notifications (Propiedades__Capabilities__<cap>__BaseUrl/ApiKey),
// Propiedades:ApiKey, Propiedades:Alerts y la plantilla propiedades.visita.confirmada publicada en
// Api.Notifications (tools/provisionar.plantillas.json).
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);
builder.AddCorrelation();

var visita = AjustesDeLaVisita.De(builder.Configuration);
builder.AddOrquestadorDeclarado(new SagaVocabulary("propiedades", "la visita"), typeof(Program).Assembly,
    new PasoValidarVisita(visita.Minutos));

var app = builder.Build();
app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Propiedades:ApiKey"]);
app.MapFlujosDeclarados();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
