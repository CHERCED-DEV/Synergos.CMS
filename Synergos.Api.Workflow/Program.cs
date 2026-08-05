using Microsoft.Extensions.Options;
using Synergos.Api.Workflow.Domain;
using Synergos.Api.Workflow.Endpoints;
using Synergos.Api.Workflow.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Workflow — la máquina de estados genérica.
//
// Es la más reutilizable del catálogo y la que estaba copiada más veces: un
// trámite que avanza por estados, una ruta clínica, el cumplimiento de un pedido
// y una matrícula son LA MISMA MÁQUINA con distintos rótulos. Los rótulos los
// pone el dominio; las transiciones válidas y la historia las guarda esto.
//
// La definición es DATO, no código: si las transiciones fueran un switch en C#,
// agregar un estado a un trámite exigiría desplegar esta capacidad, y cada
// dominio esperaría turno para cambiar su propio proceso.
//
// Y la guarda por rol es lo que la hace servir a Gobierno: radicar lo hace el
// ciudadano y aprobar el funcionario.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.Configure<WorkflowStorageOptions>(builder.Configuration.GetSection("Workflow:Storage"));
builder.Services.AddSingleton<IDefinitionStore, FileSystemDefinitionStore>();
builder.Services.AddSingleton<IInstanceStore, FileSystemInstanceStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<WorkflowStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<WorkflowService>();

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Workflow:ApiKey"]);
app.MapWorkflowEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
