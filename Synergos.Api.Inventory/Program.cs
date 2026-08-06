using Microsoft.Extensions.Options;
using Synergos.Api.Inventory.Domain;
using Synergos.Api.Inventory.Endpoints;
using Synergos.Api.Inventory.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Inventory — existencias, apartados y unidades nombradas.
//
// Es capacidad aparte de Booking porque Booking es TIEMPO —un recurso ocupado de
// 10:00 a 10:30— y esto es CANTIDAD —quedan tres—. Parecen lo mismo y no lo son:
// sus índices y sus condiciones de carrera no se parecen en nada.
//
// Absorbe lo que iba a ser "Api.Seating" (doc 08 §3): un asiento es una unidad de
// aforo CON COORDENADAS. La geometría es dato, no capacidad — y esta API no la
// interpreta, la guarda y la devuelve.
//
// El apartado con TTL es el mismo patrón del hold de Booking y de la autorización
// de Payments: primero el paso reversible, después el que cuesta.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.Configure<InventoryStorageOptions>(builder.Configuration.GetSection("Inventory:Storage"));
builder.Services.AddSingleton<IStockStore, FileSystemStockStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<InventoryStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<InventoryService>();

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Inventory:ApiKey"]);
app.MapInventoryEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
