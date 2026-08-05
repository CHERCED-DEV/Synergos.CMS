using Microsoft.Extensions.Options;
using Synergos.Api.Catalog.Domain;
using Synergos.Api.Catalog.Endpoints;
using Synergos.Api.Catalog.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Catalog — el índice de lo publicable, y su búsqueda.
//
// AGNÓSTICA por las FACETAS: pares de texto libre, no un esquema por dominio. Es
// lo que permite que el mismo índice sirva a inmuebles (barrio, habitaciones), a
// cursos (nivel, modalidad) y a trámites (entidad). Un esquema tipado la habría
// obligado a conocerlos.
//
// Y lo que NO guarda, también a propósito: precio, existencias, disponibilidad.
// Son de Pricing, Inventory y Booking; copiarlos acá crearía dos verdades que se
// desincronizan, con el catálogo mostrando un precio que la caja no cobra.
//
// Absorbe el vertical "Search" del mapa F3: buscar no era una capacidad aparte.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.Configure<CatalogStorageOptions>(builder.Configuration.GetSection("Catalog:Storage"));
builder.Services.AddSingleton<ICatalogStore, FileSystemCatalogStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<CatalogStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CatalogService>();

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Catalog:ApiKey"]);
app.MapCatalogEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
