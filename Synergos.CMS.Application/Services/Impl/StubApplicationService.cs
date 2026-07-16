using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IApplicationService"/> — agregado raíz STUB del expediente del
/// vertical Gobierno (doc gobierno.md §4), calcando <c>StubEventTicketingService</c> de
/// Eventos. Radica solicitudes (valida el trámite contra
/// <see cref="ITramiteCatalogProvider"/>, exige los campos requeridos del formulario
/// dinámico seccionado, calcula la tasa con <see cref="IGovFeeCalculator"/> y abre la
/// sesión de pago con <see cref="IPaymentProvider"/> solo si la tasa aplica) y es la
/// FUENTE DE VERDAD del estado de los expedientes: <see cref="ICaseTrackingProvider"/> /
/// <see cref="ICaseWorkflowService"/> / <see cref="IDocumentUploadService"/> lo leen y
/// mutan por composición (DIP), sin duplicar estado.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). REUSA el motor (no lo reinventa): la tasa es un pago
/// OPCIONAL vía <see cref="IPaymentProvider"/> (trámite gratuito = paso de pago OFF,
/// caso ya validado en Propiedades) y cada radicación deja un evento append-only
/// <c>gov.case-radicado</c> en <see cref="IAuditTrailWriter"/> (ADR 0037 — el rastro
/// forense es el diferenciador del dominio). <see cref="RadicarAsync"/> NO es
/// idempotente por diseño (cada radicación crea un expediente nuevo, como una orden);
/// la idempotencia vive en las decisiones (<c>StubCaseWorkflowService</c>). Siembra
/// expedientes en varios estados para que la demo muestre el ciclo de vida completo
/// (bandeja ciudadano + cola funcionario) sin radicar primero.
/// <para>
/// <b>T3 (doc 25 / ADR 0105):</b> el estado (caseId → <see cref="CaseState"/>) ya NO vive
/// en un diccionario del proceso sino detrás del seam <see cref="IJsonEntityStore"/> — con
/// el adapter FileSystem el expediente SOBREVIVE un reinicio del CMS. Cambio de BACKING
/// STORE, no de comportamiento.
/// </para>
/// </remarks>
public sealed class StubApplicationService : IApplicationService
{
    /// <summary>Etapa legible por estado (cara ciudadano/funcionario).</summary>
    private static readonly IReadOnlyDictionary<CaseStatus, string> StageLabels =
        new Dictionary<CaseStatus, string>
        {
            [CaseStatus.Radicado] = "Radicado",
            [CaseStatus.EnRevision] = "En revisión",
            [CaseStatus.Subsanacion] = "Información solicitada",
            [CaseStatus.Resuelto] = "Aprobado",
            [CaseStatus.Rechazado] = "Rechazado",
        };

    // Orden canónico de hitos del timeline (patrón GOV.UK: enviado → revisión →
    // decisión). El estado terminal (Resuelto/Rechazado) ocupa el hito "Decisión".
    private static readonly CaseStatus[] MilestoneOrder =
    {
        CaseStatus.Radicado,
        CaseStatus.EnRevision,
        CaseStatus.Resuelto,
    };

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // acentos es-CO legibles en disco
    };

    /// <summary>Familia de entidades de este motor en el store genérico (→ App_Data/syn-gov-cases/).</summary>
    private const string ResourceType = "gov-cases";

    /// <summary>Primer radicado de la secuencia (los seeds ocupan 001001..001006).</summary>
    private const int SeedSequenceFloor = 1006;

    private readonly ITramiteCatalogProvider _catalog;
    private readonly IGovFeeCalculator _fees;
    private readonly IPaymentProvider _payments;
    private readonly IAuditTrailWriter? _audit;
    private readonly IJsonEntityStore _store;
    private readonly ITransactionalNotifier? _notifier;
    private readonly Func<DateTimeOffset> _now;
    private int _radicadoSequence = SeedSequenceFloor;

    public StubApplicationService(
        ITramiteCatalogProvider catalog,
        IGovFeeCalculator fees,
        IPaymentProvider payments)
        : this(catalog, fees, payments, null, null, null)
    {
    }

    /// <summary>
    /// Ctor configurable: audit opcional (null = no-op, tests aislados sin mock
    /// obligatorio) + time source inyectable para determinismo en tests (ADR 0002).
    /// </summary>
    public StubApplicationService(
        ITramiteCatalogProvider catalog,
        IGovFeeCalculator fees,
        IPaymentProvider payments,
        IAuditTrailWriter? audit,
        Func<DateTimeOffset>? now)
        : this(catalog, fees, payments, audit, now, null)
    {
    }

    /// <summary>
    /// Ctor completo (T3): <paramref name="store"/> es el backing store durable
    /// (FileSystem en Web, InMemory en tests). Null = <see cref="InMemoryJsonEntityStore"/>
    /// → los ctors de arriba conservan EXACTO el comportamiento efímero de siempre.
    /// <paramref name="notifier"/> (T4) es opcional y va ÚLTIMO: null = no notificar
    /// (comportamiento pre-T4), así que los ctors de arriba quedan intactos.
    /// </summary>
    public StubApplicationService(
        ITramiteCatalogProvider catalog,
        IGovFeeCalculator fees,
        IPaymentProvider payments,
        IAuditTrailWriter? audit,
        Func<DateTimeOffset>? now,
        IJsonEntityStore? store,
        ITransactionalNotifier? notifier = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _fees = fees ?? throw new ArgumentNullException(nameof(fees));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _audit = audit;
        _store = store ?? new InMemoryJsonEntityStore();
        _notifier = notifier;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        Seed();
    }

    public async Task<RadicarResult> RadicarAsync(
        string tramiteId,
        IReadOnlyDictionary<string, string> answers,
        GovCitizen citizen,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tramiteId))
        {
            throw new ArgumentException("El trámite es obligatorio.", nameof(tramiteId));
        }
        if (answers is null)
        {
            throw new ArgumentException("Las respuestas del formulario son obligatorias.", nameof(answers));
        }
        if (citizen is null || string.IsNullOrWhiteSpace(citizen.Name) || string.IsNullOrWhiteSpace(citizen.Email))
        {
            throw new ArgumentException("El nombre y el email del solicitante son obligatorios.", nameof(citizen));
        }

        var detail = await _catalog.GetAsync(tramiteId.Trim(), cancellationToken)
            ?? throw new ArgumentException($"Trámite '{tramiteId.Trim()}' no encontrado.", nameof(tramiteId));

        // Formulario dinámico data-driven: exigir cada campo requerido de la definición
        // (recorre todas las secciones del formulario seccionado).
        foreach (var section in detail.FormDefinition.Sections)
        {
            foreach (var field in section.Fields)
            {
                if (field.Required
                    && (!answers.TryGetValue(field.Id, out var value) || string.IsNullOrWhiteSpace(value)))
                {
                    throw new ArgumentException(
                        $"El campo requerido '{field.Label}' ({field.Id}) está vacío.", nameof(answers));
                }
            }
        }

        var occurred = _now();
        var summary = detail.Summary;
        var quote = _fees.Calculate(summary.Id, summary.FeeMinor, summary.Currency);
        var caseId = $"case_{Guid.NewGuid():N}";
        var radicado = NextRadicado(occurred);

        // Tasa OPCIONAL: solo abre sesión de pago si el trámite cobra (Fee > 0).
        string? paymentSessionId = null;
        if (!quote.Exempt && quote.Amount > 0m)
        {
            var session = await _payments.CreateSessionAsync(
                new PaymentSessionRequest(
                    OrderReference: radicado,
                    Amount: quote.Amount,
                    Currency: quote.Currency,
                    Items: new[]
                    {
                        new PaymentLineItem(
                            Sku: summary.Id,
                            Description: $"Tasa — {summary.Name}",
                            UnitPrice: quote.Amount,
                            Quantity: 1),
                    },
                    CustomerEmail: citizen.Email.Trim()),
                cancellationToken);
            paymentSessionId = session.SessionId;
        }

        var cleanCitizen = new GovCitizen(
            citizen.Name.Trim(), citizen.Email.Trim(), citizen.DocumentId?.Trim(), citizen.Phone?.Trim());

        var state = new CaseState(
            CaseId: caseId,
            Radicado: radicado,
            TramiteId: summary.Id,
            TramiteName: summary.Name,
            Agency: summary.Agency,
            Citizen: cleanCitizen,
            FormData: new Dictionary<string, string>(answers, StringComparer.Ordinal),
            Documents: Array.Empty<CitizenDocumentRef>(),
            Status: CaseStatus.Radicado,
            Priority: CasePriority.Normal,
            SlaDays: summary.EstimatedDays,
            FeeMinor: quote.Amount,
            Currency: quote.Currency,
            RadicadoAt: occurred,
            History: new[]
            {
                new HistoryEntry(CaseStatus.Radicado, occurred, cleanCitizen.Email, "Solicitud radicada en línea."),
            },
            Decision: null);
        await WriteAsync(state, cancellationToken);

        // Rastro forense append-only del primer estado del expediente (ADR 0037).
        if (_audit is not null)
        {
            // best-effort: el expediente YA está radicado y persistido.
            await BestEffort.RunAsync(() => _audit.WriteAsync(
                    new AuditEvent(
                        Id: caseId,
                        OccurredAtUtc: occurred.UtcDateTime,
                        ActorEmail: cleanCitizen.Email,
                        ActorName: cleanCitizen.Name,
                        Action: "gov.case-radicado",
                        Resource: radicado,
                        Outcome: "success",
                        Detail: $"Expediente radicado para el trámite {summary.Id} (tasa {quote.Amount} {quote.Currency})."),
                    cancellationToken), cancellationToken);
        }

        // Avisarle al ciudadano que su trámite quedó radicado, con el radicado que debe
        // guardar (T4). Best-effort: un email caído JAMÁS puede tumbar un expediente ya
        // radicado y persistido.
        await NotificationEmission.SafeDispatchAsync(
            _notifier, BuildFiledNotification(state, quote), cancellationToken);

        return new RadicarResult(ToDetail(state, occurred), paymentSessionId);
    }

    /// <summary>
    /// El hecho "trámite radicado" para T4. DedupeKey default = gov.case.filed:{caseId} —
    /// el caseId identifica el hecho (una radicación = un expediente nuevo), así que no
    /// hace falta override. El destinatario es el ciudadano resuelto del formulario; su
    /// email es obligatorio en <see cref="RadicarAsync"/> (se valida arriba), así que aquí
    /// nunca es placeholder.
    /// </summary>
    private static NotificationEvent BuildFiledNotification(CaseState state, GovFeeQuote quote)
    {
        var cobra = !quote.Exempt && quote.Amount > 0m;
        return new NotificationEvent(
            Type: NotificationTypes.GovCaseFiled,
            SubjectId: state.CaseId,
            ToEmail: state.Citizen.Email,
            ToName: state.Citizen.Name,
            Code: state.Radicado,                      // el comprobante que el ciudadano guarda
            OccurredAt: state.RadicadoAt,
            Amount: cobra ? quote.Amount : null,
            Currency: cobra ? quote.Currency : null,
            Data: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Trámite"] = state.TramiteName,
                ["Entidad"] = state.Agency,
                ["Estado"] = StageLabels[state.Status],
            },
            ActionPath: $"/gobierno/tramites/{state.Radicado}");
    }

    // ── Superficie de composición (DIP) para tracking / workflow / documentos ──
    // El agregado es la fuente de verdad; los stubs hermanos leen/mutan por aquí
    // sin duplicar estado (mismo patrón StubEventTicketingService → Management).

    /// <summary>
    /// Devuelve el expediente por id de caso O por número de radicado, o null si no
    /// existe. Búsqueda case-insensitive. El timeline se proyecta relativo al "ahora".
    /// </summary>
    public CaseDetail? FindCase(string caseIdOrRadicado)
    {
        var state = Resolve(caseIdOrRadicado);
        return state is null ? null : ToDetail(state, _now());
    }

    /// <summary>Devuelve todos los expedientes (para armar las bandejas).</summary>
    public IReadOnlyList<CaseDetail> ListCases()
    {
        var now = _now();
        return Block(LoadAllAsync()).Select(s => ToDetail(s, now)).ToList();
    }

    /// <summary>
    /// Devuelve todos los expedientes junto con su entidad (agency) para la cola del
    /// funcionario, que filtra por entidad. La agency es interna al agregado (no viaja
    /// en <see cref="CaseDetail"/>), así que se expone aquí para la composición (DIP).
    /// </summary>
    public IReadOnlyList<(CaseDetail Case, string Agency)> ListCasesWithAgency()
    {
        var now = _now();
        return Block(LoadAllAsync()).Select(s => (ToDetail(s, now), s.Agency)).ToList();
    }

    /// <summary>
    /// Aplica una decisión YA VALIDADA por la máquina de estados
    /// (<c>StubCaseWorkflowService</c> es quien decide legalidad e idempotencia):
    /// cambia el estado, agrega la entrada al historial y registra la decisión terminal
    /// si el destino lo es. Devuelve el expediente actualizado. Lanza
    /// <see cref="ArgumentException"/> si el expediente no existe.
    /// </summary>
    public CaseDetail ApplyDecision(
        string caseIdOrRadicado,
        CaseStatus to,
        string actor,
        string note,
        DateTimeOffset occurredAt,
        CaseDecision? decision)
    {
        var state = Resolve(caseIdOrRadicado)
            ?? throw new ArgumentException($"Expediente '{caseIdOrRadicado}' no encontrado.", nameof(caseIdOrRadicado));

        var history = state.History.ToList();
        history.Add(new HistoryEntry(to, occurredAt, actor, note));
        var updated = state with { Status = to, History = history, Decision = decision ?? state.Decision };
        Block(WriteAsync(updated));
        return ToDetail(updated, occurredAt);
    }

    /// <summary>
    /// Adjunta un documento (ya aceptado) al expediente. Lanza
    /// <see cref="ArgumentException"/> si el expediente no existe.
    /// </summary>
    public void AttachDocument(string caseIdOrRadicado, CitizenDocumentRef document)
    {
        var state = Resolve(caseIdOrRadicado)
            ?? throw new ArgumentException($"Expediente '{caseIdOrRadicado}' no encontrado.", nameof(caseIdOrRadicado));

        var docs = state.Documents.ToList();
        docs.Add(document);
        Block(WriteAsync(state with { Documents = docs }));
    }

    // ── Acceso al store (deserialización defensiva) ─────────────────────
    //
    // NORMALIZACIÓN DE CLAVE — decisión deliberada. El diccionario que este motor tenía
    // antes usaba StringComparer.OrdinalIgnoreCase, o sea el lookup del expediente SIEMPRE
    // fue case-insensitive. El seam IJsonEntityStore, en cambio, es case-SENSITIVE en
    // InMemoryJsonEntityStore (ConcurrentDictionary Ordinal) pero case-INsensitive de facto
    // en FileSystemJsonEntityStore (la clave es un nombre de archivo y NTFS no distingue
    // mayúsculas) — es decir, las dos impls divergirían. Canonizar la clave a
    // ToUpperInvariant en el ÚNICO punto que toca el store (StoreKey) restaura el
    // comportamiento OrdinalIgnoreCase original y lo hace idéntico en ambas impls.
    // El casing original del CaseId se conserva intacto DENTRO del JSON (CaseState.CaseId);
    // solo se canoniza la clave de direccionamiento.
    private static string StoreKey(string caseId) => caseId.Trim().ToUpperInvariant();

    private async Task<CaseState?> LoadAsync(string? caseId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            return null;
        }
        var json = await _store.ReadAsync(ResourceType, StoreKey(caseId), cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try { return JsonSerializer.Deserialize<CaseState>(json, _json); }
        catch (JsonException) { return null; }   // archivo corrupto → como si no existiera
    }

    private async Task<List<CaseState>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var raws = await _store.ListAsync(ResourceType, cancellationToken);
        var cases = new List<CaseState>(raws.Count);
        foreach (var json in raws)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            CaseState? state;
            try { state = JsonSerializer.Deserialize<CaseState>(json, _json); }
            catch (JsonException) { continue; }
            if (state is not null) cases.Add(state);
        }
        return cases;
    }

    private Task WriteAsync(CaseState state, CancellationToken cancellationToken = default)
        => _store.WriteAsync(ResourceType, StoreKey(state.CaseId), JsonSerializer.Serialize(state, _json), cancellationToken);

    // El store es async-only pero la superficie de composición de este agregado (FindCase /
    // ListCases / ApplyDecision / AttachDocument) es SÍNCRONA y la consumen tres stubs
    // hermanos (tracking / workflow / documentos) que están fuera del alcance de este
    // cambio. Bloquear aquí preserva esos call-sites intactos: es seguro porque no hay
    // SynchronizationContext en ASP.NET Core (cero riesgo de deadlock) y ambos adapters
    // resuelven en microsegundos (InMemory completa síncrono; FileSystem lee un archivo).
    private static T Block<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private static void Block(Task task) => task.GetAwaiter().GetResult();

    private CaseState? Resolve(string caseIdOrRadicado)
    {
        if (string.IsNullOrWhiteSpace(caseIdOrRadicado))
        {
            return null;
        }
        var key = caseIdOrRadicado.Trim();
        var byId = Block(LoadAsync(key));
        if (byId is not null)
        {
            return byId;
        }
        return Block(LoadAllAsync()).FirstOrDefault(c =>
            string.Equals(c.Radicado, key, StringComparison.OrdinalIgnoreCase));
    }

    // ── Proyección estado → CaseDetail (timeline done/current/pending + SLA) ──

    private static CaseDetail ToDetail(CaseState s, DateTimeOffset now) => new(
        CaseId: s.CaseId,
        Radicado: s.Radicado,
        TramiteId: s.TramiteId,
        TramiteName: s.TramiteName,
        Citizen: s.Citizen,
        FormData: s.FormData,
        Documents: s.Documents,
        Status: s.Status,
        CurrentStage: StageLabels[s.Status],
        Priority: s.Priority,
        SlaDaysLeft: SlaDaysLeft(s, now),
        FeeMinor: s.FeeMinor,
        Currency: s.Currency,
        RadicadoAt: s.RadicadoAt,
        Timeline: BuildTimeline(s),
        Decision: s.Decision);

    // Días de SLA restantes = (radicación + días estimados) − hoy. Terminal ⇒ 0.
    private static int SlaDaysLeft(CaseState s, DateTimeOffset now)
    {
        if (s.Status is CaseStatus.Resuelto or CaseStatus.Rechazado)
        {
            return 0;
        }
        var due = s.RadicadoAt.AddDays(s.SlaDays);
        return (int)Math.Ceiling((due - now).TotalDays);
    }

    // Timeline como 3 hitos (radicado/revisión/decisión) con estado visual:
    // done = ya ocurrió, current = estado actual, pending = aún no alcanzado.
    // Subsanación es un hito insertado (ocurrió) entre revisión y decisión.
    private static IReadOnlyList<CaseTimelineEntry> BuildTimeline(CaseState s)
    {
        var reached = s.History.Select(h => h.Status).ToHashSet();
        var entries = new List<CaseTimelineEntry>();

        foreach (var milestone in MilestoneOrder)
        {
            // El hito "Decisión" (Resuelto) se marca current también si el expediente
            // terminó en Rechazado — comparten el hito final.
            var isDecisionMilestone = milestone == CaseStatus.Resuelto;
            var terminalReached = s.Status is CaseStatus.Resuelto or CaseStatus.Rechazado;

            var lastForMilestone = s.History.LastOrDefault(h =>
                h.Status == milestone
                || (isDecisionMilestone && h.Status == CaseStatus.Rechazado));

            string state;
            DateTimeOffset date;
            string label;
            string note;

            if (isDecisionMilestone)
            {
                label = "Decisión";
                if (terminalReached && lastForMilestone is not null)
                {
                    state = "current";
                    date = lastForMilestone.OccurredAt;
                    note = lastForMilestone.Note;
                }
                else
                {
                    state = "pending";
                    date = s.RadicadoAt;
                    note = string.Empty;
                }
            }
            else if (reached.Contains(milestone))
            {
                var hist = s.History.Last(h => h.Status == milestone);
                label = MilestoneLabel(milestone);
                date = hist.OccurredAt;
                note = hist.Note;
                state = s.Status == milestone && !terminalReached ? "current" : "done";
            }
            else
            {
                label = MilestoneLabel(milestone);
                state = "pending";
                date = s.RadicadoAt;
                note = string.Empty;
            }

            entries.Add(new CaseTimelineEntry(
                Id: milestone.ToString().ToLowerInvariant(),
                Label: label,
                Date: date,
                State: state,
                Note: note));
        }

        // Si el expediente está en subsanación, ese es el hito "current"; la revisión
        // queda done y la decisión pending. Insertamos el hito de subsanación.
        if (s.Status == CaseStatus.Subsanacion)
        {
            var sub = s.History.Last(h => h.Status == CaseStatus.Subsanacion);
            entries.Insert(2, new CaseTimelineEntry(
                Id: "subsanacion",
                Label: "Información solicitada",
                Date: sub.OccurredAt,
                State: "current",
                Note: sub.Note));
        }

        return entries;
    }

    private static string MilestoneLabel(CaseStatus s) => s switch
    {
        CaseStatus.Radicado => "Radicado",
        CaseStatus.EnRevision => "En revisión",
        _ => "Decisión",
    };

    private string NextRadicado(DateTimeOffset occurredAt)
        => $"SG-{occurredAt.Year}-{Interlocked.Increment(ref _radicadoSequence):D6}";

    // Secuencia del radicado. Con el estado en memoria reiniciaba en SeedSequenceFloor
    // cada boot — inofensivo porque el estado moría con el proceso. Ahora que el
    // expediente SOBREVIVE el reinicio, reiniciarla emitiría un radicado DUPLICADO (y el
    // radicado es clave de búsqueda pública), así que la secuencia se rehidrata del máximo
    // ya persistido. Sobre un store vacío/InMemory el máximo son los seeds (…001006) =
    // SeedSequenceFloor ⇒ comportamiento IDÉNTICO al de antes.
    private void RestoreSequence()
    {
        var max = SeedSequenceFloor;
        foreach (var state in Block(LoadAllAsync()))
        {
            var tail = state.Radicado.LastIndexOf('-');
            if (tail >= 0
                && int.TryParse(state.Radicado.AsSpan(tail + 1), out var seq)
                && seq > max)
            {
                max = seq;
            }
        }
        _radicadoSequence = max;
    }

    // ── Seed: expedientes en varios estados con prioridad/SLA (demo del ciclo) ──

    private void Seed()
    {
        var baseDate = new DateTimeOffset(2026, 6, 20, 9, 0, 0, TimeSpan.FromHours(-5));
        const string officer = "funcionario@entidad.gov.co";

        // 1) Radicado — recién llega a la cola (prioridad normal).
        SeedCase(
            caseId: "case-1001",
            radicado: "SG-2026-001001",
            tramiteId: "trm-certificado-residencia",
            tramiteName: "Certificado de residencia",
            agency: "Alcaldía Municipal",
            citizen: new GovCitizen("María Fernanda López", "maria.lopez@correo.co", "52841937", "+57 310 555 0101"),
            formData: new Dictionary<string, string>
            {
                ["nombreCompleto"] = "María Fernanda López",
                ["cedula"] = "52841937",
                ["direccion"] = "Cra 15 # 82-33",
                ["barrio"] = "El Lago",
                ["correo"] = "maria.lopez@correo.co",
            },
            priority: CasePriority.Normal,
            slaDays: 2,
            feeMinor: 0m,
            radicadoAt: baseDate.AddDays(8),
            history: new[]
            {
                new HistoryEntry(CaseStatus.Radicado, baseDate.AddDays(8), "maria.lopez@correo.co", "Solicitud radicada en línea."),
            },
            decision: null);

        // 2) En revisión — el funcionario la tomó (prioridad alta).
        SeedCase(
            caseId: "case-1002",
            radicado: "SG-2026-001002",
            tramiteId: "trm-licencia-conduccion",
            tramiteName: "Renovación de licencia de conducción",
            agency: "Secretaría de Movilidad",
            citizen: new GovCitizen("Carlos Andrés Pérez", "carlos.perez@correo.co", "80234561", "+57 315 555 0202"),
            formData: new Dictionary<string, string>
            {
                ["nombreCompleto"] = "Carlos Andrés Pérez",
                ["cedula"] = "80234561",
                ["categoria"] = "B1",
                ["examenMedico"] = "true",
                ["correo"] = "carlos.perez@correo.co",
            },
            priority: CasePriority.High,
            slaDays: 5,
            feeMinor: 95_000m,
            radicadoAt: baseDate.AddDays(5),
            history: new[]
            {
                new HistoryEntry(CaseStatus.Radicado, baseDate.AddDays(5), "carlos.perez@correo.co", "Solicitud radicada en línea."),
                new HistoryEntry(CaseStatus.EnRevision, baseDate.AddDays(6), officer, "Expediente asignado para revisión."),
            },
            decision: null);

        // 3) Subsanación — falta un documento; el ciudadano debe responder (alta).
        SeedCase(
            caseId: "case-1003",
            radicado: "SG-2026-001003",
            tramiteId: "trm-pasaporte",
            tramiteName: "Expedición de pasaporte",
            agency: "Cancillería",
            citizen: new GovCitizen("Juliana Ríos", "juliana.rios@correo.co", "1020456789", "+57 300 555 0303"),
            formData: new Dictionary<string, string>
            {
                ["nombreCompleto"] = "Juliana Ríos",
                ["cedula"] = "1020456789",
                ["fechaNacimiento"] = "1994-03-12",
                ["ciudad"] = "Bogotá",
                ["correo"] = "juliana.rios@correo.co",
            },
            priority: CasePriority.High,
            slaDays: 8,
            feeMinor: 189_000m,
            radicadoAt: baseDate.AddDays(2),
            history: new[]
            {
                new HistoryEntry(CaseStatus.Radicado, baseDate.AddDays(2), "juliana.rios@correo.co", "Solicitud radicada en línea."),
                new HistoryEntry(CaseStatus.EnRevision, baseDate.AddDays(3), officer, "Expediente asignado para revisión."),
                new HistoryEntry(CaseStatus.Subsanacion, baseDate.AddDays(4), officer, "La foto no cumple el fondo blanco — adjuntar una nueva."),
            },
            decision: null,
            documents: new[]
            {
                new CitizenDocumentRef("doc-1003-01", "case-1003", "cedula.pdf", "accepted", baseDate.AddDays(2)),
            });

        // 4) Resuelto (aprobado) — ciclo completo favorable (prioridad normal).
        SeedCase(
            caseId: "case-1004",
            radicado: "SG-2026-001004",
            tramiteId: "trm-registro-mercantil",
            tramiteName: "Registro mercantil de empresa",
            agency: "Cámara de Comercio",
            citizen: new GovCitizen("Andrés Felipe Torres", "andres.torres@correo.co", "900123456", "+57 320 555 0404"),
            formData: new Dictionary<string, string>
            {
                ["razonSocial"] = "Café La Palma S.A.S.",
                ["nit"] = "900123456",
                ["actividad"] = "5613 — Expendio de comidas",
                ["representante"] = "Andrés Felipe Torres",
                ["correo"] = "andres.torres@correo.co",
            },
            priority: CasePriority.Normal,
            slaDays: 3,
            feeMinor: 320_000m,
            radicadoAt: baseDate.AddDays(-6),
            history: new[]
            {
                new HistoryEntry(CaseStatus.Radicado, baseDate.AddDays(-6), "andres.torres@correo.co", "Solicitud radicada en línea."),
                new HistoryEntry(CaseStatus.EnRevision, baseDate.AddDays(-5), officer, "Expediente asignado para revisión."),
                new HistoryEntry(CaseStatus.Resuelto, baseDate.AddDays(-3), officer, "Registro mercantil expedido — certificado disponible."),
            },
            decision: new CaseDecision("approve", "Registro mercantil expedido — certificado disponible.", baseDate.AddDays(-3), officer),
            documents: new[]
            {
                new CitizenDocumentRef("doc-1004-01", "case-1004", "acta-constitucion.pdf", "accepted", baseDate.AddDays(-6)),
            });

        // 5) Rechazado — ciclo completo desfavorable (prioridad low).
        SeedCase(
            caseId: "case-1005",
            radicado: "SG-2026-001005",
            tramiteId: "trm-afiliacion-salud",
            tramiteName: "Afiliación al régimen subsidiado de salud",
            agency: "Secretaría de Salud",
            citizen: new GovCitizen("Luz Dary Gómez", "luz.gomez@correo.co", "39765432", "+57 311 555 0505"),
            formData: new Dictionary<string, string>
            {
                ["nombreCompleto"] = "Luz Dary Gómez",
                ["cedula"] = "39765432",
                ["sisben"] = "C5",
                ["eps"] = "Nueva EPS",
                ["correo"] = "luz.gomez@correo.co",
            },
            priority: CasePriority.Low,
            slaDays: 10,
            feeMinor: 0m,
            radicadoAt: baseDate.AddDays(-10),
            history: new[]
            {
                new HistoryEntry(CaseStatus.Radicado, baseDate.AddDays(-10), "luz.gomez@correo.co", "Solicitud radicada en línea."),
                new HistoryEntry(CaseStatus.EnRevision, baseDate.AddDays(-9), officer, "Expediente asignado para revisión."),
                new HistoryEntry(CaseStatus.Rechazado, baseDate.AddDays(-7), officer, "Puntaje SISBÉN fuera del rango del régimen subsidiado."),
            },
            decision: new CaseDecision("reject", "Puntaje SISBÉN fuera del rango del régimen subsidiado.", baseDate.AddDays(-7), officer));

        // 6) Radicado — segundo en la cola, prioridad normal (más volumen para el funcionario).
        SeedCase(
            caseId: "case-1006",
            radicado: "SG-2026-001006",
            tramiteId: "trm-subsidio-vivienda",
            tramiteName: "Solicitud de subsidio de vivienda",
            agency: "Ministerio de Vivienda",
            citizen: new GovCitizen("Diego Martínez", "diego.martinez@correo.co", "1015789456", "+57 312 555 0606"),
            formData: new Dictionary<string, string>
            {
                ["nombreCompleto"] = "Diego Martínez",
                ["cedula"] = "1015789456",
                ["integrantes"] = "4",
                ["ingresos"] = "1800000",
                ["modalidad"] = "Compra de vivienda nueva",
                ["correo"] = "diego.martinez@correo.co",
            },
            priority: CasePriority.Normal,
            slaDays: 30,
            feeMinor: 0m,
            radicadoAt: baseDate.AddDays(7),
            history: new[]
            {
                new HistoryEntry(CaseStatus.Radicado, baseDate.AddDays(7), "diego.martinez@correo.co", "Solicitud radicada en línea."),
            },
            decision: null);

        RestoreSequence();
    }

    private void SeedCase(
        string caseId,
        string radicado,
        string tramiteId,
        string tramiteName,
        string agency,
        GovCitizen citizen,
        IReadOnlyDictionary<string, string> formData,
        CasePriority priority,
        int slaDays,
        decimal feeMinor,
        DateTimeOffset radicadoAt,
        IReadOnlyList<HistoryEntry> history,
        CaseDecision? decision,
        IReadOnlyList<CitizenDocumentRef>? documents = null)
    {
        // Sembrar SOLO si el expediente no existe ya en el store. Con el estado en memoria
        // el seed siempre partía de cero; ahora, sobre un store durable, re-sembrar en cada
        // boot PISARÍA las mutaciones reales del expediente sembrado (una decisión del
        // funcionario sobre case-1001 volvería a "Radicado" tras un reinicio) — justo la
        // durabilidad que este cambio persigue. Sobre un store vacío/InMemory siembra los
        // 6 igual que siempre ⇒ comportamiento IDÉNTICO al de antes.
        if (Block(LoadAsync(caseId)) is not null)
        {
            return;
        }

        Block(WriteAsync(new CaseState(
            CaseId: caseId,
            Radicado: radicado,
            TramiteId: tramiteId,
            TramiteName: tramiteName,
            Agency: agency,
            Citizen: citizen,
            FormData: formData,
            Documents: documents ?? Array.Empty<CitizenDocumentRef>(),
            Status: history[^1].Status,
            Priority: priority,
            SlaDays: slaDays,
            FeeMinor: feeMinor,
            Currency: "COP",
            RadicadoAt: radicadoAt,
            History: history,
            Decision: decision)));
    }
}

/// <summary>Una transición cruda del historial interno (fuente del timeline proyectado).</summary>
/// <remarks>
/// Top-level (no anidado) para round-trip limpio con System.Text.Json — records
/// posicionales deserializan por ctor. Mismo patrón que <c>PersistedOrder</c> (T1).
/// </remarks>
internal sealed record HistoryEntry(CaseStatus Status, DateTimeOffset OccurredAt, string Actor, string Note);

/// <summary>
/// La forma SERIALIZADA del expediente (el estado interno del agregado tras T3). Guarda
/// MÁS que el <see cref="CaseDetail"/> público: <see cref="Agency"/> y <see cref="SlaDays"/>
/// no viajan en la proyección pero son necesarios para reconstruirla tras un reinicio.
/// </summary>
internal sealed record CaseState(
    string CaseId,
    string Radicado,
    string TramiteId,
    string TramiteName,
    string Agency,
    GovCitizen Citizen,
    IReadOnlyDictionary<string, string> FormData,
    IReadOnlyList<CitizenDocumentRef> Documents,
    CaseStatus Status,
    CasePriority Priority,
    int SlaDays,
    decimal FeeMinor,
    string Currency,
    DateTimeOffset RadicadoAt,
    IReadOnlyList<HistoryEntry> History,
    CaseDecision? Decision);
