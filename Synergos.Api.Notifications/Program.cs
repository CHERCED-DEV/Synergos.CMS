using Microsoft.Extensions.Options;
using Synergos.Api.Notifications.Domain;
using Synergos.Api.Notifications.Endpoints;
using Synergos.Api.Notifications.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Notifications — avisos salientes: sistema → persona, una vía.
//
// NO es Messaging. Messaging es humano↔humano y bidireccional; comparten la
// palabra "mensaje" y nada más — distinto almacén, distinta retención, distinto
// modo de fallo (doc 07 §2).
//
// AGNÓSTICA porque la plantilla vive acá y el TEXTO lo escribe el dominio: sabe
// rellenar marcadores y entregar, no sabe qué es una cita. Si el texto viviera
// cableado por caso de uso, la primera plantilla clínica la habría atado a Salud.
//
// El transporte es una costura (INotificationSender). Por defecto registra y
// avisa a gritos de que no salió: un transporte silencioso que dice "entregado"
// sin entregar es la forma más cara de descubrir en producción que nadie
// configuró el correo.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<NotificationStorageOptions>(builder.Configuration.GetSection("Notifications:Storage"));
builder.Services.AddSingleton<ITemplateStore, FileSystemTemplateStore>();
builder.Services.AddSingleton<IDeliveryStore, FileSystemDeliveryStore>();
builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<NotificationStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<NotificationService>();

var app = builder.Build();

app.UseSharedKeyAuth(app.Configuration["Notifications:ApiKey"]);
app.MapNotificationEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
