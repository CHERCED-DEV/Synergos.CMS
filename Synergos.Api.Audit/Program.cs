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

// Verificar identidad, para que un asiento diga quién actuó DE VERDAD (#72). `required: false`
// es deliberado: sin llave se sigue auditando —el asiento dirá CmsSession, que es la verdad— y
// un token presentado donde no se puede comprobar se RECHAZA, no se ignora. Parar la bitácora
// cuando falla la identidad convertiría una caída en un hueco en el registro, que es peor que
// un asiento débil.
builder.AddIdentityTokens(required: false);

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
