using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Bff.Reservas.Domain;
using Synergos.Bff.Reservas.Endpoints;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Bff.Reservas — la reserva de un servicio por franja con cobro (011-booking-reserva), el primer
// orquestador de producto del CASO COMÚN del núcleo (N1): su flujo es flujos/reservas.servicio.json y lo
// sirve el host genérico. No escribe saga, enlace, fachada ni adaptadores.
//
// Lo que NO es del caso común queda aparte y atribuido: los dos pasos interinos (la clase fija del
// servicio, NR-1; el largo de la franja, NB-1) y la oferta de los servicios (P12, NB-8).
//
// ANTES DE DESPLEGAR: los destinos de booking, pricing, payments y notifications
// (Reservas__Capabilities__<cap>__BaseUrl/ApiKey), Reservas:ApiKey, Reservas:Alerts y la plantilla
// reservas.servicio.confirmada publicada en Api.Notifications (tools/provisionar.plantillas.json).
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);
builder.AddCorrelation();

var ofertas = RastroDeOfertas.De(builder.Configuration);
builder.AddOrquestadorDeclarado(new SagaVocabulary("reservas", "la reserva de un servicio"), typeof(Program).Assembly,
    new PasoServicio(), new PasoDuracion(ofertas));
builder.AddOfertaDeReservas(ofertas);

var app = builder.Build();
app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Reservas:ApiKey"]);
app.MapFlujosDeclarados();
app.MapOfertas();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
