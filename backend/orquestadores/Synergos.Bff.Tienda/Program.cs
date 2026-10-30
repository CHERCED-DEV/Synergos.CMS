using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
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
//   2. La plantilla que pide —Tienda:Alerts:TemplateKey, o bff.compensacion.colgada
//      si no se configura, que es lo que hacen los cuatro— publicada en
//      Api.Notifications. NO se autora a mano: la publica tools/provisionar.sh
//      desde tools/provisionar.plantillas.json, y --verificar sale rojo si
//      falta (#174). Una clave propia hay que declararla en ese fichero.
// Sin las dos, una compensación rendida queda visible en /v1/compensations con
// alertedAtUtc en nulo y un error en el log que nombra lo que falta.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

// En minúscula porque es el prefijo de los códigos de rechazo (tienda.saga_not_found).
// Sirve además de raíz de configuración: las claves de IConfiguration no distinguen
// mayúsculas, así que "tienda:Alerts" encuentra "Tienda:Alerts" del appsettings.
var vocabulario = new SagaVocabulary("tienda", "la compra");

builder.AddSagaMachinery<PurchaseSaga, TiendaCompensationExecutor>(
    vocabulario,
    TiendaCapabilities.Cart, TiendaCapabilities.Pricing, TiendaCapabilities.Inventory,
    TiendaCapabilities.Orders, TiendaCapabilities.Payments, TiendaCapabilities.Fulfillment);

builder.Services.AddSingleton<TiendaCapabilities>();
builder.Services.AddSingleton<PurchaseFlow>();

// La oferta del catálogo (004-tienda-compra, R-9): el CMS publica precio y existencias de cada producto y
// esto los deja en Pricing e Inventory, con los sujetos de las dos compras (la de la puerta y la de arriba).
builder.Services.AddSingleton<OfertaDeTienda>();

// La compra por la puerta (ADR 0140): flujos/tienda.compra.json sobre el host genérico, en sus propias
// rutas (/v1/flujos/tienda.compra…), que sólo sirve el flujo declarado; /v1/purchases sigue siendo del
// motor imperativo de arriba, que el CMS llama. Por eso el modo arranca en «declarado» (lote 2, matiz
// 1): «imperativo» apaga la puerta de la tienda sin tocar el otro camino.
var declarado = ModosDelOrquestador.Leer(builder.Configuration, vocabulario, ModoDelOrquestador.Declarado) == ModoDelOrquestador.Declarado;
if (declarado)
{
    builder.AddOrquestadorDeclarado(vocabulario, typeof(Program).Assembly, new PasoRevisarLineas());

    // Interino de N-12: los tipos del host más las líneas del pedido (TipoLineasDePedido).
    builder.Services.AddSingleton(TiposDeValor.Con(new TipoContacto(), new TipoDireccion(), new TipoLineasDePedido()));

    // El tope de códigos de promoción que no sirven por comprador (US 8; copia declarada del de Eventos hasta
    // N-11): «cotizar» y «abrir» con código contestan distinto a uno que no existe, a uno vencido y a uno que
    // vale. Tienda:Cupon:FallosPorVentana (10) y Tienda:Cupon:Ventana (00:15:00) los ajusta el despliegue.
    builder.Services.AddSingleton(sp =>
    {
        var configuracion = sp.GetRequiredService<IConfiguration>();
        return new IntentosDeCodigo(
            sp.GetRequiredService<TimeProvider>(),
            configuracion.GetValue("Tienda:Cupon:FallosPorVentana", IntentosDeCodigo.TopePorDefecto),
            configuracion.GetValue("Tienda:Cupon:Ventana", IntentosDeCodigo.VentanaPorDefecto));
    });
}

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Tienda:ApiKey"]);
app.MapTiendaEndpoints();
// Las rutas del flujo las pone el host genérico; el cupón (recortar el código y frenar los que no sirven) se les
// cuelga como filtro de un grupo sin prefijo —las rutas no cambian—, porque la línea no toca el mapeo del núcleo.
if (declarado) app.MapGroup(string.Empty).AddEndpointFilter<CuponDeLaCompra>().MapFlujosDeclarados();

// La lectura del envío que pide el CMS para el personal (013-seller-operaciones, interino de NS-2): lee las sagas del
// flujo declarado, así que existe con él, y no lleva marca de la puerta. Va en su bloque y no en una línea
// «if (declarado) app.…» porque no es del coste de declarar la compra (MetricaDeLaCompraDeTiendaTests): la medida de
// 013 (SC-007) la cuenta atribuida a NS-2.
if (declarado)
{
    app.MapLecturaDelEnvio();
}
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
