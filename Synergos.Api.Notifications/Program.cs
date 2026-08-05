using Microsoft.Extensions.Options;
using Synergos.Api.Notifications.Domain;
using Synergos.Api.Notifications.Endpoints;
using Synergos.Api.Notifications.Storage;
using Synergos.Api.Notifications.Transport;
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
// El transporte es una costura (INotificationSender). Con credenciales sale por
// Resend; sin ellas, el transporte por defecto RECHAZA y lo grita. No da nada
// por entregado: un transporte silencioso que dice "entregado" sin entregar es
// la forma más cara de descubrir en producción que nadie configuró el correo.
//
// El estado real de un envío llega DESPUÉS, por webhook, y puede llegar fuera de
// orden. Por eso /v1/webhooks/resend está exento de la llave compartida —quien
// lo llama es un tercero que no la tiene— y lo que lo protege es la firma.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.Configure<NotificationStorageOptions>(builder.Configuration.GetSection("Notifications:Storage"));
builder.Services.AddSingleton<ITemplateStore, FileSystemTemplateStore>();
builder.Services.AddSingleton<IDeliveryStore, FileSystemDeliveryStore>();
// El transporte se elige por configuración, y la elección se GRITA al arrancar:
// un despliegue que cree que manda correos y no manda es un fallo silencioso que
// solo se descubre cuando alguien reclama que nunca le llegó nada.
builder.Services.Configure<ResendOptions>(builder.Configuration.GetSection("Notifications:Resend"));
if (builder.Configuration.GetSection("Notifications:Resend")["ApiKey"] is { Length: > 0 })
{
    builder.Services.AddHttpClient<INotificationSender, ResendNotificationSender>();
}
else
{
    builder.Services.AddSingleton<INotificationSender, LoggingNotificationSender>();
}
builder.Services.AddSingleton<WebhookVerifier>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<NotificationStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<NotificationService>();

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Notifications:ApiKey"], "/v1/webhooks");
app.MapNotificationEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
