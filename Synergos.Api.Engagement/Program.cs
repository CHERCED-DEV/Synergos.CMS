using Microsoft.Extensions.Options;
using Synergos.Api.Engagement.Domain;
using Synergos.Api.Engagement.Endpoints;
using Synergos.Api.Engagement.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Engagement — un actor opina sobre un Ref.
//
// Es el hallazgo que más código ahorra del despiece (doc 08 §2): los comentarios
// de Social, las reseñas de Shop, las valoraciones de Academy y los favoritos de
// Realty estaban escritos CUATRO VECES con cuatro nombres. Son lo mismo; lo que
// cambia entre ellos son cuatro líneas de regla sobre qué campos exige cada clase.
//
// NO decide qué se oculta — eso es Api.Moderation. Solo sabe ocultar, y conserva
// la fila para que una decisión de moderación se pueda revisar o revertir.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.Configure<EngagementStorageOptions>(builder.Configuration.GetSection("Engagement:Storage"));
builder.Services.AddSingleton<IEngagementStore, FileSystemEngagementStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<EngagementStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<EngagementService>();

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Engagement:ApiKey"]);
app.MapEngagementEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
