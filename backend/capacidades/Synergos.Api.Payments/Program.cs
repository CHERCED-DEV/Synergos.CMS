using Microsoft.Extensions.Options;
using Synergos.Api.Payments.Domain;
using Synergos.Api.Payments.Endpoints;
using Synergos.Api.Payments.Storage;
using Synergos.Api.Payments.Transport;
using Synergos.Shared;

// ─────────────────────────────────────────────────────────────────────────────
// Synergos.Api.Payments — autorizar, capturar, liberar y devolver.
//
// AUTORIZAR Y CAPTURAR ESTÁN SEPARADOS, y es la decisión que sostiene todo el
// resto: autorizar reserva cupo en el medio de pago —reversible y barato—,
// capturar mueve la plata. Es el mismo razonamiento del hold de Api.Booking:
// primero el paso reversible, después el que cuesta. Es lo que permite que un
// flujo que cruza capacidades falle sin dejar plata mal cobrada.
//
// El proveedor es una costura (IPaymentProvider). Por defecto registra, autoriza
// todo y AVISA A GRITOS en cada operación: el CMS ya tuvo el defecto de
// Provider=Wompi sirviendo el stub en silencio, y costó una investigación.
//
// CON CREDENCIAL, quien cobra es Wompi (HU #27) — PSE y Nequi son la mitad de los
// pagos de este mercado, así que una pasarela sin ellos no cobra. Y desde esta HU
// ES ACÁ donde se mueve la plata: el proveedor que el CMS tiene en proceso
// (ADR 0116) sigue sirviendo el camino sin servicios, pero los dos no pueden
// cobrar de verdad a la vez — si lo hicieran, un mismo cobro pasaría por
// plomerías distintas según un interruptor. Lo vigila un gate del lado del CMS.
// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);

// El hilo que permite seguir una compra por los seis procesos (HU #28).
builder.AddCorrelation();

builder.Services.Configure<PaymentStorageOptions>(builder.Configuration.GetSection("Payments:Storage"));
builder.Services.AddSingleton<IPaymentStore, FileSystemPaymentStore>();
// Qué proveedor cobra — `Payments:Provider` (HU #27).
//
//   (vacío) / "logging"  → LoggingPaymentProvider. Dice que sí a todo y lo grita. Es el default
//                          de desarrollo: un clon limpio corre el flujo sin cuenta de pasarela.
//   cualquier otro       → se cobra de verdad con ESE, y si le falta la credencial se registra
//                          NotConfiguredPaymentProvider, que RECHAZA cada cobro a gritos.
//
// La tercera opción —el nombre puesto y el stub sirviendo en silencio— es justo el defecto que
// el CMS ya sufrió, y por eso no existe: o cobra, o dice a gritos que no puede.
builder.Services.Configure<WompiOptions>(builder.Configuration.GetSection($"Payments:{WompiOptions.ProviderName}"));

// El cliente hacia Wompi. Es el ÚNICO HttpClient de esta capacidad, y por eso Api.Payments está
// en la lista de las que pueden salir a la red (#49) CON la razón al lado: sale a un TERCERO, no
// a otra capacidad. Las veinte siguen siendo hojas.
builder.Services.AddHttpClient(WompiOptions.ProviderName);

builder.Services.AddSingleton<IPaymentProvider>(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var pedido = (cfg["Payments:Provider"] ?? string.Empty).Trim();

    if (pedido.Length == 0 || string.Equals(pedido, "logging", StringComparison.OrdinalIgnoreCase))
    {
        return new LoggingPaymentProvider(sp.GetRequiredService<ILogger<LoggingPaymentProvider>>());
    }

    if (string.Equals(pedido, WompiOptions.ProviderName, StringComparison.OrdinalIgnoreCase))
    {
        var wompi = sp.GetRequiredService<IOptions<WompiOptions>>();

        // Qué falta se dice POR NOMBRE. «No está configurado» manda a leer código; «falta
        // Payments:wompi:IntegritySecret» manda a poner una variable.
        if (wompi.Value.QueFalta() is { } incompleto)
        {
            return new NotConfiguredPaymentProvider(
                pedido, incompleto, sp.GetRequiredService<ILogger<NotConfiguredPaymentProvider>>());
        }

        return new WompiPaymentProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(WompiOptions.ProviderName),
            wompi,
            sp.GetRequiredService<ILogger<WompiPaymentProvider>>());
    }

    // Un nombre que no conocemos es un defecto de despliegue, no un motivo para caer al stub en
    // silencio: eso es exactamente lo que el CMS ya sufrió con Provider=Wompi.
    var llave = cfg[$"Payments:{pedido}:ApiKey"];
    var falta = string.IsNullOrWhiteSpace(llave) ? $"Payments:{pedido}:ApiKey" : null;

    return new NotConfiguredPaymentProvider(
        pedido,
        falta ?? $"el adaptador de '{pedido}' (no existe; el que sí existe se pide como '{WompiOptions.ProviderName}')",
        sp.GetRequiredService<ILogger<NotConfiguredPaymentProvider>>());
});
builder.Services.AddSingleton<IIdempotencyLedger>(sp =>
    new FileIdempotencyLedger(sp.GetRequiredService<IOptions<PaymentStorageOptions>>().Value.Root));
builder.Services.AddSingleton<WebhookVerifier>();
builder.Services.AddSingleton(TimeProvider.System);

// El verificador de tokens de identidad (HU #14). NO obligatorio: un clon limpio arranca sin
// llave y el cobro se sigue autorizando con CmsSession, que es lo que hacia siempre.
//
// Lo que NO pasa sin llave es aceptar un token a ciegas: si alguien presenta uno y este servicio
// no puede comprobarlo, se RECHAZA. Ignorarlo dejaria que alguien mandara cualquier cosa y
// siguiera adelante como si no hubiera mandado nada, que es peor que no aceptar tokens.
//
// QUIEN PAGA se comprueba, no se cree. Con la llave compartida sola, cualquier servicio que
// pueda hablar con esta capacidad escribe un cobro a nombre de quien quiera — y un movimiento de
// plata atribuido a quien no lo hizo es de los registros que alguien cita en una disputa.
// Verificacion LOCAL, sin preguntarle a Api.Identity: una capacidad no llama a otra (#49) y
// hacerlo la volveria el punto unico de fallo de las veinte.
builder.AddIdentityTokens(required: false);
builder.Services.AddSingleton<PaymentService>();

var app = builder.Build();

app.UseCorrelation();
app.UseSharedKeyAuth(app.Configuration["Payments:ApiKey"], "/v1/webhooks");

// Un solo escritor por capacidad, aunque corran varias réplicas (#112). Sube a proceso
// cruzado el `lock` que el servicio ya tenía dentro: con el almacén en un fichero por
// documento, lo que queda por serializar es leer-decidir-escribir sobre el MISMO.
app.UseStoreWriteGate(
    app.Services.GetRequiredService<IOptions<PaymentStorageOptions>>().Value.Root,
    PaymentRules.CodePrefix,
    app.Configuration.GetValue<int?>("Payments:Storage:WriteGateSeconds"));
app.MapPaymentEndpoints();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

/// <summary>El programa, expuesto para que los tests puedan levantarlo en memoria.</summary>
public partial class Program;
