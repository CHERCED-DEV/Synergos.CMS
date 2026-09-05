using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Las notificaciones de actos administrativos, guardadas de este lado (HU #62).
/// </summary>
/// <remarks>
/// <para><b>Es el camino del clon limpio y también un camino legítimo de producción.</b> Una
/// entidad pequeña que no despliega el árbol de servicios sigue pudiendo notificar y registrar
/// accesos; lo que no tiene es la afirmación verificable de identidad, y por eso todo lo que
/// registra dice <c>CmsSession</c> — que es la verdad sobre ello.</para>
///
/// <para><b>Durable desde el primer día</b> (<c>IJsonEntityStore</c>, ADR 0105). Un registro de
/// notificaciones que se pierde al reiniciar no sirve para lo único que existe: sostener que un
/// término empezó.</para>
/// </remarks>
public sealed class StubGovActNotificationService : IGovActNotificationService
{
    /// <summary>Familia de entidades en el store genérico (→ App_Data/syn-gov-notifications/).</summary>
    public const string ResourceType = "gov-notifications";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // acentos es-CO legibles en disco
    };

    private readonly IJsonEntityStore _store;
    private readonly Func<DateTimeOffset> _now;
    private readonly SemaphoreSlim _mutate = new(1, 1);

    public StubGovActNotificationService(IJsonEntityStore store, Func<DateTimeOffset>? now = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<GovActNotification> NotifyAsync(
        string caseId,
        string radicado,
        Guid citizenMemberKey,
        string title,
        string body,
        string? documentRef = null,
        DateTimeOffset? acknowledgeBeforeUtc = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caseId)) throw new ArgumentException("El expediente es obligatorio.", nameof(caseId));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("El acto necesita título.", nameof(title));
        if (citizenMemberKey == Guid.Empty) throw new ArgumentException("Hace falta el ciudadano.", nameof(citizenMemberKey));

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Un acto se notifica UNA vez. Re-notificarlo devuelve el que ya está: abrir uno
            // segundo le daría al ciudadano dos plazos para el mismo acto, y al que recurre tarde,
            // un argumento.
            var previa = (await TodasAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(n => string.Equals(n.CaseId, caseId.Trim(), StringComparison.Ordinal)
                                  && string.Equals(n.Title, title.Trim(), StringComparison.Ordinal));
            if (previa is not null) return previa;

            var notificacion = new GovActNotification(
                Id: $"not_{Guid.NewGuid():N}",
                CaseId: caseId.Trim(),
                Radicado: (radicado ?? string.Empty).Trim(),
                Title: title.Trim(),
                Body: (body ?? string.Empty).Trim(),
                DocumentRef: string.IsNullOrWhiteSpace(documentRef) ? null : documentRef.Trim(),
                NotifiedAtUtc: _now(),
                AcknowledgeBeforeUtc: acknowledgeBeforeUtc);

            await GuardarAsync(notificacion, citizenMemberKey, cancellationToken).ConfigureAwait(false);
            return notificacion;
        }
        finally { _mutate.Release(); }
    }

    public async Task<GovActNotification> AcknowledgeAsync(
        string notificationId, Guid memberKey, CancellationToken cancellationToken = default)
    {
        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var guardada = await LeerAsync(notificationId, cancellationToken).ConfigureAwait(false)
                ?? throw new ArgumentException($"Notificación '{notificationId}' no encontrada.", nameof(notificationId));

            if (guardada.Owner != memberKey)
            {
                // Hacia fuera no se distingue de «no existe» —el borde contesta 403— pero acá sí,
                // porque el rastro tiene que poder decir que alguien intentó abrir lo ajeno.
                throw new GovActNotAddresseeException();
            }

            var notificacion = guardada.Notification;

            // El PRIMER acceso es el que cuenta. Si el segundo pisara la fecha, el término se
            // correría solo cada vez que el ciudadano vuelve a mirar — a su favor, que es lo
            // contrario de lo que la notificación pretende.
            if (notificacion.Opened) return notificacion;

            var ahora = _now();
            if (notificacion.AcknowledgeBeforeUtc is { } limite && ahora > limite)
            {
                throw new InvalidOperationException(
                    $"El plazo para registrar el acceso venció el {limite:O}.");
            }

            var abierta = notificacion with
            {
                OpenedAtUtc = ahora,
                OpenedBy = memberKey,
                // Sin identidad verificable, esto es lo más fuerte que se puede afirmar — y es
                // honesto: significa «nuestra propia sesión da fe».
                OpenedWith = GovActAssertions.CmsSession,
            };

            await GuardarAsync(abierta, memberKey, cancellationToken).ConfigureAwait(false);
            return abierta;
        }
        finally { _mutate.Release(); }
    }

    public async Task<IReadOnlyList<GovActNotification>> GetForCaseAsync(
        string caseId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caseId)) return Array.Empty<GovActNotification>();

        return (await TodasAsync(cancellationToken).ConfigureAwait(false))
            .Where(n => string.Equals(n.CaseId, caseId.Trim(), StringComparison.Ordinal))
            .OrderByDescending(n => n.NotifiedAtUtc)
            .ToList();
    }

    public async Task<IReadOnlyList<GovActNotification>> GetForCitizenAsync(
        Guid memberKey, CancellationToken cancellationToken = default)
    {
        if (memberKey == Guid.Empty) return Array.Empty<GovActNotification>();

        return (await TodasGuardadasAsync(cancellationToken).ConfigureAwait(false))
            .Where(g => g.Owner == memberKey)
            .Select(g => g.Notification)
            .OrderByDescending(n => n.NotifiedAtUtc)
            .ToList();
    }

    // ── Lo que se guarda ────────────────────────────────────────────────────

    /// <summary>
    /// La notificación más su dueño.
    /// </summary>
    /// <remarks>
    /// El dueño NO va en <see cref="GovActNotification"/> a propósito: ese tipo cruza hacia la
    /// vista, y la llave del Member de otra persona no tiene por qué salir del servidor. Es la
    /// lección de #47 —el seudónimo del comprador acabó en pantalla— aplicada antes de que pase.
    /// </remarks>
    private sealed record Guardada(GovActNotification Notification, Guid Owner);

    private Task GuardarAsync(GovActNotification n, Guid owner, CancellationToken ct)
        => _store.WriteAsync(ResourceType, n.Id, JsonSerializer.Serialize(new Guardada(n, owner), Json), ct);

    private async Task<Guardada?> LeerAsync(string? id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var json = await _store.ReadAsync(ResourceType, id.Trim(), ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Guardada>(json, Json); }
        catch (JsonException) { return null; }   // corrupto → como si no existiera
    }

    private async Task<List<Guardada>> TodasGuardadasAsync(CancellationToken ct)
    {
        var crudas = await _store.ListAsync(ResourceType, ct).ConfigureAwait(false);
        var todas = new List<Guardada>(crudas.Count);
        foreach (var json in crudas)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            Guardada? g;
            try { g = JsonSerializer.Deserialize<Guardada>(json, Json); }
            catch (JsonException) { continue; }
            if (g?.Notification is not null) todas.Add(g);
        }
        return todas;
    }

    private async Task<List<GovActNotification>> TodasAsync(CancellationToken ct)
        => (await TodasGuardadasAsync(ct).ConfigureAwait(false)).Select(g => g.Notification).ToList();
}
