using Microsoft.Extensions.Options;
using Synergos.Api.Geo.Domain;
using Synergos.Api.Geo.Endpoints;
using Synergos.Api.Geo.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Geo — dónde está algo, y qué hay cerca.
//
// AGNÓSTICA: ubica un Ref. No sabe si es un inmueble, una sede de trámites o un
// hotel — y por eso Realty, Travel, Shop y Booking la comparten.
//
// Guarda GRADOS DECIMALES y no direcciones: "Cra 70 #44-30" y "Carrera 70 No
// 44-30" son el mismo sitio y ninguna consulta lo sabría. Geocodificar es un
// servicio externo; su resultado es lo que se guarda acá.
//
// Y mide con el semiverseno, no con Pitágoras sobre grados: un grado de longitud
// mide 111 km en el ecuador y 78 en Medellín.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GeoStorageOptions>(builder.Configuration.GetSection("Geo:Storage"));
builder.Services.AddSingleton<IPlaceStore, FileSystemPlaceStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<GeoStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GeoService>();

var app = builder.Build();

app.UseSharedKeyAuth(app.Configuration["Geo:ApiKey"]);
app.MapGeoEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
