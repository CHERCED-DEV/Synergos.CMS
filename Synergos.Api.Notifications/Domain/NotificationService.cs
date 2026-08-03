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

    public Result<Delivery> Send(
        Ref to, string? address, string? templateKey,
        IReadOnlyDictionary<string, string>? values, IdempotencyKey idem)
    {
        lock (_gate)
        {
            // Antes que nada: si esta llave ya mandó algo, NO se manda otra vez. Es el caso que
            // más duele de todos — un reintento tras un timeout le llega a la persona como un
            // segundo correo idéntico.
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

            var desde = Now - NotificationRules.RateWindow;
            var recientes = _deliveries.ForRecipient(to).Count(d => d.AtUtc >= desde);
            motivo = NotificationRules.CheckRate(recientes);
            if (motivo is not null) return Result.Rejected<Delivery>(motivo);

            var datos = values ?? new Dictionary<string, string>(StringComparer.Ordinal);
            var asunto = NotificationRules.Fill(template.Subject, datos);
            if (!asunto.IsOk) return Result.Rejected<Delivery>(asunto.Rejection!);
            var cuerpo = NotificationRules.Fill(template.Body, datos);
            if (!cuerpo.IsOk) return Result.Rejected<Delivery>(cuerpo.Rejection!);

            var enviado = _sender.Send(template.Channel, address!, asunto.Value, cuerpo.Value);

            // El envío se registra HAYA SALIDO O NO. Un fallo de transporte que no deja rastro
            // es la peor combinación: la persona no recibió nada y el sistema no sabe que le
            // debe un aviso.
            var id = Guid.NewGuid().ToString("n");
            var delivery = new Delivery(id, to, address!, template.Channel, template.Key,
                asunto.Value, cuerpo.Value, enviado ? DeliveryStatus.Sent : DeliveryStatus.Failed, Now);

            _deliveries.Put(delivery);
            _idempotency.Remember("delivery", idem, id);
            return Result.Ok(delivery);
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
