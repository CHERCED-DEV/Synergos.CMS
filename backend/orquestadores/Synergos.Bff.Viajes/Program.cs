using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Bff.Viajes.Clients;
using Synergos.Bff.Viajes.Domain;
using Synergos.Bff.Viajes.Endpoints;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Bff.Viajes — el cuarto orquestador, y el primero con VARIOS pasos
// reversibles heterogéneos.
//
// Los tres anteriores tenían un solo cupo que soltar (Salud), o varios del mismo
// tipo sobre el mismo pozo (Tienda, Eventos). Un viaje son un vuelo, dos noches
// de hotel y un auto: cuatro apartados sobre cuatro recursos con cuatro
// ventanas, y el fallo puede llegar en cualquiera — incluso después de haber
// confirmado los tres primeros. Eso es lo que convirtió a la HU #36 de cableado
// en orquestador, y lo dice el propio ticket.
//
// LA DECISIÓN QUE TRAÍA LA HU #36: «no todo va a Api.Booking; un asiento de vuelo
// se parece más a un pozo contable». Mirando el código y no la intuición, los
// CUATRO van a Api.Booking: su Resource ya lleva Capacity —«1 para un consultorio;
// 40 para un aula»— y su regla de «horario vacío = siempre abierto» se tomó
// nombrando el caso hotel. El vuelo se consideró para Api.Inventory y se descartó
// con el argumento escrito, y con el disparador para revisarlo: que haga falta
// sobreventa por clase tarifaria. Ver TravelSubject.
//
// Y LO DELICADO DE ESTE DOMINIO: la compensación del cupo cambia de carácter.
// Antes de confirmar, deshacer es «soltar el apartado»; después, es «cancelar la
// reserva», porque Api.Booking convirtió el apartado en otra cosa y rechaza
// soltarlo. La reescritura va DENTRO del bucle de confirmación, ítem por ítem.
//
// NO referencia ninguna Synergos.Api.*: habla con ellas por HTTP, como el CMS.
//
// ANTES DE DESPLEGAR: el aviso de compensación colgada necesita DOS cosas que no
// se pueden inventar desde acá —
//   1. Viajes:Alerts:{ToKind,ToId,Address} — a quién se le avisa.
//   2. La plantilla que pide —Viajes:Alerts:TemplateKey, o bff.compensacion.colgada
//      si no se configura, que es lo que hacen los cuatro— publicada en
//      Api.Notifications. NO se autora a mano: la publica tools/provisionar.sh
//      desde tools/provisionar.plantillas.json, y --verificar sale rojo si
//      falta (#174). Una clave propia hay que declararla en ese fichero.
// Sin las dos, una compensación rendida queda visible en /v1/compensations con
// alertedAtUtc en nulo y un error en el log que nombra lo que falta.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una reserva por los procesos (HU #28).
builder.AddCorrelation();

// En minúscula porque es el prefijo de los códigos de rechazo (viajes.trip_not_found).
// Sirve además de raíz de configuración: las claves de IConfiguration no distinguen
// mayúsculas, así que "viajes:Alerts" encuentra "Viajes:Alerts" del appsettings.
var vocabulario = new SagaVocabulary("viajes", "la reserva de viaje");

builder.AddSagaMachinery<TripSaga, ViajesCompensationExecutor>(
    vocabulario, ViajesCapabilities.Pricing, ViajesCapabilities.Booking, ViajesCapabilities.Payments);

builder.Services.AddSingleton<ViajesCapabilities>();
builder.Services.AddSingleton<TripFlow>();

// La reserva por la puerta (ADR 0140; línea 006, research R15): flujos/viajes.reserva.json sobre el host
// genérico, en sus propias rutas (/v1/flujos/viajes.reserva…), que sólo sirve el flujo declarado, con su
// saga en su carpeta. /v1/trips sigue siendo de TripFlow, que el CMS llama. Por eso el modo arranca en
// «declarado» (lote 2, matiz 1): «imperativo» apaga la puerta de Viajes sin tocar el otro camino.
var declarado = ModosDelOrquestador.Leer(builder.Configuration, vocabulario, ModoDelOrquestador.Declarado) == ModoDelOrquestador.Declarado;
if (declarado)
{
    // La confirmación parcial por la puerta (historia 3, NV-13): el flujo genérico le pregunta a la extensión del vertical si
    // suelta un ítem cuyo cierre falló, y ésta arma la devolución de su parte, que sabe deshacer el ejecutor común.
    builder.Services.AddSingleton<IExtensionDelFlujo, ExtensionDeViajes>();
    builder.Services.AddSingleton<IDeshaceUnTipo>(sp => new DevolverParteDelItem(sp.GetRequiredService<IDeshacerCobros>(), sp.GetRequiredService<TiposDeValor>()));
    builder.AddOrquestadorDeclarado(vocabulario, typeof(Program).Assembly, PasosDeViajes.Propios());

    // Los tipos del host más el itinerario del viaje, que la saga genérica guarda y la puerta devuelve.
    builder.Services.AddSingleton(TiposDeValor.Con(new TipoContacto(), new TipoDireccion(), new TipoFranjas(), new TipoItinerario()));
}

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Viajes:ApiKey"]);
app.MapViajesEndpoints();
if (declarado) app.MapFlujosDeclarados();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
