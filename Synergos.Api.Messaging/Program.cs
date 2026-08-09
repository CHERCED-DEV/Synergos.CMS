using Microsoft.Extensions.Options;
using Synergos.Api.Messaging.Domain;
using Synergos.Api.Messaging.Endpoints;
using Synergos.Api.Messaging.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Messaging — conversaciones humano↔humano.
//
// NO es Notifications: aquella es sistema→humano y de una vía. Comparten la
// palabra "mensaje" y nada más — distinto almacén, distinta retención, distinto
// modo de fallo.
//
// Y es la capacidad que más urgía partir: hoy UN SOLO IMessagingService sirve la
// in-basket clínica, la correspondencia gubernamental y los DMs sociales — tres
// regímenes regulatorios sobre un stub. Acá el transporte es uno, y quién puede
// leer, qué se conserva y por cuánto lo decide cada dominio.
//
// Los adjuntos son REFERENCIAS, no bytes: el binario vive en Api.Documents, con
// su lista blanca, su huella y sus enlaces firmados.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.Configure<MessagingStorageOptions>(builder.Configuration.GetSection("Messaging:Storage"));
builder.Services.AddSingleton<IThreadStore, FileSystemThreadStore>();
builder.Services.AddSingleton<IMessageStore, FileSystemMessageStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<MessagingStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<MessagingService>();

// El verificador de tokens de identidad (HU #14). NO obligatorio: un clon limpio arranca
// sin llave y el acuse sigue funcionando con CmsSession, que es lo que hacía siempre.
//
// Lo que NO pasa sin llave es aceptar un token a ciegas: si alguien presenta uno y este
// servicio no puede comprobarlo, se RECHAZA. Ignorarlo sería peor que no aceptar tokens.
builder.AddIdentityTokens(required: false);

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Messaging:ApiKey"]);
app.MapMessagingEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
