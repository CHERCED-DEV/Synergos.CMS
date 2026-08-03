using Microsoft.Extensions.Options;
using Synergos.Bff.Salud.Clients;
using Synergos.Core;

namespace Synergos.Bff.Salud.Domain;

/// <summary>A quién se avisa cuando una compensación se rinde.</summary>
/// <remarks>
/// <b>El destinatario se configura; no se inventa.</b> Cablear una dirección acá sería la primera
/// grieta de un BFF que después hay que desplegar en otra clínica con otra guardia. Si no está
/// configurado, el aviso no sale y se dice a gritos cuál es la clave que falta — que es
/// infinitamente mejor que mandarle correos de urgencia a quien puso su dirección en un ejemplo.
/// </remarks>
public sealed class AlertOptions
{
    /// <summary>Tipo del destinatario — <c>salud.guardia</c>, <c>equipo</c>, lo que sea.</summary>
    public string? ToKind { get; set; }

    /// <summary>Identificador del destinatario.</summary>
    public string? ToId { get; set; }

    /// <summary>La dirección concreta en el canal de la plantilla.</summary>
    public string? Address { get; set; }

    /// <summary>Clave de la plantilla en <c>Api.Notifications</c>.</summary>
    public string TemplateKey { get; set; } = CompensationAlert.DefaultTemplateKey;
}

/// <summary>
/// Avisa a una persona de que una compensación se rindió.
/// </summary>
/// <remarks>
/// <para><b>Es lo que cierra el lazo.</b> Sin esto, <c>CompensationFailed</c> es una fila en
/// <c>GET /v1/compensations</c> y un log en rojo — y el log en rojo solo sirve si alguien lo está
/// mirando justo en ese minuto. Plata cobrada sin servicio que nadie atiende es el peor final
/// posible de todo este aparato.</para>
///
/// <para><b>No lleva datos del paciente.</b> El aviso sale por correo o SMS a una guardia
/// operativa, y el identificador de la cita basta para que quien lo atienda entre al sistema y
/// mire. Meter el paciente en el cuerpo del correo sacaría un dato clínico a un canal que no está
/// pensado para eso.</para>
/// </remarks>
public sealed class CompensationAlert
{
    /// <summary>La plantilla que hay que autorar en <c>Api.Notifications</c>.</summary>
    public const string DefaultTemplateKey = "salud.compensacion.colgada";

    /// <summary>
    /// Los marcadores que este aviso rellena. La plantilla <b>no puede usar otros</b>:
    /// <c>Api.Notifications</c> rechaza un marcador sin valor en vez de mandar un hueco.
    /// </summary>
    public static readonly IReadOnlyList<string> Placeholders = new[] { "cita", "desde", "pendientes" };

    private readonly SaludCapabilities _caps;
    private readonly AlertOptions _options;

    public CompensationAlert(SaludCapabilities caps, IOptions<AlertOptions> options)
    {
        _caps = caps;
        _options = options.Value;
    }

    /// <summary>La plantilla que este despliegue usa, para poder nombrarla en un log.</summary>
    public string TemplateKey => _options.TemplateKey;

    /// <summary>Manda el aviso. Devuelve el rechazo si no pudo, <c>null</c> si salió.</summary>
    /// <remarks>
    /// La llave de idempotencia deriva de la saga y del número de aviso —<c>{sagaId}:alert:0</c>—
    /// por el mismo motivo que todas las demás: un reintento tras un timeout no le manda a la
    /// guardia un segundo correo idéntico, pero un reintento pedido <i>a mano</i> después de
    /// arreglar la causa sí manda uno nuevo.
    /// </remarks>
    public async Task<Rejection?> RaiseAsync(AppointmentSaga saga, CancellationToken ct)
    {
        var to = Ref.TryCreate(_options.ToKind, _options.ToId);
        if (to is null || string.IsNullOrWhiteSpace(_options.Address))
        {
            return Rejection.Invalid("salud.alert_not_configured",
                "No hay a quién avisar: faltan Salud:Alerts:ToKind, :ToId y :Address.");
        }

        var valores = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cita"] = saga.Id,
            ["desde"] = saga.StartedAtUtc.ToString("u"),
            ["pendientes"] = Describir(saga.Stuck),
        };

        var r = await _caps.NotifyAsync(to, _options.Address!, _options.TemplateKey, valores,
            saga.KeyFor($"alert:{saga.AlertsSent}"), ct);

        return r.IsOk ? null : r.Rejection;
    }

    /// <summary>Qué quedó colgado, en una línea que se pueda leer en un SMS.</summary>
    private static string Describir(IReadOnlyList<Compensation> colgadas)
        => colgadas.Count == 0
            ? "(nada)"
            : string.Join("; ", colgadas.Select(c =>
                $"{c.Kind} sobre {c.TargetId} tras {c.Attempts} intentos: {c.LastError ?? "sin detalle"}"));
}
