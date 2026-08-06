using Microsoft.Extensions.Options;
using Synergos.Api.Audit.Domain;
using Synergos.Api.Audit.Endpoints;
using Synergos.Api.Audit.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Audit — la bitácora append-only.
//
// AGNÓSTICA: registra que un Actor hizo una Action sobre un Ref. No sabe si el
// Ref es una historia clínica, un expediente o un pedido — y por eso puede
// servirle a los nueve dominios con el mismo régimen.
//
// La propiedad que la define: NO hay Update ni Delete, ni en la interfaz del
// almacén ni en el ruteo. Una bitácora editable no sirve de bitácora.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.Configure<AuditStorageOptions>(builder.Configuration.GetSection("Audit:Storage"));
builder.Services.AddSingleton<IAuditStore, FileSystemAuditStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<AuditStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<AuditService>();

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Audit:ApiKey"]);
app.MapAuditEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
