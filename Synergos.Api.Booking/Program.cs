using Synergos.Api.Booking.Domain;
using Synergos.Api.Booking.Endpoints;
using Synergos.Api.Booking.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Booking — la capacidad de reservar tiempo sobre un recurso.
//
// AGNÓSTICA: sabe de recurso, ventana, cupo, hold y política de cancelación. NO
// sabe qué es un médico, una habitación ni un aula — eso viaja como un Ref opaco
// que esta API guarda y devuelve, y NUNCA interpreta (doc 07 §3).
//
// La misma capacidad sirve a una agenda clínica (con horario de atención) y a una
// noche de hotel (que cruza la medianoche): un recurso sin horario declarado está
// siempre abierto. Ver Domain/Resource.cs.
//
// Este Program.cs es el molde que copian las demás (doc 08 §4): arranque y nada
// más — DI, middleware, Map*Endpoints().
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<BookingStorageOptions>(builder.Configuration.GetSection("Booking:Storage"));
builder.Services.AddSingleton<IResourceStore, FileSystemResourceStore>();
builder.Services.AddSingleton<IHoldStore, FileSystemHoldStore>();
builder.Services.AddSingleton<IReservationStore, FileSystemReservationStore>();
builder.Services.AddSingleton<IIdempotencyLedger, FileSystemIdempotencyStore>();

// El reloj se inyecta: los bordes temporales —el hold que vence justo, la
// cancelación en el límite del plazo— son la mitad de los errores de una agenda, y
// sin reloj inyectable no se reproducen.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BookingService>();

var app = builder.Build();

app.UseSharedKeyAuth(app.Configuration["Booking:ApiKey"]);

app.MapBookingEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
