using Synergos.Api.Notifications.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Notifications.Domain;

/// <summary>Compone las reglas de <see cref="NotificationRules"/> con los almacenes y el transporte.</summary>
public sealed class NotificationService
{
    private readonly ITemplateStore _templates;
    private readonly IDeliveryStore _deliveries;
    private readonly INotificationSender _sender;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public NotificationService(
        ITemplateStore templates, IDeliveryStore deliveries, INotificationSender sender,
        IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _templates = templates;
        _deliveries = deliveries;
        _sender = sender;
        _idempotency = idempotency;
        _clock = clock;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    public Result<Template> SaveTemplate(string? key, Channel channel, string? subject, string? body, IdempotencyKey idem)
    {
        lock (_gate)
        {
            if (_idempotency.Find("template", idem) is { } yaEra)
            {
                return _templates.Find(yaEra) is { } previa
                    ? Result.Ok(previa)
                    : Rejection.Conflict($"{NotificationRules.CodePrefix}.idempotency_orphan",
                        "La llave ya se usó pero la plantilla no está.");
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                return Rejection.Invalid($"{NotificationRules.CodePrefix}.key_required", "La plantilla necesita una clave estable.");
            }
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
            {
                return Rejection.Invalid($"{NotificationRules.CodePrefix}.empty_template", "Hacen falta asunto y cuerpo.");
            }
            if (_templates.FindByKey(key!) is not null)
            {
                return Rejection.Conflict($"{NotificationRules.CodePrefix}.key_taken", $"Ya hay una plantilla '{key}'.");
            }

            var id = Guid.NewGuid().ToString("n");
            var template = new Template(id, key!.Trim(), channel, subject!, body!);
            _templates.Put(template);
            _idempotency.Remember("template", idem, id);
            return Result.Ok(template);
        }
    }

    public Result<Template> GetTemplate(string id)
        => _templates.Find(id) is { } t
            ? Result.Ok(t)
            : Rejection.NotFound($"{NotificationRules.CodePrefix}.template_not_found", $"No existe la plantilla {id}.");

    public Page<Template> ListTemplates(int offset, int limit)
    {
        var todas = _templates.All().OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
        return new Page<Template>(todas.Skip(offset).Take(limit).ToList(), todas.Count, offset);
    }

    /// <summary>
    /// Manda un aviso: reserva el registro, habla con el proveedor, y anota en qué quedó.
    /// </summary>
    /// <remarks>
    /// <para><b>El registro se reserva ANTES de tocar la red, y ése es el cambio que importa.</b>
    /// Antes se enviaba primero y se registraba después: si el proceso moría entre las dos cosas,
    /// el correo había salido, no quedaba rastro, y la llave de idempotencia no estaba anotada —
    /// así que el reintento mandaba un segundo correo idéntico. Al revés, lo peor que queda es un
    /// registro <c>Queued</c> que se ve, se consulta y se reintenta.</para>
    ///
    /// <para><b>Un <c>Queued</c> no cierra la llave, la sostiene.</b> Reintentar con la misma
    /// llave vuelve a intentar el envío <i>sobre el mismo registro</i>; en cuanto el proveedor
    /// acepta, la llave pasa a devolver lo que ya salió y no se manda nada más. Es la única forma
    /// de que «no mandar dos veces» y «un fallo transitorio se puede reintentar» sean verdad a la
    /// vez.</para>
    /// </remarks>
    public async Task<Result<Delivery>> SendAsync(
        Ref to, string? address, string? templateKey,
        IReadOnlyDictionary<string, string>? values, IdempotencyKey idem, CancellationToken ct = default)
    {
        Delivery reservada;
        Channel canal;

        lock (_gate)
        {
            var reserva = Reservar(to, address, templateKey, values, idem);
            if (!reserva.IsOk) return Result.Rejected<Delivery>(reserva.Rejection!);
            if (reserva.Value.Status != DeliveryStatus.Queued) return reserva;   // ya salió

            reservada = reserva.Value;
            canal = reservada.Channel;
        }

        // Fuera del cerrojo: acá se habla por red, y sostener el cerrojo durante una llamada
        // remota deja la capacidad entera esperando a un tercero.
        Result<string> envio;
        try
        {
            envio = await _sender.SendAsync(canal, reservada.Address, reservada.Subject, reservada.Body,
                reservada.Id, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // El contrato dice que no lanza. Si aun así lanza, el registro tiene que quedar
            // consultable: perder el rastro por una excepción del adapter es exactamente el
            // agujero que esta reserva existe para tapar.
            envio = Result.Rejected<string>(NotificationRules.TransportUnavailable(ex.GetType().Name));
        }

        lock (_gate)
        {
            var actual = _deliveries.Find(reservada.Id) ?? reservada;

            if (!envio.IsOk)
            {
                // Transitorio → se queda en Queued y se puede reintentar con la misma llave.
                // Definitivo → Failed, y no se vuelve a intentar.
                if (!envio.Rejection!.IsTransient)
                {
                    _deliveries.Put(actual with { Status = DeliveryStatus.Failed, StatusAtUtc = Now });
                }
                return Result.Rejected<Delivery>(envio.Rejection);
            }

            var aceptada = actual with
            {
                Status = NotificationRules.Advance(actual.Status, DeliveryStatus.Accepted),
                ProviderMessageId = envio.Value,
                StatusAtUtc = Now,
            };
            _deliveries.Put(aceptada);
            return Result.Ok(aceptada);
        }
    }

    /// <summary>
    /// Resuelve la llave y, si hace falta, crea el registro <c>Queued</c>. Todo bajo el cerrojo.
    /// </summary>
    /// <remarks>
    /// La llave se resuelve <b>antes</b> que toda regla que dependa del estado — tope de
    /// frecuencia incluido. Al revés, el reintento de un envío que ya contó chocaría contra el
    /// tope que él mismo llenó (CLAUDE.md §0.B, punto 16).
    /// </remarks>
    private Result<Delivery> Reservar(
        Ref to, string? address, string? templateKey,
        IReadOnlyDictionary<string, string>? values, IdempotencyKey idem)
    {
        if (_idempotency.Find("delivery", idem) is { } yaEra)
        {
            return _deliveries.Find(yaEra) is { } previa
                ? Result.Ok(previa)
                : Rejection.Conflict($"{NotificationRules.CodePrefix}.idempotency_orphan",
                    "La llave ya se usó pero el envío no está.");
        }

        if (string.IsNullOrWhiteSpace(templateKey) || _templates.FindByKey(templateKey!) is not { } template)
        {
            return Rejection.NotFound($"{NotificationRules.CodePrefix}.template_not_found",
                $"No existe la plantilla '{templateKey}'.");
        }

        var motivo = NotificationRules.CheckAddress(template.Channel, address);
        if (motivo is not null) return Result.Rejected<Delivery>(motivo);

        // Se pregunta antes de registrar nada: un envío por un canal que este despliegue no sabe
        // hablar no es un fallo del proveedor, es una configuración que falta.
        if (!_sender.Supports(template.Channel))
        {
            return Result.Rejected<Delivery>(NotificationRules.ChannelUnsupported(template.Channel));
        }

        var desde = Now - NotificationRules.RateWindow;
        var recientes = _deliveries.ForRecipient(to).Count(d => d.AtUtc >= desde);
        motivo = NotificationRules.CheckRate(recientes);
        if (motivo is not null) return Result.Rejected<Delivery>(motivo);

        var datos = values ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var asunto = NotificationRules.Fill(template.Subject, datos);
        if (!asunto.IsOk) return Result.Rejected<Delivery>(asunto.Rejection!);
        var cuerpo = NotificationRules.Fill(template.Body, datos);
        if (!cuerpo.IsOk) return Result.Rejected<Delivery>(cuerpo.Rejection!);

        var id = Guid.NewGuid().ToString("n");
        var delivery = new Delivery(id, to, address!, template.Channel, template.Key,
            asunto.Value, cuerpo.Value, DeliveryStatus.Queued, Now, ProviderMessageId: null, StatusAtUtc: Now);

        _deliveries.Put(delivery);
        _idempotency.Remember("delivery", idem, id);
        return Result.Ok(delivery);
    }

    /// <summary>
    /// Anota lo que el proveedor cuenta de un envío suyo: entregado, rebotado, marcado como spam.
    /// </summary>
    /// <param name="providerEventId">El id del EVENTO del proveedor — no el nuestro, ni el del mensaje.</param>
    /// <param name="providerMessageId">A qué mensaje se refiere, en la numeración del proveedor.</param>
    /// <param name="status">A qué estado dice que llegó.</param>
    /// <remarks>
    /// <para><b>La idempotencia va por el id del evento del proveedor</b>, y no por uno nuestro,
    /// porque acá el que reintenta es él: los proveedores reentregan el mismo webhook durante
    /// días hasta ver un 2xx. Nuestro id no distingue una reentrega de un evento nuevo.</para>
    ///
    /// <para><b>Un evento que no aporta no es un error.</b> Devuelve el envío tal como está —si
    /// contestáramos un 4xx, el proveedor seguiría insistiendo con algo que ya procesamos.</para>
    /// </remarks>
    public Result<Delivery> RecordProviderEvent(
        string? providerEventId, string? providerMessageId, DeliveryStatus status)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(providerEventId))
            {
                return Rejection.Invalid($"{NotificationRules.CodePrefix}.provider_event_id_required",
                    "El evento del proveedor tiene que traer su propio identificador.");
            }
            if (string.IsNullOrWhiteSpace(providerMessageId))
            {
                return Rejection.Invalid($"{NotificationRules.CodePrefix}.provider_message_id_required",
                    "El evento no dice a qué mensaje se refiere.");
            }

            var llave = IdempotencyKey.Of(providerEventId!);
            if (_idempotency.Find("provider-event", llave) is { } yaVisto)
            {
                return _deliveries.Find(yaVisto) is { } previa
                    ? Result.Ok(previa)
                    : Rejection.Conflict($"{NotificationRules.CodePrefix}.idempotency_orphan",
                        "El evento ya se procesó pero el envío no está.");
            }

            if (_deliveries.FindByProviderMessageId(providerMessageId!) is not { } envio)
            {
                // Pasa de verdad: correos mandados desde otra cuenta del mismo proveedor, o de un
                // despliegue anterior. Se dice claro y el borde decide qué contestarle al
                // proveedor — lo que NO se puede hacer es apuntarlo contra un envío cualquiera.
                return Rejection.NotFound($"{NotificationRules.CodePrefix}.unknown_provider_message",
                    $"Ningún envío de este despliegue corresponde al mensaje {providerMessageId} del proveedor.");
            }

            var avanzado = NotificationRules.Advance(envio.Status, status);
            var actualizado = avanzado == envio.Status
                ? envio                                                     // fuera de orden o repetido
                : envio with { Status = avanzado, StatusAtUtc = Now };

            _deliveries.Put(actualizado);
            _idempotency.Remember("provider-event", llave, actualizado.Id);
            return Result.Ok(actualizado);
        }
    }

    public Result<Delivery> GetDelivery(string id)
        => _deliveries.Find(id) is { } d
            ? Result.Ok(d)
            : Rejection.NotFound($"{NotificationRules.CodePrefix}.delivery_not_found", $"No existe el envío {id}.");

    public Result<Page<Delivery>> ListDeliveries(Ref? to, int offset, int limit)
    {
        if (to is null)
        {
            // Sin destinatario esto sería un volcado del rastro de avisos de todo el mundo.
            return Rejection.Invalid($"{NotificationRules.CodePrefix}.recipient_required",
                "Hace falta filtrar por destinatario.");
        }

        var todas = _deliveries.ForRecipient(to)
            .OrderByDescending(d => d.AtUtc)
            .ThenBy(d => d.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<Delivery>(todas.Skip(offset).Take(limit).ToList(), todas.Count, offset));
    }
}
