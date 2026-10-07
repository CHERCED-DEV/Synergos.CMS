using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Eventos.Clients;
using Synergos.Bff.Eventos.Domain;
using Synergos.Bff.Eventos.Endpoints;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Bff.Eventos — el tercer orquestador, y el que comprueba que las
// capacidades son agnósticas de verdad.
//
// NO NECESITÓ NI UNA CAPACIDAD NUEVA NI UN ENDPOINT NUEVO. Apartar aforo es
// Api.Inventory tal cual la dejó la tienda; cobrar es Api.Payments tal cual.
// Un dominio distinto entrando sin tocar nada es la diferencia entre «agnóstica»
// y «agnóstica hasta el segundo caso».
//
// Tres capacidades contra las seis de Tienda y las cuatro de Salud, y lo que
// NO tiene es lo que lo define: sin Api.Orders y sin Api.Fulfillment, porque una
// entrada no se despacha. El artefacto —el e-ticket con su QR— lo emite el CMS
// después de que esto conteste que sí: el firmante vive allá, y un orquestador
// que emitiera artefactos tendría estado propio más allá de sus sagas.
//
// LA DECISIÓN QUE TRAÍA LA HU #35: butaca nominada frente a cupo general. Las
// dos son el MISMO pozo contable y Api.Inventory no distingue — la granularidad
// va en el identificador del sujeto (evento/localidad, o evento/localidad/butaca
// con existencia 1). Ver AforoSubject.
//
// NO referencia ninguna Synergos.Api.*: habla con ellas por HTTP, como el CMS.
//
// ANTES DE DESPLEGAR: el aviso de compensación colgada necesita DOS cosas que no
// se pueden inventar desde acá —
//   1. Eventos:Alerts:{ToKind,ToId,Address} — a quién se le avisa.
//   2. La plantilla que pide —Eventos:Alerts:TemplateKey, o bff.compensacion.colgada
//      si no se configura, que es lo que hacen los cuatro— publicada en
//      Api.Notifications. NO se autora a mano: la publica tools/provisionar.sh
//      desde tools/provisionar.plantillas.json, y --verificar sale rojo si
//      falta (#174). Una clave propia hay que declararla en ese fichero.
// Sin las dos, una compensación rendida queda visible en /v1/compensations con
// alertedAtUtc en nulo y un error en el log que nombra lo que falta.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los procesos (HU #28).
builder.AddCorrelation();

builder.AddSagaMachinery<TicketingSaga, EventosCompensationExecutor>(
    // En minúscula porque es el prefijo de los códigos de rechazo (eventos.purchase_not_found).
    // Sirve además de raíz de configuración: las claves de IConfiguration no distinguen
    // mayúsculas, así que "eventos:Alerts" encuentra "Eventos:Alerts" del appsettings.
    new SagaVocabulary("eventos", "la compra de entradas"),
    EventosCapabilities.Pricing, EventosCapabilities.Inventory, EventosCapabilities.Payments);

builder.Services.AddSingleton<EventosCapabilities>();
builder.Services.AddSingleton<TicketingFlow>();

// La compra se DECLARA en flujos/eventos.compra.json y se valida AL ARRANCAR (ADR 0140): un tipo
// de paso que nadie registró, una lectura sin escritura previa o una reserva sin quien la consuma
// tumban el proceso aquí, no en la primera compra. Se valida contra el MISMO registro de pasos que
// arma TicketingFlow — de una sola fábrica, para que las dos listas no puedan desviarse.
builder.Services.AddSingleton<IRegistroDePasos>(sp => EventosPasos.Registro(sp.GetRequiredService<EventosCapabilities>()));
builder.Services.AddFlows(EventosFlujos.Compra);

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Eventos:ApiKey"]);
app.MapEventosEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
