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
// Qué proveedor cobra — `Payments:Provider` (HU #27).
//
//   (vacío) / "logging"  → LoggingPaymentProvider. Dice que sí a todo y lo grita. Es el default
//                          de desarrollo: un clon limpio corre el flujo sin cuenta de pasarela.
//   cualquier otro       → se cobra de verdad con ESE, y si le falta la credencial se registra
//                          NotConfiguredPaymentProvider, que RECHAZA cada cobro a gritos.
//
// La tercera opción —el nombre puesto y el stub sirviendo en silencio— es justo el defecto que
// el CMS ya sufrió, y por eso no existe: o cobra, o dice a gritos que no puede.
builder.Services.AddSingleton<IPaymentProvider>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var pedido = (cfg["Payments:Provider"] ?? string.Empty).Trim();

    if (pedido.Length == 0 || string.Equals(pedido, "logging", StringComparison.OrdinalIgnoreCase))
    {
        return new LoggingPaymentProvider(sp.GetRequiredService<ILogger<LoggingPaymentProvider>>());
    }

    // Un proveedor de verdad necesita, como mínimo, con qué autenticarse. Sin eso no hay
    // integración que valga: se registra el que rechaza y grita.
    var llave = cfg[$"Payments:{pedido}:ApiKey"];
    var falta = string.IsNullOrWhiteSpace(llave) ? $"Payments:{pedido}:ApiKey" : null;

    if (falta is not null)
    {
        return new NotConfiguredPaymentProvider(
            pedido, falta, sp.GetRequiredService<ILogger<NotConfiguredPaymentProvider>>());
    }

    // Acá va el adaptador real cuando exista la cuenta comercial. Mientras tanto, pedir un
    // proveedor CON credencial y no tener adaptador es un defecto de despliegue, no un motivo
    // para caer al stub en silencio.
    return new NotConfiguredPaymentProvider(
        pedido, $"el adaptador de '{pedido}' (todavía no implementado)",
        sp.GetRequiredService<ILogger<NotConfiguredPaymentProvider>>());
});
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
