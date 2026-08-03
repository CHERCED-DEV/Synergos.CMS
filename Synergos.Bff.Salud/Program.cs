using Synergos.Bff.Core;
using Synergos.Bff.Salud.Clients;
using Synergos.Bff.Salud.Domain;
using Synergos.Bff.Salud.Endpoints;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Bff.Salud — el primer orquestador.
//
// Lo que aporta y NINGUNA capacidad puede aportar: el ORDEN y la COMPENSACIÓN.
// Booking sabe apartar, Payments sabe cobrar, Consent sabe si hay permiso — y
// ninguno de los tres sabe que el permiso va antes del cupo, que el cupo va antes
// del cobro, y que si la confirmación falla hay que devolver la plata.
//
// NO referencia ninguna Synergos.Api.*: habla con ellas por HTTP, como el CMS.
//
// LA MÁQUINA DE SAGAS YA NO VIVE ACÁ. Vivió dentro mientras hubo un solo
// consumidor —la regla de CLAUDE.md §6— y con Bff.Tienda encima se promovió a
// Synergos.Bff.Core. Lo que quedó en este proyecto es lo único que no se puede
// compartir: el ORDEN de los pasos, y qué significa deshacer cada uno.
//
// ANTES DE DESPLEGAR: el aviso de compensación colgada necesita DOS cosas que no
// se pueden inventar desde acá —
//   1. Salud:Alerts:{ToKind,ToId,Address} — a quién se le avisa.
//   2. La plantilla configurada en Salud:Alerts:TemplateKey autorada en
//      Api.Notifications, usando SOLO los marcadores {saga}, {origen}, {desde}
//      y {pendientes}.
// Sin las dos, una compensación rendida queda visible en /v1/compensations con
// alertedAtUtc en nulo y un error en el log que nombra lo que falta. No hay
// seeder que las cree: CLAUDE.md §0.4 los prohíbe, y adivinar una dirección de
// guardia es peor que no mandar nada.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

builder.AddSagaMachinery<AppointmentSaga, SaludCompensationExecutor>(
    // En minúscula porque es el prefijo de los códigos de rechazo (salud.saga_not_found).
    // Sirve además de raíz de configuración: las claves de IConfiguration no distinguen
    // mayúsculas, así que "salud:Alerts" encuentra "Salud:Alerts" del appsettings.
    new SagaVocabulary("salud", "la cita"),
    SaludCapabilities.Consent, SaludCapabilities.Booking,
    SaludCapabilities.Pricing, SaludCapabilities.Payments);

builder.Services.AddSingleton<SaludCapabilities>();
builder.Services.AddSingleton<AppointmentFlow>();

var app = builder.Build();

app.UseSharedKeyAuth(app.Configuration["Salud:ApiKey"]);
app.MapSaludEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
