using Microsoft.Extensions.Options;
using Synergos.Api.Payments.Domain;
using Synergos.Api.Payments.Endpoints;
using Synergos.Api.Payments.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Payments — autorizar, capturar, liberar y devolver.
//
// AUTORIZAR Y CAPTURAR ESTÁN SEPARADOS, y es la decisión que sostiene todo el
// resto: autorizar reserva cupo en el medio de pago —reversible y barato—,
// capturar mueve la plata. Es el mismo razonamiento del hold de Api.Booking:
// primero el paso reversible, después el que cuesta. Es lo que permite que un
// flujo que cruza capacidades falle sin dejar plata mal cobrada.
//
// El proveedor es una costura (IPaymentProvider). Por defecto registra, autoriza
// todo y AVISA A GRITOS en cada operación: el CMS ya tuvo el defecto de
// Provider=Wompi sirviendo el stub en silencio, y costó una investigación.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PaymentStorageOptions>(builder.Configuration.GetSection("Payments:Storage"));
builder.Services.AddSingleton<IPaymentStore, FileSystemPaymentStore>();
builder.Services.AddSingleton<IPaymentProvider, LoggingPaymentProvider>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<PaymentStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PaymentService>();

var app = builder.Build();

app.UseSharedKeyAuth(app.Configuration["Payments:ApiKey"]);
app.MapPaymentEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
