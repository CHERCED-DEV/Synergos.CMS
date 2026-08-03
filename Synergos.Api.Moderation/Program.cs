using Microsoft.Extensions.Options;
using Synergos.Api.Moderation.Domain;
using Synergos.Api.Moderation.Endpoints;
using Synergos.Api.Moderation.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Moderation — la cola de revisión y su decisión.
//
// AGNÓSTICA porque el objetivo es un Ref: modera un comentario, un documento, un
// perfil o lo que venga. Y NO oculta nada por su cuenta — registra la decisión, y
// ejecutarla es del BFF. Si ocultara directamente tendría que conocer dónde vive
// cada cosa moderable, y ahí dejaría de servir a la siguiente.
//
// Quién decidió y por qué son OBLIGATORIOS: una moderación anónima y sin motivo
// es indistinguible de un borrado arbitrario, y es lo primero que se pide cuando
// alguien reclama.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ModerationStorageOptions>(builder.Configuration.GetSection("Moderation:Storage"));
builder.Services.AddSingleton<IModerationStore, FileSystemModerationStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<ModerationStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ModerationService>();

var app = builder.Build();

app.UseSharedKeyAuth(app.Configuration["Moderation:ApiKey"]);
app.MapModerationEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
