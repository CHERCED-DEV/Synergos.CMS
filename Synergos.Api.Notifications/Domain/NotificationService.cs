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

    /// <summary>Los envíos que tienen un reintento hablando con el proveedor ahora mismo.</summary>
    /// <remarks>
    /// <para><b>Existe porque el barrido no es uno solo.</b> Cada orquestador levanta el suyo
    /// (ver <c>Bff.Core.DeliverySweeper</c>), así que dos pueden pedir el reintento del mismo
    /// envío en el mismo segundo. El cerrojo no alcanza: el envío se hace <i>fuera</i> de él —a
    /// propósito, para no dejar la capacidad entera esperando a un tercero—, y en esa ventana los
    /// dos ven el registro en <c>Queued</c> y los dos mandan. El destinatario recibe el aviso dos
    /// veces.</para>
    ///
    /// <para><b>Y por qué no un estado <c>Sending</c> persistido</b>, que sería la respuesta
    /// obvia: si el proceso muere entre marcar y enviar, el envío queda en un estado del que nadie
    /// lo saca — ningún barrido lo mira, porque ya no está <c>Queued</c>. La marca en memoria se
    /// pierde con el proceso, que es exactamente lo que se quiere: al reiniciar, el envío vuelve a
    /// ser reintentable.</para>
    ///
    /// <para>No protege de dos <i>instancias</i> de esta capacidad — nada acá lo hace, y por eso
    /// hay una sola (<c>CLAUDE.md</c> §11, <c>JsonCollectionStore</c>).</para>
    /// </remarks>
    private readonly HashSet<string> _enVuelo = new(StringComparer.Ordinal);

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

    // ── Lo que el barrido necesita (HU #29) ─────────────────────────────────

    /// <summary>Los envíos que quedaron a medias, del más viejo al más nuevo.</summary>
    /// <remarks>
    /// <para>Sin filtro por destinatario a propósito, al revés que <see cref="ListDeliveries"/>:
    /// quien pregunta acá no es una persona mirando lo suyo, es <b>el barrido</b> — y lo que
    /// necesita es justo lo que aquella consulta prohíbe, todo lo que está colgado sin importar
    /// de quién.</para>
    ///
    /// <para>Del más viejo primero: si el barrido se corta a la mitad, lo atendido es lo que más
    /// lleva esperando.</para>
    /// </remarks>
    public Result<Page<Delivery>> ListQueued(int offset, int limit)
    {
        lock (_gate)
        {
            var todos = _deliveries.WithStatus(DeliveryStatus.Queued)
                .OrderBy(d => d.AtUtc)
                .ThenBy(d => d.Id, StringComparer.Ordinal)
                .ToList();

            return Result.Ok(new Page<Delivery>(todos.Skip(offset).Take(limit).ToList(), todos.Count, offset));
        }
    }

    /// <summary>
    /// Vuelve a intentar un envío que quedó en <c>Queued</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>No hace falta la llave original.</b> El registro guarda todo lo que el envío
    /// necesita —canal, dirección, asunto y cuerpo ya rellenados—, así que reintentar es
    /// reenviar <i>ese</i> registro. Pedirle al barrido que conservara la llave del llamador
    /// original le obligaría a guardar un estado que no es suyo.</para>
    ///
    /// <para><b>El contador sube SIEMPRE que se intenta</b>, salga como salga. Es lo que le
    /// permite a quien barre saber cuándo rendirse — y si solo subiera al fallar, un envío que
    /// alterna fallo y silencio no llegaría nunca al techo.</para>
    ///
    /// <para><b>Un envío a la vez, aunque quien pida sean varios</b>: ver <see cref="_enVuelo"/>.
    /// Dos barridos pidiendo el mismo reintento a la vez mandarían dos avisos idénticos.</para>
    /// </remarks>
    public async Task<Result<Delivery>> RetryAsync(string id, CancellationToken ct = default)
    {
        Delivery actual;
        lock (_gate)
        {
            var hallado = _deliveries.Find(id);
            if (hallado is null)
            {
                return Rejection.NotFound($"{NotificationRules.CodePrefix}.delivery_not_found", $"No existe el envío {id}.");
            }
            if (hallado.Status != DeliveryStatus.Queued)
            {
                // Ya salió, ya rebotó o ya se rindió: reintentarlo mandaría un segundo aviso.
                return Rejection.Conflict($"{NotificationRules.CodePrefix}.not_retryable",
                    $"El envío {id} está en {hallado.Status} y no se reintenta.");
            }
            if (!_enVuelo.Add(hallado.Id))
            {
                // Transitorio: el otro intento está EN CURSO, no es que este envío sea irreintentable.
                // Quien barre tiene que poder volver por él en la siguiente vuelta.
                return Rejection.Unavailable($"{NotificationRules.CodePrefix}.retry_in_flight",
                    $"El envío {id} ya tiene un reintento en curso.");
            }
            actual = hallado;
        }

        try
        {
            Result<string> envio;
            try
            {
                envio = await _sender.SendAsync(actual.Channel, actual.Address, actual.Subject, actual.Body,
                    actual.Id, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                envio = Result.Rejected<string>(NotificationRules.TransportUnavailable(ex.GetType().Name));
            }

            lock (_gate)
            {
                var vigente = _deliveries.Find(actual.Id) ?? actual;
                var intentos = vigente.Attempts + 1;

                if (!envio.IsOk)
                {
                    var fallido = vigente with
                    {
                        Attempts = intentos,
                        LastError = envio.Rejection!.Message,
                        // Definitivo → Failed: no se vuelve. Transitorio → sigue en Queued.
                        Status = envio.Rejection.IsTransient ? vigente.Status : DeliveryStatus.Failed,
                        StatusAtUtc = Now,
                    };
                    _deliveries.Put(fallido);
                    return Result.Rejected<Delivery>(envio.Rejection);
                }

                var aceptada = vigente with
                {
                    Status = NotificationRules.Advance(vigente.Status, DeliveryStatus.Accepted),
                    ProviderMessageId = envio.Value,
                    Attempts = intentos,
                    LastError = null,
                    StatusAtUtc = Now,
                };
                _deliveries.Put(aceptada);
                return Result.Ok(aceptada);
            }
        }
        finally
        {
            // Pase lo que pase. Un envío que se queda marcado como en vuelo no lo reintenta nadie
            // nunca más, y el síntoma sería un `Queued` eterno — el mismo que esto viene a cerrar.
            lock (_gate) { _enVuelo.Remove(actual.Id); }
        }
    }

    /// <summary>
    /// Da por perdido un envío. <b>Con la causa, que es lo que lo vuelve accionable.</b>
    /// </summary>
    /// <remarks>
    /// Quien decide rendirse NO es esta capacidad: es el barrido de <c>Bff.Core</c>, que es donde
    /// vive la máquina de reintentar y rendirse. Acá solo se anota — poner el techo también acá
    /// duplicaría esa lógica, y el día que las dos difirieran nadie sabría cuál manda
    /// (<c>CLAUDE.md</c> §11).
    /// </remarks>
    public Result<Delivery> GiveUp(string id, string? reason)
    {
        lock (_gate)
        {
            var hallado = _deliveries.Find(id);
            if (hallado is null)
            {
                return Rejection.NotFound($"{NotificationRules.CodePrefix}.delivery_not_found", $"No existe el envío {id}.");
            }
            if (hallado.Status == DeliveryStatus.GivenUp) return Result.Ok(hallado);   // idempotente
            if (hallado.Status != DeliveryStatus.Queued)
            {
                return Rejection.Conflict($"{NotificationRules.CodePrefix}.not_retryable",
                    $"El envío {id} está en {hallado.Status}: no hay nada que abandonar.");
            }

            var rendido = hallado with
            {
                Status = DeliveryStatus.GivenUp,
                LastError = string.IsNullOrWhiteSpace(reason) ? hallado.LastError : reason,
                StatusAtUtc = Now,
            };
            _deliveries.Put(rendido);
            return Result.Ok(rendido);
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
