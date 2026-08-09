using Microsoft.Extensions.Options;
using Synergos.Api.Identity.Domain;
using Synergos.Api.Identity.Endpoints;
using Synergos.Api.Identity.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Identity — quién puede actuar y qué puede hacer.
//
// La usan los NUEVE dominios (doc 08 §2), y por eso es agnóstica hasta de lo que
// parece obvio: no guarda nombre ni correo. Contesta "¿quién es y qué puede
// hacer?", no "¿cómo se llama?" — los datos personales viven donde el dominio
// decida, con su régimen de consentimiento, y así el derecho al olvido no obliga
// a borrar la identidad que la bitácora todavía necesita para ser legible.
//
// Habla "principal" y no "usuario": un trabajo programado también actúa, y la
// bitácora necesita poder nombrarlo.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.Configure<IdentityStorageOptions>(builder.Configuration.GetSection("Identity:Storage"));
builder.Services.AddSingleton<IPrincipalStore, FileSystemPrincipalStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<IdentityStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IdentityService>();

// Los tokens de identidad (HU #14). La llave de firma NO es la compartida: mezclarlas
// haría que quien puede llamar a un servicio pudiera además fabricar identidades.
// Obligatoria acá: un emisor que no puede firmar no sirve de nada.
builder.AddIdentityTokens(required: true);

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Identity:ApiKey"]);
app.MapIdentityEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
