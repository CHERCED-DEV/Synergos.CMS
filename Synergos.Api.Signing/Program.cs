using Microsoft.Extensions.Options;
using Synergos.Api.Signing.Domain;
using Synergos.Api.Signing.Endpoints;
using Synergos.Api.Signing.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Signing — firmar y verificar, con llaves por propósito.
//
// Existe porque la misma firma HMAC está hoy escrita tres veces en el CMS
// —entradas, certificados y webhooks— con tres manejos de llave distintos.
//
// UNA LLAVE POR PROPÓSITO: con una sola para todo, quien consigue firmar una
// entrada de evento puede firmar un certificado gubernamental.
//
// Y retirar una llave NO invalida lo ya firmado: deja de emitir y sigue
// verificando. Si dejara de verificar, rotar invalidaría de golpe todas las
// entradas vendidas.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.Configure<SigningStorageOptions>(builder.Configuration.GetSection("Signing:Storage"));
builder.Services.AddSingleton<ISigningKeyStore, FileSystemSigningKeyStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<SigningStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<SigningService>();

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Signing:ApiKey"]);
app.MapSigningEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
