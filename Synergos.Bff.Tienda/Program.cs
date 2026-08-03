using Synergos.Bff.Core;
using Synergos.Bff.Tienda.Clients;
using Synergos.Bff.Tienda.Domain;
using Synergos.Bff.Tienda.Endpoints;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Bff.Tienda — el segundo orquestador, y el que justificó promover la
// máquina de sagas a Synergos.Bff.Core.
//
// Comparte con Salud solo tres capacidades —Pricing, Payments, Notifications— y
// suma Cart, Inventory, Orders y Fulfillment. Sobre todo NO tiene Booking: si el
// segundo BFF hubiera vuelto a apoyarse en holds de calendario, «la máquina de
// sagas se comparte» no habría querido decir nada. Acá el número de
// compensaciones ni siquiera es fijo: una compra de siete líneas lleva siete
// apartados de existencias, más el pedido, más el cobro.
//
// NO referencia ninguna Synergos.Api.*: habla con ellas por HTTP, como el CMS.
//
// ANTES DE DESPLEGAR: el aviso de compensación colgada necesita DOS cosas que no
// se pueden inventar desde acá —
//   1. Tienda:Alerts:{ToKind,ToId,Address} — a quién se le avisa.
//   2. La plantilla configurada en Tienda:Alerts:TemplateKey autorada en
//      Api.Notifications, usando SOLO los marcadores {saga}, {origen}, {desde}
//      y {pendientes}.
// Sin las dos, una compensación rendida queda visible en /v1/compensations con
// alertedAtUtc en nulo y un error en el log que nombra lo que falta.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.AddSagaMachinery<PurchaseSaga, TiendaCompensationExecutor>(
    // En minúscula porque es el prefijo de los códigos de rechazo (tienda.saga_not_found).
    // Sirve además de raíz de configuración: las claves de IConfiguration no distinguen
    // mayúsculas, así que "tienda:Alerts" encuentra "Tienda:Alerts" del appsettings.
    new SagaVocabulary("tienda", "la compra"),
    TiendaCapabilities.Cart, TiendaCapabilities.Pricing, TiendaCapabilities.Inventory,
    TiendaCapabilities.Orders, TiendaCapabilities.Payments, TiendaCapabilities.Fulfillment);

builder.Services.AddSingleton<TiendaCapabilities>();
builder.Services.AddSingleton<PurchaseFlow>();

var app = builder.Build();

app.UseSharedKeyAuth(app.Configuration["Tienda:ApiKey"]);
app.MapTiendaEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
