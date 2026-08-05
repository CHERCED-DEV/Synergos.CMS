using Microsoft.Extensions.Options;
using Synergos.Api.Pricing.Domain;
using Synergos.Api.Pricing.Endpoints;
using Synergos.Api.Pricing.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Pricing — cuánto cuesta algo, y cuánto se cobra al final.
//
// AGNÓSTICA: pone precio a un Ref. No sabe si es un producto, un curso, una
// noche de hotel o una tasa de trámite.
//
// Es dueña de una decisión que se equivoca fácil y se paga en cada factura: el
// DESCUENTO VA ANTES DEL IMPUESTO. Al revés se cobra impuesto sobre plata que
// nadie pagó.
//
// Y cotizar NO guarda nada: una cotización es una lectura. Lo que se persiste es
// el pedido, y eso es de Api.Orders.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.Configure<PricingStorageOptions>(builder.Configuration.GetSection("Pricing:Storage"));
builder.Services.AddSingleton<IPriceStore, FileSystemPriceStore>();
builder.Services.AddSingleton<IPromotionStore, FileSystemPromotionStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<PricingStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PricingService>();

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Pricing:ApiKey"]);
app.MapPricingEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
