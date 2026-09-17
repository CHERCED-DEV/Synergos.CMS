using Microsoft.Extensions.Options;
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

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

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

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Booking:ApiKey"]);

// Un solo escritor por capacidad, aunque corran varias réplicas (#112). Sube a proceso
// cruzado el `lock` que el servicio ya tenía dentro: con el almacén en un fichero por
// documento, lo que queda por serializar es leer-decidir-escribir sobre el MISMO.
app.UseStoreWriteGate(
    app.Services.GetRequiredService<IOptions<BookingStorageOptions>>().Value.Root,
    BookingRules.CodePrefix,
    app.Configuration.GetValue<int?>("Booking:Storage:WriteGateSeconds"));

app.MapBookingEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
