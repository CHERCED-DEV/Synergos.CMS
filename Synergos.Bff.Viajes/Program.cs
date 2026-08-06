using Synergos.Bff.Core;
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
//   2. La plantilla configurada en Viajes:Alerts:TemplateKey autorada en
//      Api.Notifications, usando SOLO los marcadores {saga}, {origen}, {desde}
//      y {pendientes}.
// Sin las dos, una compensación rendida queda visible en /v1/compensations con
// alertedAtUtc en nulo y un error en el log que nombra lo que falta.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una reserva por los procesos (HU #28).
builder.AddCorrelation();

builder.AddSagaMachinery<TripSaga, ViajesCompensationExecutor>(
    // En minúscula porque es el prefijo de los códigos de rechazo (viajes.trip_not_found).
    // Sirve además de raíz de configuración: las claves de IConfiguration no distinguen
    // mayúsculas, así que "viajes:Alerts" encuentra "Viajes:Alerts" del appsettings.
    new SagaVocabulary("viajes", "la reserva de viaje"),
    ViajesCapabilities.Pricing, ViajesCapabilities.Booking, ViajesCapabilities.Payments);

builder.Services.AddSingleton<ViajesCapabilities>();
builder.Services.AddSingleton<TripFlow>();

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Viajes:ApiKey"]);
app.MapViajesEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
