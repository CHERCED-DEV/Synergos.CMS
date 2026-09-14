using Microsoft.Extensions.Options;
using Synergos.Api.Cart.Domain;
using Synergos.Api.Cart.Endpoints;
using Synergos.Api.Cart.Storage;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Cart — la canasta efímera.
//
// Es capacidad aparte de Orders porque su ciclo de vida no se parece en nada:
// una canasta se abandona —la gran mayoría lo son—, vence sola y nadie la
// audita. Un pedido se conserva, se reclama y se factura. Juntarlas obligaría al
// almacén de pedidos a cargar con millones de intenciones que nunca fueron nada.
//
// NO guarda precios: guarda qué y cuánto. El cuánto cuesta lo contesta
// Api.Pricing en el momento de mirar; congelarlo acá lo haría envejecer dentro
// de la canasta y alguien pagaría un precio que ya no existe.
//
// DE QUIEN ES LA CANASTA se comprueba, no se cree (HU #14): abrir acepta
// X-Synergos-Identity, lo verifica EN LOCAL y guarda con qué se afirmó. Local y no
// preguntandole a Api.Identity, porque una capacidad no llama a otra y porque eso
// la volveria el punto unico de fallo de las veinte.
//
// Y NO llama a Orders al cerrar: si lo hiciera tendría que conocer el modelo de
// pedido de cada negocio. Devuelve la canasta cerrada; el orden de los pasos es
// del BFF.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.Configure<CartStorageOptions>(builder.Configuration.GetSection("Cart:Storage"));
builder.Services.AddSingleton<ICartStore, FileSystemCartStore>();
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<CartStorageOptions>>().Value.Root));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<CartService>();

// El verificador de tokens de identidad (HU #14). NO obligatorio: un clon limpio arranca sin
// llave y la canasta sigue abriéndose con CmsSession, que es lo que hacía siempre.
//
// Lo que NO pasa sin llave es aceptar un token a ciegas: si alguien presenta uno y este servicio
// no puede comprobarlo, se RECHAZA. Ignorarlo sería peor que no aceptar tokens.
builder.AddIdentityTokens(required: false);

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Cart:ApiKey"]);
app.MapCartEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
