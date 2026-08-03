using Microsoft.Extensions.Options;
using Synergos.Api.Orders.Domain;
using Synergos.Api.Orders.Endpoints;
using Synergos.Api.Orders.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Orders — el pedido y su ciclo de vida.
//
// NO tiene estado "pagado". El cobro es de Api.Payments y se enlaza por
// referencia, porque UN PEDIDO PUEDE EXISTIR SIN COBRO: un trámite gubernamental
// se radica y puede pagarse después, o no pagarse nunca. Meter el pago acá
// obligaría a Gov a arrastrar semántica de tienda — que es exactamente el nudo
// que el mapa F3 midió y esta separación deshace.
//
// El precio SÍ se congela en la línea, al revés que en la canasta: un pedido es
// un acuerdo sobre un monto, y si se releyera del catálogo, la factura cambiaría
// sola cada vez que alguien sube un precio.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<OrderStorageOptions>(builder.Configuration.GetSection("Orders:Storage"));
builder.Services.AddSingleton<IOrderStore, FileSystemOrderStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<OrderStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<OrderService>();

var app = builder.Build();

app.UseSharedKeyAuth(app.Configuration["Orders:ApiKey"]);
app.MapOrderEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
