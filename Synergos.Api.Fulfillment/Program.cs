using Microsoft.Extensions.Options;
using Synergos.Api.Fulfillment.Domain;
using Synergos.Api.Fulfillment.Endpoints;
using Synergos.Api.Fulfillment.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Fulfillment — envíos, direcciones y rastro.
//
// Es capacidad aparte de Orders porque su ritmo y su almacén no se parecen: un
// pedido lo mueve la aplicación, un envío lo mueven eventos externos de la
// transportadora, que llegan tarde y desordenados. Fundirlas metería un
// integrador de terceros dentro del ciclo de vida del pedido.
//
// Los estados son los del TRANSPORTE, no los del pedido: un servicio prestado en
// sitio cumple el pedido sin que exista envío.
//
// Y los finales son finales: un evento tardío no hace retroceder el estado, o el
// cliente vería "en tránsito" un paquete que ya tiene en la mano.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FulfillmentStorageOptions>(builder.Configuration.GetSection("Fulfillment:Storage"));
builder.Services.AddSingleton<IShipmentStore, FileSystemShipmentStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<FulfillmentStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<FulfillmentService>();

var app = builder.Build();

app.UseSharedKeyAuth(app.Configuration["Fulfillment:ApiKey"]);
app.MapFulfillmentEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
