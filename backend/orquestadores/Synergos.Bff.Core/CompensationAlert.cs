using Microsoft.Extensions.Options;
using Synergos.Core;

namespace Synergos.Bff.Core;

/// <summary>
/// Cómo este orquestador se nombra a sí mismo.
/// </summary>
/// <param name="Origin">Prefijo de los códigos de rechazo y nombre del sistema en el aviso —
/// <c>salud</c>, <c>tienda</c>.</param>
/// <param name="Noun">Cómo llama este dominio a su saga, con artículo — <c>la cita</c>,
/// <c>el pedido</c>. Va solo en el texto para una persona.</param>
/// <remarks>
/// <b>El código y el texto se separan a propósito.</b> El <c>code</c> lo compara una máquina y
/// tiene que ser estable e igual en los ocho dominios (<c>{origen}.saga_not_found</c>); el
/// <c>detail</c> lo lee una persona en español y por eso lleva el sustantivo del dominio. Meter
/// el sustantivo en el código obligaría a cada consumidor a conocer nueve variantes de
/// «no existe».
/// </remarks>
public sealed record SagaVocabulary(string Origin, string Noun);

/// <summary>A quién se avisa cuando una compensación se rinde.</summary>
/// <remarks>
/// <b>El destinatario se configura; no se inventa.</b> Cablear una dirección aquí sería la
/// primera grieta de un orquestador que después hay que desplegar en otra instalación con otra
/// guardia. Si no está configurado, el aviso no sale y se dice a gritos cuál es la clave que
/// falta — que es infinitamente mejor que mandarle correos de urgencia a quien puso su dirección
/// en un ejemplo.
/// </remarks>
public sealed class AlertOptions
{
    /// <summary>Tipo del destinatario — <c>guardia</c>, <c>equipo</c>, lo que sea.</summary>
    public string? ToKind { get; set; }

    /// <summary>Identificador del destinatario.</summary>
    public string? ToId { get; set; }

    /// <summary>La dirección concreta en el canal de la plantilla.</summary>
    public string? Address { get; set; }

    /// <summary>Clave de la plantilla en <c>Api.Notifications</c>.</summary>
    public string? TemplateKey { get; set; }
}

/// <summary>
/// Avisa a una persona de que una compensación se rindió.
/// </summary>
/// <remarks>
/// <para><b>Es lo que cierra el lazo.</b> Sin esto, <c>CompensationFailed</c> es una fila en la
/// vista de operación y un log en rojo — y el log en rojo solo sirve si alguien lo está mirando
/// justo en ese minuto. Plata cobrada sin servicio que nadie atiende es el peor final posible de
/// todo este aparato.</para>
///
/// <para><b>No lleva datos de la persona atendida.</b> El aviso sale por correo o SMS a una
/// guardia operativa, y el identificador de la saga basta para que quien lo atienda entre al
/// sistema y mire. Meter el paciente, el comprador o su dirección en el cuerpo del correo sacaría
/// un dato sensible a un canal que no está pensado para eso.</para>
/// </remarks>
public sealed class CompensationAlert
{
    /// <summary>El nombre del cliente HTTP de la capacidad de avisos.</summary>
    public const string Capability = "notifications";

    /// <summary>Clave de plantilla por defecto si el despliegue no configura otra.</summary>
    public const string DefaultTemplateKey = "bff.compensacion.colgada";

    /// <summary>
    /// Los marcadores que este aviso rellena. La plantilla <b>no puede usar otros</b>:
    /// <c>Api.Notifications</c> rechaza un marcador sin valor en vez de mandar un hueco.
    /// </summary>
    public static readonly IReadOnlyList<string> Placeholders = new[] { "saga", "origen", "desde", "pendientes" };

    private readonly IHttpClientFactory _clients;
    private readonly SagaVocabulary _vocabulary;
    private readonly AlertOptions _options;

    public CompensationAlert(IHttpClientFactory clients, SagaVocabulary vocabulary, IOptions<AlertOptions> options)
    {
        _clients = clients;
        _vocabulary = vocabulary;
        _options = options.Value;
    }

    /// <summary>La plantilla que este despliegue usa, para poder nombrarla en un log.</summary>
    public string TemplateKey => string.IsNullOrWhiteSpace(_options.TemplateKey)
        ? DefaultTemplateKey
        : _options.TemplateKey!;

    /// <summary>Manda el aviso. Devuelve el rechazo si no pudo, <c>null</c> si salió.</summary>
    /// <remarks>
    /// La llave de idempotencia deriva de la saga y del número de aviso —<c>{sagaId}:alert:0</c>—
    /// por el mismo motivo que todas las demás: un reintento tras un timeout no le manda a la
    /// guardia un segundo correo idéntico, pero un reintento pedido <i>a mano</i> después de
    /// arreglar la causa sí manda uno nuevo.
    /// </remarks>
    public async Task<Rejection?> RaiseAsync(ISaga saga, CancellationToken ct)
    {
        var to = Ref.TryCreate(_options.ToKind, _options.ToId);
        if (to is null || string.IsNullOrWhiteSpace(_options.Address))
        {
            return Rejection.Invalid($"{_vocabulary.Origin}.alert_not_configured",
                $"No hay a quién avisar: faltan Alerts:ToKind, :ToId y :Address de {_vocabulary.Origin}.");
        }

        var valores = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["saga"] = saga.Id,
            ["origen"] = _vocabulary.Origin,
            ["desde"] = saga.StartedAtUtc.ToString("u"),
            ["pendientes"] = Describir(saga.Stuck()),
        };

        var r = await CapabilityHttp.SendAsync<DeliveryDto>(
            _clients.CreateClient(Capability), HttpMethod.Post, "v1/deliveries",
            new
            {
                toKind = to.Kind,
                toId = to.Id,
                address = _options.Address,
                // Se manda la CLAVE de la plantilla, no el texto: es la línea que mantiene
                // Api.Notifications agnóstica. Sabe rellenar marcadores y entregar, no sabe qué
                // es una compensación colgada. El texto lo escribe el dominio y vive del otro lado.
                templateKey = TemplateKey,
                values = valores,
            },
            saga.KeyFor($"alert:{saga.AlertsSent}"), Capability, ct);

        return r.IsOk ? null : r.Rejection;
    }

    /// <summary>Qué quedó colgado, en una línea que se pueda leer en un SMS.</summary>
    private static string Describir(IReadOnlyList<Compensation> colgadas)
        => colgadas.Count == 0
            ? "(nada)"
            : string.Join("; ", colgadas.Select(c =>
                $"{c.Kind} sobre {c.TargetId} tras {c.Attempts} intentos: {c.LastError ?? "sin detalle"}"));

    private sealed record DeliveryDto(string Id, string Status);
}
