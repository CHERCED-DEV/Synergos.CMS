using Synergos.Bff.Alquiler.Clients;
using Synergos.Bff.Alquiler.Domain;
using Synergos.Bff.Alquiler.Endpoints;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Bff.Pasos.Http;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Bff.Alquiler — el QUINTO orquestador (HU #147, piloto 2 de la fábrica).
//
// LAS TRES PREGUNTAS, contestadas por separado (doc 12 §4) y no mirando «cuántos
// pasos compone», que es el error que §11 documenta cuatro veces:
//
//   ¿hay algo que DESHACER?        sí — la ventana apartada y la plata retenida.
//   ¿el recurso lo lleva OTRO?     sí — la disponibilidad del equipo es Api.Booking.
//   ¿quién tiene la PLATA?         este orquestador, con Synergos:Alquiler:Mode=Bff.
//
// LO QUE ESTE VERTICAL ESTRENA, y por eso hizo falta un orquestador nuevo en vez
// de reusar uno: DOS COBROS CON VIDAS DISTINTAS. El alquiler se captura al
// reservar; la GARANTÍA se autoriza y se queda sin capturar mientras el equipo
// está fuera, para anularse al devolver —el caso normal— o capturarse por el
// daño. Los cuatro flujos anteriores autorizan para capturar y ninguno tenía ese
// estado intermedio, que es el que obliga a distinguir VoidPayment de
// RefundPayment en la compensación.
//
// Y LA SAGA SE CIERRA AL RESERVAR. Un alquiler dura días; una saga que siguiera
// `Running` hasta la devolución la daría por muerta el barrido a los
// Sweep:AbandonAfterMinutes — soltando la ventana y anulando la garantía de un
// alquiler VIVO, sin que nada fallara. Devolver y cancelar son operaciones sobre
// una saga ya liquidada, no pasos suyos.
//
// NO referencia ninguna Synergos.Api.*: habla con ellas por HTTP, como el CMS.
//
// ANTES DE DESPLEGAR: el aviso de compensación colgada necesita DOS cosas que no
// se pueden inventar desde acá —
//   1. Alquiler:Alerts:{ToKind,ToId,Address} — a quién se le avisa.
//   2. La plantilla de Api.Notifications con SOLO los marcadores {saga}, {origen},
//      {desde} y {pendientes}.
// Sin las dos, una compensación rendida queda en /v1/compensations con
// alertedAtUtc en nulo: degrada, no rompe, y nadie se entera.
//
// LA RENTA DECLARADA (ADR 0140, línea 005-alquiler-renta). flujos/alquiler.renta.json
// la declara y el host genérico la sirve en las rutas de la puerta
// (/v1/flujos/alquiler.renta/…). RentalFlow se queda en /v1/rentals*, que es lo
// que usa la ruta directa del CMS: dos motores en rutas distintas, y el
// interruptor alquiler:Flujos:Modo dice si corre el declarado («declarado» por
// defecto en las rutas de la puerta: lote 2, matiz 1).
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir un alquiler por los procesos (HU #28).
builder.AddCorrelation();

// En minúscula porque es el prefijo de los códigos de rechazo (alquiler.rental_not_found),
// y sirve además de raíz de configuración: las claves de IConfiguration no distinguen
// mayúsculas, así que "alquiler:Alerts" encuentra "Alquiler:Alerts" del appsettings.
var vocabulario = new SagaVocabulary("alquiler", "el alquiler de equipos");

builder.AddSagaMachinery<RentalSaga, AlquilerCompensationExecutor>(
    vocabulario, AlquilerCapabilities.Booking, AlquilerCapabilities.Payments, AlquilerCapabilities.Pricing);

builder.Services.AddSingleton<AlquilerCapabilities>();
builder.Services.AddSingleton<RentalFlow>();

// El huso del despliegue: qué día es hoy para la regla de cancelar (línea 005, T019). El mismo valor por defecto
// que el CMS (Synergos:Alquiler:TimeZone); uno que este sistema no conozca no arranca.
builder.Services.AddSingleton(HusoDelAlquiler.De(builder.Configuration["Alquiler:TimeZone"]));

// La oferta de cada equipo (línea 005): lo que la renta por la puerta cotiza en vez de un total del navegador.
builder.Services.AddSingleton<OfertaDeAlquiler>();

var declarado = ModosDelOrquestador.Leer(builder.Configuration, vocabulario, ModoDelOrquestador.Declarado) == ModoDelOrquestador.Declarado;
if (declarado)
{
    builder.AddOrquestadorDeclarado(vocabulario, typeof(Program).Assembly);

    // El tramo (la regla del alquiler) es el paso propio de la renta. La garantía se conserva con el paso común
    // (payments.conservar, NN-4) y «mis alquileres» lee la lista genérica del dueño («listar», NN-7). La cancelación de
    // lo confirmado (alquiler.cancelacion, NN-8) lee la original con flow.leer-original —este registro reemplaza al del
    // núcleo, así que lo nombra— y suma su regla del día de inicio.
    var maxDias = builder.Configuration.GetValue("Alquiler:MaxRentalDays", 30);
    builder.Services.AddSingleton<IRegistroDePasos>(sp => PasosComunes.Registro(
        sp.GetRequiredService<PuertosHttp>(),
        [
            new PasoLeerOriginal(sp.GetRequiredService<ISagaStore<SagaDeFlujo>>(), sp.GetRequiredService<TiposDeValor>()),
            .. AlquilerPasos.Propios(sp.GetRequiredService<OfertaDeAlquiler>(), maxDias),
            .. CancelacionDeAlquiler.Pasos(sp.GetRequiredService<LiquidacionDeAlquiler>()),
        ]));

    // Lo que pasa después del cierre fuera de la puerta: declarar, confirmar y la cancelación propia, que el CMS sigue
    // eligiendo detrás de su interruptor (lote 3, 005-P9; D-005-NN8).
    builder.Services.AddSingleton<LiquidacionDeAlquiler>();
}

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Alquiler:ApiKey"]);
app.MapAlquilerEndpoints();
if (declarado)
{
    app.MapFlujosDeclarados();
    app.MapLiquidacionDeAlquiler();
}
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
