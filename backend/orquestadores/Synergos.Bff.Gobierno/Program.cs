using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Gobierno.Domain;
using Synergos.Bff.Gobierno.Endpoints;
using Synergos.Bff.Pasos;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Bff.Gobierno — el trámite de gobierno por la puerta (009-gobierno-tramite): radicar con la tasa que
// publicó la entidad (flujos/gobierno.radicacion.json) y, después, los actos sobre el expediente, como flujos
// declarados del caso común sobre los pasos de procesos del núcleo (workflow.*). No referencia ninguna capacidad.
//
// Lo que NO es del caso común queda aparte y atribuido: la oferta de los trámites (P12, NB-8), como Reservas.
//
// ANTES DE DESPLEGAR: los destinos de pricing, payments, workflow y notifications
// (Gobierno__Capabilities__<cap>__BaseUrl/ApiKey), Gobierno:ApiKey, Gobierno:Alerts, la plantilla
// gobierno.tramite.radicado en Api.Notifications y —porque el flujo arranca procesos— el catálogo de procesos en
// Gobierno:Procesos:Archivo, con la v2 publicada en Api.Workflow (NP-1 de 009: tools/provisionar.procesos.json).
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);
builder.AddCorrelation();

builder.AddOrquestadorDeclarado(new SagaVocabulary("gobierno", "el trámite de gobierno"), typeof(Program).Assembly);
builder.AddOfertaDeGobierno(RastroDeTramites.De(builder.Configuration));

var app = builder.Build();
app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Gobierno:ApiKey"]);
app.MapFlujosDeclarados();
app.MapOfertas();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
