using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El <see cref="IVisitSchedulingService"/> que aparta cupo de verdad — contra
/// <c>Synergos.Api.Booking</c>, sin orquestador en medio (HU #33a).
/// </summary>
/// <remarks>
/// <para><b>Directo a la capacidad, y ésa es la decisión.</b> La tienda (HU #24) y la cita clínica
/// (HU #25) fueron contra su BFF porque su flujo toca varias capacidades y hay un orden que
/// respetar y algo que deshacer. Agendar una visita no: <b>una visita no se cobra</b> —el motor en
/// proceso la confirmaba con una sesión de pago de mentira, <c>visit-free</c>, y un total
/// simbólico de 1 COP— así que toca <b>una sola</b> capacidad. Meter un orquestador sería una saga
/// de un paso: la máquina de compensar sin nada que compensar (<c>CLAUDE.md</c> §17, promover al
/// segundo consumidor y no antes).</para>
///
/// <para><b>El reparto, que era lo que la HU #33 tenía sin resolver.</b> Qué franjas EXISTEN es
/// del CMS —la agenda del agente es una decisión del negocio inmobiliario, ver
/// <see cref="VisitAgenda"/>—; si una franja concreta sigue LIBRE es de <c>Api.Booking</c>. No son
/// dos verdades que sincronizar: son dos cosas distintas, y por eso ninguna duplica a la otra.
/// El motor viejo las tenía fundidas, y ésa es la razón de que su <c>Reservation</c> llevara
/// <c>RoomTypeCode</c> y <c>GuestName</c> — sustantivos que ninguna capacidad puede guardar.</para>
///
/// <para><b>El identificador del recurso NO se adivina.</b> Se resuelve preguntándole a la
/// capacidad por el sujeto (<c>GET /v1/resources?subjectKind=&amp;subjectId=</c>). Lo genera ella;
/// ninguna convención del CMS podría acertarlo. Inventarse una fue el error que costó una vuelta
/// en la HU #25, y no se repite acá.</para>
///
/// <para><b>Al interesado se le manda un identificador OPACO, no su correo.</b> El nombre, el
/// email y el teléfono se quedan de este lado: <c>Api.Booking</c> necesita saber <i>que hay
/// alguien</i> ocupando la franja, no quién es. Mandarle datos personales a una capacidad que solo
/// cuenta cupo los esparce sin ninguna ganancia — y los respaldos de la HU #31 ya llevan
/// bastantes.</para>
///
/// <para><b>La idempotencia sale del par listado+franja</b>, y por eso no hace falta almacén de
/// este lado: reagendar la misma franja reusa la misma llave, la capacidad devuelve el mismo hold
/// y la misma reserva, y no se aparta dos veces. El stub lo lograba guardando la visita en su
/// propio store; acá el registro es el de la capacidad, que es dueña de él.</para>
///
/// <para><b>Y no hay dos relojes.</b> En este modo el CMS no crea ninguna reserva propia, así que
/// <c>HoldExpirationScannerHostedService</c> no tiene nada de este vertical que barrer:
/// <c>Api.Booking</c> vence sus holds sola —de forma perezosa, sin barrido— y es la única que
/// opina sobre ellos.</para>
/// </remarks>
public sealed class HttpVisitSchedulingService : IVisitSchedulingService
{
    /// <summary>Cabecera de la llave compartida.</summary>
    public const string ApiKeyHeader = "X-Synergos-Key";

    /// <summary>Cliente nombrado que registra el composer.</summary>
    public const string ClientName = "synergos-api-booking";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _clientes;
    private readonly IOptionsMonitor<RealtySettings> _opciones;
    private readonly TimeProvider _reloj;
    private readonly ILogger<HttpVisitSchedulingService> _log;

    public HttpVisitSchedulingService(
        IHttpClientFactory clientes,
        IOptionsMonitor<RealtySettings> opciones,
        TimeProvider reloj,
        ILogger<HttpVisitSchedulingService> log)
    {
        _clientes = clientes;
        _opciones = opciones;
        _reloj = reloj;
        _log = log;
    }

    private RealtySettings Config => _opciones.CurrentValue;

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Si la capacidad no contesta, la agenda se muestra igual</b> — con todo disponible,
    /// y el intento de agendar será el que falle con su motivo. Es la degradación correcta para
    /// una consulta de lectura: esconder la ficha del inmueble porque el servicio de cupo está
    /// caído castiga al visitante por un problema que no es suyo, y la ficha es lo que vende.</para>
    /// </remarks>
    public async Task<IReadOnlyList<VisitSlot>> GetSlotsAsync(
        string listingId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(listingId)) return Array.Empty<VisitSlot>();

        var listado = listingId.Trim();
        var agenda = VisitAgenda.For(listado, _reloj.GetUtcNow());

        string? recurso;
        try
        {
            recurso = await ResolverRecursoAsync(listado, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning(ex, "Api.Booking no contestó al listar la agenda de {Listado}.", listado);
            return agenda;
        }

        if (recurso is null)
        {
            // El inmueble no tiene recurso registrado. Es un paso de DESPLIEGUE que falta, no un
            // fallo: se dice a gritos porque de otro modo la agenda se ve normal y agendar
            // rechaza, y nadie relaciona las dos cosas.
            _log.LogError(
                "El listado {Listado} no tiene recurso en Api.Booking. Registrarlo con "
                + "POST /v1/resources (subjectKind={Kind}). Sin él no se puede apartar ninguna visita.",
                listado, Config.ListingKind);
            return agenda;
        }

        // Una consulta por franja. Son seis, y se paga a propósito: la capacidad contesta por
        // ventana concreta y con el MOTIVO de cada no —fuera de horario, sin cupo, en el pasado—,
        // que es lo que permite decirle al visitante qué cambiar. Deducirlo de una lista de
        // reservas sería una consulta menos y una mentira más: los holds sin confirmar no salen
        // ahí, y la franja aparecería libre hasta el momento de reservarla.
        var resultado = new List<VisitSlot>(agenda.Count);
        foreach (var slot in agenda)
        {
            resultado.Add(slot with
            {
                Available = await EstaLibreAsync(recurso, slot.StartUtc, cancellationToken).ConfigureAwait(false),
            });
        }

        return resultado;
    }

    /// <inheritdoc />
    public async Task<VisitResult> BookAsync(
        string listingId, string slot, VisitContact contact, CancellationToken cancellationToken = default)
    {
        // Las mismas validaciones y las mismas excepciones que el motor en proceso: el controller
        // ya las traduce, y cambiarlas acá haría que el vertical se comportara distinto según una
        // bandera de despliegue.
        if (string.IsNullOrWhiteSpace(listingId))
        {
            throw new ArgumentException("El listado es obligatorio.", nameof(listingId));
        }
        if (string.IsNullOrWhiteSpace(slot))
        {
            throw new ArgumentException("El slot de visita es obligatorio.", nameof(slot));
        }
        if (contact is null || string.IsNullOrWhiteSpace(contact.Name) || string.IsNullOrWhiteSpace(contact.Email))
        {
            throw new ArgumentException("El nombre y el email del interesado son obligatorios.", nameof(contact));
        }

        var listado = listingId.Trim();
        var slotId = slot.Trim();

        var franja = VisitAgenda.Find(listado, slotId, _reloj.GetUtcNow())
            ?? throw new ArgumentException(
                $"El slot '{slotId}' no existe para el listado '{listado}'.", nameof(slot));

        var recurso = await ResolverRecursoAsync(listado, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Este inmueble todavía no acepta visitas en línea. Escribinos y lo coordinamos.");

        var llave = LlaveDe(listado, slotId);
        var hold = await ApartarAsync(recurso, franja, contact, llave, cancellationToken).ConfigureAwait(false);
        var reserva = await ConfirmarAsync(hold, llave, cancellationToken).ConfigureAwait(false);

        return new VisitResult($"visit_{reserva}", "Confirmed");
    }

    // ── Contra la capacidad ─────────────────────────────────────────────────

    private HttpClient Cliente() => _clientes.CreateClient(ClientName);

    private async Task<string?> ResolverRecursoAsync(string listado, CancellationToken ct)
    {
        var ruta = $"v1/resources?subjectKind={Uri.EscapeDataString(Config.ListingKind)}"
                 + $"&subjectId={Uri.EscapeDataString(listado)}";

        using var res = await Cliente().GetAsync(ruta, ct).ConfigureAwait(false);

        // 404 es la respuesta legítima a «este inmueble no tiene recurso», no un fallo.
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        if (!res.IsSuccessStatusCode)
        {
            await GritarSiEsLaLlaveAsync(res, "resolver el recurso", ct).ConfigureAwait(false);
            return null;
        }

        var recurso = await res.Content.ReadFromJsonAsync<RecursoDto>(Json, ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(recurso?.Id) ? null : recurso!.Id;
    }

    private async Task<bool> EstaLibreAsync(string recurso, DateTimeOffset inicio, CancellationToken ct)
    {
        var ruta = $"v1/resources/{Uri.EscapeDataString(recurso)}/availability"
                 + $"?start={Instante(inicio)}&end={Instante(inicio + VisitAgenda.Duracion)}";

        try
        {
            using var res = await Cliente().GetAsync(ruta, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return true;   // ver el remark de GetSlotsAsync

            var disp = await res.Content.ReadFromJsonAsync<DisponibilidadDto>(Json, ct).ConfigureAwait(false);
            return disp?.Available ?? true;
        }
        catch (HttpRequestException)
        {
            return true;
        }
    }

    private async Task<string> ApartarAsync(
        string recurso, VisitSlot franja, VisitContact contacto, string llave, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/holds")
        {
            Content = JsonContent.Create(new
            {
                resourceId = recurso,
                start = franja.StartUtc,
                end = franja.StartUtc + VisitAgenda.Duracion,
                heldForKind = Config.VisitorKind,
                heldForId = Seudonimo(contacto.Email),
            }, options: Json),
        };
        req.Headers.TryAddWithoutValidation("Idempotency-Key", $"{llave}:hold");

        using var res = await Cliente().SendAsync(req, ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            var problema = await LeerProblemaAsync(res, ct).ConfigureAwait(false);
            await GritarSiEsLaLlaveAsync(res, "apartar la visita", ct).ConfigureAwait(false);

            // El motivo de la capacidad se traduce a algo que el visitante pueda accionar. Un
            // «no disponible» genérico para los tres casos sería una regresión frente a lo que
            // hoy dice el motor en proceso (HU #33 §3).
            throw new InvalidOperationException(problema?.Code switch
            {
                "booking.insufficient_capacity" => "Ese horario ya lo tomaron. Elegí otro de la agenda.",
                "booking.outside_opening_hours" => "Ese horario está fuera de la agenda de visitas.",
                "booking.in_the_past" => "Ese horario ya pasó. Elegí uno de los próximos días.",
                _ => "No pudimos apartar la visita. No quedó agendada.",
            });
        }

        var hold = await res.Content.ReadFromJsonAsync<HoldDto>(Json, ct).ConfigureAwait(false);
        return hold?.Id ?? throw new InvalidOperationException("Api.Booking apartó sin devolver identificador.");
    }

    private async Task<string> ConfirmarAsync(string hold, string llave, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post, $"v1/holds/{Uri.EscapeDataString(hold)}/confirm");
        req.Headers.TryAddWithoutValidation("Idempotency-Key", $"{llave}:confirm");

        using var res = await Cliente().SendAsync(req, ct).ConfigureAwait(false);

        if (!res.IsSuccessStatusCode)
        {
            var problema = await LeerProblemaAsync(res, ct).ConfigureAwait(false);
            _log.LogWarning("Api.Booking no confirmó el hold {Hold}: {Code}", hold, problema?.Code ?? "-");

            // No se suelta el hold acá a propósito: vence solo a los 10 min por su propio TTL, y
            // un release que también falle dejaría al visitante esperando dos veces por lo mismo.
            throw new InvalidOperationException(
                "No pudimos confirmar la visita. El horario queda apartado unos minutos; volvé a intentarlo.");
        }

        var reserva = await res.Content.ReadFromJsonAsync<ReservaDto>(Json, ct).ConfigureAwait(false);
        return reserva?.Id ?? throw new InvalidOperationException("Api.Booking confirmó sin devolver identificador.");
    }

    // ── Lo pequeño ──────────────────────────────────────────────────────────

    private static string Instante(DateTimeOffset t)
        => Uri.EscapeDataString(t.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    /// <summary>La llave que hace idempotente reagendar la misma franja del mismo inmueble.</summary>
    private static string LlaveDe(string listado, string slotId)
        => $"realty:{listado}:{slotId}".ToLowerInvariant();

    /// <summary>
    /// Un identificador estable del interesado que <b>no es su correo</b>.
    /// </summary>
    /// <remarks>
    /// Estable para que dos visitas del mismo interesado se reconozcan, y opaco para que la
    /// capacidad —que solo cuenta cupo— no acumule direcciones de correo. No pretende ser
    /// anonimato: es no esparcir lo que no hace falta esparcir.
    /// </remarks>
    private static string Seudonimo(string email)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private async Task<ProblemaDto?> LeerProblemaAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try { return await res.Content.ReadFromJsonAsync<ProblemaDto>(Json, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is JsonException or NotSupportedException) { return null; }
    }

    /// <summary>
    /// Un 401 es la llave, y solo un 401.
    /// </summary>
    /// <remarks>
    /// <c>SharedKeyAuth</c> emite <b>401 y nunca 403</b>. Tratar el 403 como «llave mala» mandaría
    /// a revisar una configuración correcta mientras el rechazo real —de negocio— pasa
    /// desapercibido. Este defecto ya se cometió dos veces en este repo y lo destapó levantar los
    /// procesos, no un test.
    /// </remarks>
    private async Task GritarSiEsLaLlaveAsync(HttpResponseMessage res, string queHacia, CancellationToken ct)
    {
        if (res.StatusCode != HttpStatusCode.Unauthorized) return;

        _log.LogError(
            "Api.Booking respondió 401 al {Que}: la llave compartida falta o es inválida. "
            + "Revisar Synergos:Realty:ApiKey.", queHacia);

        await Task.CompletedTask.ConfigureAwait(false);
    }

    // La forma de lo que llega. Vive acá porque es el contrato HTTP con una capacidad concreta,
    // no vocabulario del CMS.
    private sealed record RecursoDto(string? Id);
    private sealed record DisponibilidadDto(bool Available, string? ReasonCode);
    private sealed record HoldDto(string? Id);
    private sealed record ReservaDto(string? Id);
    private sealed record ProblemaDto(string? Code, string? Detail);
}
