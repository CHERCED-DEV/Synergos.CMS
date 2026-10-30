using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Web.Services.Gobierno;

/// <summary>Cómo terminó una operación del artefacto del expediente: el radicado, o el motivo con su código.</summary>
/// <param name="Estado">El estado HTTP.</param>
/// <param name="Codigo">El código del motivo, nulo si salió bien.</param>
/// <param name="Mensaje">Para el log, y para la persona sólo cuando nombra la pregunta que falta.</param>
/// <param name="EstadoDeLaRadicacion">En qué quedó la saga, cuando eso es el motivo.</param>
/// <param name="Expediente">El expediente guardado, con su radicado.</param>
/// <param name="Transitorio">Si reintentar puede salir distinto.</param>
public sealed record ResultadoDelExpediente(
    int Estado, string? Codigo = null, string? Mensaje = null, string? EstadoDeLaRadicacion = null,
    ExpedienteDeLaPuerta? Expediente = null, bool Transitorio = false)
{
    /// <summary>Si salió bien.</summary>
    public bool Bien => Codigo is null;
}

/// <summary>
/// El artefacto del expediente de una radicación por la puerta (009-gobierno-tramite T027): lo que el CMS guarda y el
/// orquestador de Gobierno no lleva —las respuestas del formulario, el contacto y el radicado—, entre abrir y cerrar.
/// </summary>
/// <remarks>
/// <para><b>Siempre de quien radica.</b> Cada lectura al orquestador lleva el sujeto del miembro de la sesión —el mismo
/// que puso la puerta al abrir—, así que una radicación ajena es un 404 del orquestador y nunca se guarda nada sobre
/// ella.</para>
///
/// <para><b>Lo que se guarda sale del lado que cotizó</b>: el trámite, la tasa y la aceptación de la notificación
/// electrónica son los de la saga, nunca lo que diga el navegador. Las respuestas se validan contra el formulario
/// publicado del trámite.</para>
///
/// <para><b>Un expediente por confirmar no existe para nadie</b> hasta que la reconciliación ve su radicación completa;
/// si termina deshecha, sus respuestas se borran. Reconciliar corre en la ruta de lectura, con techo (la regla de la
/// F3): como mucho <see cref="TopeDeReconciliacion"/> expedientes y <see cref="PresupuestoDeReconciliacion"/> en total;
/// lo que falte se mira en la próxima lectura.</para>
/// </remarks>
public sealed class ArtefactoDelExpediente
{
    /// <summary>Cliente nombrado hacia el orquestador de Gobierno, por la pieza del árbol.</summary>
    public const string ClientName = "synergos-bff-gobierno-artefacto";

    /// <summary>El flujo de la puerta cuyas radicaciones atiende: de su configuración sale el <c>Kind</c> del sujeto.</summary>
    public const string Flujo = "gobierno.radicacion";

    /// <summary>Cuántos expedientes por confirmar mira, como mucho, una reconciliación: los más recientes primero.</summary>
    public const int TopeDeReconciliacion = 10;

    /// <summary>Cuánto puede tardar, como mucho, una reconciliación entera: la espera de quien lee la bandeja.</summary>
    public static readonly TimeSpan PresupuestoDeReconciliacion = TimeSpan.FromSeconds(3);

    private const string Cabecera = "X-Synergos-Sujeto";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _clientes;
    private readonly IExpedientesDeLaPuerta _expedientes;
    private readonly IOptionsMonitor<PuertaSettings> _puerta;
    private readonly ILogger<ArtefactoDelExpediente> _log;
    private readonly bool _hayDestino;
    private readonly Func<DateTimeOffset> _ahora;
    private readonly TimeSpan _presupuesto;

    /// <param name="hayDestino">Si el despliegue dice dónde vive el orquestador de Gobierno.</param>
    public ArtefactoDelExpediente(
        IHttpClientFactory clientes, IExpedientesDeLaPuerta expedientes, IOptionsMonitor<PuertaSettings> puerta,
        ILogger<ArtefactoDelExpediente> log, bool hayDestino, Func<DateTimeOffset>? ahora = null, TimeSpan? presupuesto = null)
    {
        _clientes = clientes;
        _expedientes = expedientes;
        _puerta = puerta;
        _log = log;
        _hayDestino = hayDestino;
        _ahora = ahora ?? (() => DateTimeOffset.UtcNow);
        _presupuesto = presupuesto ?? PresupuestoDeReconciliacion;
    }

    /// <summary>El <c>Kind</c> con que la puerta nombra al ciudadano, o nulo si el flujo no está abierto.</summary>
    private string? Kind => _puerta.CurrentValue.Flujos.TryGetValue(Flujo, out var f) ? f.SujetoKind : null;

    /// <summary>
    /// Guarda las respuestas de una radicación en curso del miembro —o las reemplaza mientras siga abierta— y acuña el
    /// radicado la primera vez.
    /// </summary>
    /// <param name="ciudadano">El contacto de la sesión, con la <c>MemberKey</c> del miembro.</param>
    public async Task<ResultadoDelExpediente> GuardarRespuestasAsync(
        string radicacion, GovCitizen ciudadano, IReadOnlyDictionary<string, string> respuestas, CancellationToken ct)
    {
        if (NoDisponible() is { } cerrado) return cerrado;
        if (ciudadano?.MemberKey is not { } miembro || miembro == Guid.Empty)
        {
            return new ResultadoDelExpediente(StatusCodes.Status401Unauthorized, "puerta.sesion_requerida", "Hace falta iniciar sesión.");
        }

        var (leida, fallo) = await LeerAsync(radicacion, miembro, ct).ConfigureAwait(false);
        if (fallo is not null) return fallo;

        if (!string.Equals(leida!.Estado, "Running", StringComparison.Ordinal))
        {
            return new ResultadoDelExpediente(StatusCodes.Status409Conflict, "gov.radicacion_cerrada",
                "Las respuestas se guardan mientras la radicación está abierta.", leida.Estado);
        }
        if (leida.Datos?.Tramite?.Id is not { Length: > 0 } tramite || leida.Datos.Total is not { } total)
        {
            return Invalida();
        }

        var aceptacion = leida.Datos.NotificacionElectronica is { } acepta && !string.IsNullOrWhiteSpace(leida.Datos.VersionDelTexto)
            ? new NotificationConsent(acepta, leida.Datos.VersionDelTexto.Trim(), _ahora())
            : null;
        try
        {
            var guardado = await _expedientes.GuardarAsync(
                new RespuestasDeLaPuerta(leida.Id, tramite, ciudadano, respuestas, aceptacion, total.Amount, total.Currency), ct)
                .ConfigureAwait(false);
            return guardado is null
                ? new ResultadoDelExpediente(StatusCodes.Status409Conflict, "gov.radicacion_cerrada",
                    "La radicación ya quedó radicada: sus respuestas no cambian.", "Completed")
                : new ResultadoDelExpediente(StatusCodes.Status200OK, Expediente: guardado);
        }
        catch (ArgumentException ex)
        {
            // El mensaje nombra la pregunta obligatoria que falta (escenario 4 de la historia 3): no se cobró nada.
            return new ResultadoDelExpediente(StatusCodes.Status400BadRequest, "gov.respuestas_incompletas", ex.Motivo());
        }
    }

    /// <summary>
    /// Confirma los expedientes por confirmar que <paramref name="lasQueTocan"/> elige y cuya radicación el orquestador
    /// completó, y borra los de las que terminaron deshechas. Nunca falla hacia quien lee.
    /// </summary>
    /// <remarks>
    /// Una radicación que el orquestador no tiene para su dueño (404) tampoco se completará: se borra igual. Una que se
    /// rindió con la tasa cobrada sin devolver (<c>CompensationFailed</c>) se queda: es de una persona.
    /// </remarks>
    public async Task ReconciliarAsync(Func<ExpedienteDeLaPuerta, bool> lasQueTocan, CancellationToken ct)
    {
        if (NoDisponible() is not null) return;

        using var presupuesto = CancellationTokenSource.CreateLinkedTokenSource(ct);
        presupuesto.CancelAfter(_presupuesto);
        try
        {
            var pendientes = (await _expedientes.PorConfirmarAsync(presupuesto.Token).ConfigureAwait(false))
                .Where(e => e.Dueno is { } d && d != Guid.Empty && lasQueTocan(e))
                .Take(TopeDeReconciliacion)
                .ToList();
            foreach (var expediente in pendientes)
            {
                var (leida, fallo) = await LeerAsync(expediente.Radicacion, expediente.Dueno!.Value, presupuesto.Token).ConfigureAwait(false);
                if (fallo is { Estado: StatusCodes.Status404NotFound } || leida is { Estado: "Compensated" })
                {
                    await _expedientes.DescartarAsync(expediente.Radicacion, presupuesto.Token).ConfigureAwait(false);
                    _log.LogInformation("La radicación {Radicado} terminó deshecha: se borraron sus respuestas.", expediente.Radicado);
                }
                else if (leida is { Estado: "Completed" })
                {
                    await _expedientes.ConfirmarAsync(expediente.Radicacion, presupuesto.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("La reconciliación de radicaciones de la puerta llegó a su techo de {Techo}: lo que falta se mira en la próxima.", _presupuesto);
        }
    }

    private ResultadoDelExpediente? NoDisponible()
        => _hayDestino && !string.IsNullOrWhiteSpace(Kind)
            ? null
            : new ResultadoDelExpediente(StatusCodes.Status503ServiceUnavailable, "gov.radicacion_no_disponible",
                "La radicación por la puerta no está abierta en este sitio.");

    private static ResultadoDelExpediente Ajena()
        => new(StatusCodes.Status404NotFound, "gobierno.saga_not_found", "La radicación no existe.");

    private static ResultadoDelExpediente Invalida()
        => new(StatusCodes.Status502BadGateway, "gov.respuesta_invalida", "El orquestador no pudo contestar por la radicación.");

    /// <summary>La radicación tal como la ve su dueño, o el motivo por el que no se pudo leer.</summary>
    private async Task<(RadicacionLeida? Radicacion, ResultadoDelExpediente? Fallo)> LeerAsync(string radicacion, Guid miembro, CancellationToken ct)
    {
        // Un segmento de punto no es una radicación: escaparlo no lo cambia y movería la lectura a otra ruta.
        if (string.IsNullOrWhiteSpace(radicacion) || Puerta.ReenvioDeLaPuerta.EsSegmentoDePunto(radicacion)) return (null, Ajena());

        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/flujos/{Flujo}/{Uri.EscapeDataString(radicacion)}");
        req.Headers.TryAddWithoutValidation(Cabecera, $"{Kind}:{miembro:n}");
        try
        {
            using var res = await _clientes.CreateClient(ClientName).SendAsync(req, ct).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                var leida = await res.Content.ReadFromJsonAsync<RadicacionLeida>(Json, ct).ConfigureAwait(false);
                return leida is null || string.IsNullOrWhiteSpace(leida.Id) ? (null, Invalida()) : (leida, null);
            }
            if (res.StatusCode == HttpStatusCode.NotFound) return (null, Ajena());

            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, ct).ConfigureAwait(false);
            _log.LogWarning("Gobierno contestó {Estado} ({Codigo}) al leer una radicación para su expediente.", (int)res.StatusCode, rechazo?.Codigo ?? "-");
            return (null, Invalida());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            _log.LogWarning("Gobierno no respondió al leer una radicación para su expediente: {Motivo}", ex.Message);
            return (null, new ResultadoDelExpediente(StatusCodes.Status503ServiceUnavailable, "gov.orquestador_no_disponible",
                "El orquestador de Gobierno no está disponible.", Transitorio: true));
        }
    }

    // Lo que el artefacto lee de la respuesta de la puerta (GobiernoRadicacionRespuesta de Synergos.Bff.Gobierno.json):
    // sólo los campos que usa, y los cruza ConsumidorCmsDeBffGobiernoTests contra el contrato comiteado.

    internal sealed record RadicacionLeida(string Id, string Numero, string Estado, DatosDeLaRadicacion? Datos, string? Error);

    internal sealed record DatosDeLaRadicacion(TramiteLeido? Tramite, DineroLeido? Total, bool? NotificacionElectronica, string? VersionDelTexto);

    internal sealed record TramiteLeido(string Kind, string Id);

    internal sealed record DineroLeido(decimal Amount, string Currency);
}
