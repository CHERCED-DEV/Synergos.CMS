using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre la durabilidad de los tres seams del portal inmobiliario: visitas
/// agendadas, leads del agente y búsquedas guardadas con su alerta.
/// </summary>
/// <remarks>
/// <para>Los tres vivían en un <c>ConcurrentDictionary</c> del proceso mientras
/// órdenes, pagos, reservas, expedientes, tickets y matrículas ya estaban en
/// disco (ADR 0105). Un reinicio borraba las visitas que un comprador tenía
/// agendadas —aunque la reserva subyacente siguiera confirmada—, el embudo
/// completo que un agente estaba trabajando, y los criterios que resuelven la
/// alerta de una búsqueda guardada.</para>
///
/// <para>El proxy de reinicio es el mismo que usa el repo en
/// <c>TrackingAndReturnsDurabilityTests</c>: <b>construir un servicio nuevo
/// sobre el MISMO store</b>. Si el estado sobrevive ese cambio, sobrevive un
/// reinicio del proceso.</para>
///
/// <para>Las dos propiedades que sostienen todo lo demás y que nadie verificaba:
/// <b>cada seam tiene su propio espacio</b> (leer un documento contra la forma
/// de otro dominio no falla — devuelve otra cosa en silencio) y <b>un documento
/// corrupto se salta</b> (un JSON ilegible no puede tumbar la agenda de un
/// listado ni el kanban entero de un agente).</para>
/// </remarks>
public sealed class RealtyDurabilityTests
{
    private const string VisitsNs = "realty-visits";
    private const string LeadsNs = "realty-leads";
    private const string SearchesNs = "realty-saved-searches";

    private static readonly DateTimeOffset Clock = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static VisitContact Contact() => new("Camila Restrepo", "camila@synergos.co", "+57 300 555 0000");

    // ── Visitas agendadas ────────────────────────────────────────────────

    private static StubVisitSchedulingService Visits(
        IJsonEntityStore store,
        IReservationService? reservations = null,
        string ns = VisitsNs)
        => new(reservations ?? new StubReservationService(), () => Clock, store, ns);

    [Fact] // empty: sin nada agendado, la agenda entera está disponible
    public async Task Sin_visitas_agendadas_todos_los_slots_estan_disponibles()
    {
        var store = new InMemoryJsonEntityStore();

        var slots = await Visits(store).GetSlotsAsync("prop-001");

        Assert.NotEmpty(slots);
        Assert.All(slots, s => Assert.True(s.Available));
    }

    [Fact] // happy + survives-restart
    public async Task Una_visita_agendada_SOBREVIVE_a_un_servicio_nuevo_sobre_el_mismo_store()
    {
        var store = new InMemoryJsonEntityStore();

        var before = Visits(store);
        var slots = await before.GetSlotsAsync("prop-001");
        var booked = await before.BookAsync("prop-001", slots[0].Id, Contact());

        var after = Visits(store);
        var afterSlots = await after.GetSlotsAsync("prop-001");

        Assert.Equal(ReservationStatus.Confirmed.ToString(), booked.Status);
        Assert.False(afterSlots.Single(s => s.Id == slots[0].Id).Available);
    }

    [Fact] // idempotent-after-restart: no se re-aparta el slot ni se duplica la visita
    public async Task Re_agendar_tras_el_reinicio_devuelve_la_MISMA_visita_sin_tocar_el_motor()
    {
        var store = new InMemoryJsonEntityStore();
        var reservations = new CountingReservationService(new StubReservationService());

        var before = Visits(store, reservations);
        var slots = await before.GetSlotsAsync("prop-001");
        var first = await before.BookAsync("prop-001", slots[0].Id, Contact());
        var holdsAfterFirst = reservations.Holds;

        var after = Visits(store, reservations);
        var second = await after.BookAsync("prop-001", slots[0].Id, Contact());

        Assert.Equal(first.VisitId, second.VisitId);
        // La idempotencia debe cortar ANTES del motor: un hold de más deja un
        // recurso apartado que nadie va a usar ni liberar.
        Assert.Equal(holdsAfterFirst, reservations.Holds);
    }

    [Fact] // per-namespace isolation
    public async Task Una_visita_de_OTRO_espacio_no_ocupa_el_slot()
    {
        var store = new InMemoryJsonEntityStore();

        var portal = Visits(store);
        var slots = await portal.GetSlotsAsync("prop-001");
        await portal.BookAsync("prop-001", slots[0].Id, Contact());

        // Mismo store, otra familia de entidades: el slot sigue libre allí.
        var otro = Visits(store, ns: "realty-visits-otro-origen");
        Assert.All(await otro.GetSlotsAsync("prop-001"), s => Assert.True(s.Available));
    }

    [Fact] // per-namespace isolation: la forma de OTRO dominio no se cuela como visita
    public async Task Un_documento_con_la_forma_de_otro_dominio_no_pasa_por_visita()
    {
        var store = new InMemoryJsonEntityStore();
        var svc = Visits(store);
        var slots = await svc.GetSlotsAsync("prop-001");

        // Un lead escrito en el espacio de las visitas: deserializa "casi" bien
        // (JSON válido, campos ausentes → null) y ese es justo el fallo silencioso
        // que separar los espacios evita.
        await store.WriteAsync(
            VisitsNs,
            $"prop-001/{slots[0].Id}".ToLowerInvariant(),
            """{"LeadId":"lead_x","AgentId":"laura-gomez","Status":0}""");

        Assert.All(await svc.GetSlotsAsync("prop-001"), s => Assert.True(s.Available));
    }

    [Fact] // corrupt-document-is-skipped
    public async Task Una_visita_corrupta_se_salta_y_el_slot_se_puede_volver_a_agendar()
    {
        var store = new InMemoryJsonEntityStore();
        var svc = Visits(store);
        var slots = await svc.GetSlotsAsync("prop-001");

        await store.WriteAsync(VisitsNs, $"prop-001/{slots[0].Id}".ToLowerInvariant(), "{ esto no es json");

        // Ni GetSlots ni Book revientan: el documento ilegible se trata como ausente.
        Assert.All(await svc.GetSlotsAsync("prop-001"), s => Assert.True(s.Available));
        var result = await svc.BookAsync("prop-001", slots[0].Id, Contact());
        Assert.Equal(ReservationStatus.Confirmed.ToString(), result.Status);
    }

    // ── Leads del agente ─────────────────────────────────────────────────

    private const string NoAgent = "agente-desconocido";

    private static StubLeadCaptureService Leads(
        IJsonEntityStore store,
        IAuditTrailWriter? audit = null,
        string ns = LeadsNs)
        => new(audit, null, null, () => Clock, store, ns);

    [Fact] // empty: sin leads capturados, el kanban del agente está vacío
    public async Task Sin_leads_el_kanban_del_agente_esta_vacio()
    {
        var store = new InMemoryJsonEntityStore();

        Assert.Empty(await Leads(store).GetForAgentAsync(NoAgent));
    }

    [Fact] // happy + survives-restart
    public async Task Un_lead_capturado_SOBREVIVE_a_un_servicio_nuevo()
    {
        var store = new InMemoryJsonEntityStore();

        var before = Leads(store);
        var captured = await before.CaptureAsync("prop-001", Contact(), "Me interesa el apartamento");

        var after = Leads(store);
        var kanban = await after.GetForAgentAsync(NoAgent);

        var lead = Assert.Single(kanban);
        Assert.Equal(captured.LeadId, lead.LeadId);
        Assert.Equal(LeadStatus.Nuevo, lead.Status);
        Assert.Equal("Me interesa el apartamento", lead.Message);
    }

    [Fact] // survives-restart: el avance del embudo también persiste
    public async Task El_avance_del_lead_persiste_tras_el_reinicio()
    {
        var store = new InMemoryJsonEntityStore();

        var before = Leads(store);
        var captured = await before.CaptureAsync("prop-001", Contact(), "Interesado");
        await before.AdvanceLeadAsync(captured.LeadId, LeadStatus.Contactado);

        var after = Leads(store);
        var lead = Assert.Single(await after.GetForAgentAsync(NoAgent));

        Assert.Equal(LeadStatus.Contactado, lead.Status);
    }

    [Fact] // idempotent-after-restart: re-avanzar al estado actual no re-audita
    public async Task Avanzar_al_estado_actual_tras_el_reinicio_sigue_siendo_idempotente()
    {
        var store = new InMemoryJsonEntityStore();
        var audit = new CapturingAudit();

        var before = Leads(store, audit);
        var captured = await before.CaptureAsync("prop-001", Contact(), "Interesado");
        await before.AdvanceLeadAsync(captured.LeadId, LeadStatus.Contactado);
        var events = audit.Actions.Count;

        var after = Leads(store, audit);
        var again = await after.AdvanceLeadAsync(captured.LeadId, LeadStatus.Contactado);

        Assert.Equal(LeadStatus.Contactado, again.Status);
        Assert.Equal(events, audit.Actions.Count); // ni un evento de más
    }

    [Fact] // per-namespace isolation
    public async Task Los_leads_de_OTRO_espacio_no_salen_en_el_kanban()
    {
        var store = new InMemoryJsonEntityStore();

        await Leads(store).CaptureAsync("prop-001", Contact(), "Interesado");

        // Mismo store, otra familia: el agente no ve leads que no son suyos.
        Assert.Empty(await Leads(store, ns: "realty-leads-otro-origen").GetForAgentAsync(NoAgent));
    }

    [Fact] // per-namespace isolation: un lead del otro espacio tampoco se puede avanzar
    public async Task Un_lead_de_OTRO_espacio_no_se_resuelve_por_id()
    {
        var store = new InMemoryJsonEntityStore();
        var captured = await Leads(store).CaptureAsync("prop-001", Contact(), "Interesado");

        var otro = Leads(store, ns: "realty-leads-otro-origen");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            otro.AdvanceLeadAsync(captured.LeadId, LeadStatus.Contactado));
    }

    [Fact] // corrupt-document-is-skipped: un lead ilegible no tumba el embudo entero
    public async Task Un_lead_corrupto_se_salta_y_el_resto_del_kanban_sigue_en_pie()
    {
        var store = new InMemoryJsonEntityStore();
        var svc = Leads(store);
        var sano = await svc.CaptureAsync("prop-001", Contact(), "Interesado");

        await store.WriteAsync(LeadsNs, "lead_corrupto", "{ esto no es json");
        await store.WriteAsync(LeadsNs, "lead_vacio", "null");

        var kanban = await svc.GetForAgentAsync(NoAgent);

        var lead = Assert.Single(kanban);
        Assert.Equal(sano.LeadId, lead.LeadId);
    }

    // ── Búsquedas guardadas + alerta ─────────────────────────────────────

    private static StubSavedSearchService Searches(IJsonEntityStore store, string ns = SearchesNs)
        => new(new StubUserCollection(() => Clock), new StubPropertyCatalogProvider(), () => Clock, store, ns);

    [Fact] // empty: una búsqueda que nunca se guardó no resuelve
    public async Task Una_busqueda_inexistente_no_resuelve_su_alerta()
    {
        var store = new InMemoryJsonEntityStore();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Searches(store).GetMatchesAsync("search_nope"));
    }

    [Fact] // happy + survives-restart: la ALERTA es lo que se pierde en un reinicio
    public async Task La_alerta_de_una_busqueda_guardada_SOBREVIVE_a_un_servicio_nuevo()
    {
        var store = new InMemoryJsonEntityStore();

        var before = Searches(store);
        var saved = await before.SaveAsync("camila@synergos.co", new PropertyQuery(Type: "apartamento"), "Aptos");

        // Servicio nuevo — y colección de usuario nueva: lo único compartido es el
        // store. Antes, aquí GetMatches lanzaba "búsqueda no encontrada" para una
        // búsqueda que el usuario seguía teniendo guardada.
        var after = Searches(store);
        var matches = await after.GetMatchesAsync(saved.Id);

        Assert.Equal(saved.Id, matches.SearchId);
        Assert.True(matches.Count > 0);
        Assert.All(matches.Listings, l => Assert.Equal("apartamento", l.Type));
    }

    [Fact] // idempotent-after-restart: el id determinista sigue siendo el mismo
    public async Task Guardar_la_misma_busqueda_tras_el_reinicio_devuelve_el_MISMO_id()
    {
        var store = new InMemoryJsonEntityStore();
        var criteria = new PropertyQuery(Type: "casa", Location: "Medellín");

        var first = await Searches(store).SaveAsync("camila@synergos.co", criteria);
        var second = await Searches(store).SaveAsync("camila@synergos.co", criteria);

        // Mismo id → una sola búsqueda, no dos alertas para los mismos criterios.
        Assert.Equal(first.Id, second.Id);
        var matches = await Searches(store).GetMatchesAsync(second.Id);
        Assert.Equal(first.Id, matches.SearchId);
    }

    [Fact] // per-namespace isolation
    public async Task Una_busqueda_de_OTRO_espacio_no_resuelve_su_alerta()
    {
        var store = new InMemoryJsonEntityStore();
        var saved = await Searches(store).SaveAsync("camila@synergos.co", new PropertyQuery(Type: "apartamento"));

        var otro = Searches(store, "realty-saved-searches-otro-origen");
        await Assert.ThrowsAsync<ArgumentException>(() => otro.GetMatchesAsync(saved.Id));
    }

    [Fact] // corrupt-document-is-skipped: la excepción sigue siendo la del contrato
    public async Task Unos_criterios_corruptos_se_tratan_como_ausentes_y_no_revientan()
    {
        var store = new InMemoryJsonEntityStore();
        var svc = Searches(store);
        var saved = await svc.SaveAsync("camila@synergos.co", new PropertyQuery(Type: "apartamento"));

        await store.WriteAsync(SearchesNs, saved.Id, "{ esto no es json");

        // ArgumentException (lo que el contrato promete), NO una JsonException que
        // se filtre hasta el controller como un 500.
        await Assert.ThrowsAsync<ArgumentException>(() => svc.GetMatchesAsync(saved.Id));
    }

    // ── Fakes hand-written (evita el trap NSubstitute Returns-inside-Returns) ──

    private sealed class CapturingAudit : IAuditTrailWriter
    {
        public List<string> Actions { get; } = new();

        public Task WriteAsync(AuditEvent evt, CancellationToken cancellationToken)
        {
            Actions.Add(evt.Action);
            return Task.CompletedTask;
        }

        public IReadOnlyList<AuditEvent> GetRecent(int maxItems, string? actorEmailFilter = null, string? actionFilter = null)
            => Array.Empty<AuditEvent>();

        public IReadOnlyList<AuditEvent> GetByDateRange(DateTime fromUtc, DateTime toUtc, int maxItems, string? actorEmailFilter = null, string? actionFilter = null)
            => Array.Empty<AuditEvent>();

        public AuditEvent? GetById(string id) => null;
    }

    // Cuenta los holds para probar que la idempotencia corta ANTES del motor de
    // reservas: un hold de más deja un recurso apartado que nadie libera.
    private sealed class CountingReservationService : IReservationService
    {
        private readonly IReservationService _inner;

        public CountingReservationService(IReservationService inner) => _inner = inner;

        public int Holds { get; private set; }

        public Task<Reservation> HoldAsync(ReservationRequest request, CancellationToken cancellationToken = default)
        {
            Holds++;
            return _inner.HoldAsync(request, cancellationToken);
        }

        public Task<Reservation> HoldItemAsync(TravelItemReservationRequest request, CancellationToken cancellationToken = default)
        {
            Holds++;
            return _inner.HoldItemAsync(request, cancellationToken);
        }

        public Task<Reservation> ConfirmAsync(string reservationId, string paymentSessionId, CancellationToken cancellationToken = default)
            => _inner.ConfirmAsync(reservationId, paymentSessionId, cancellationToken);

        public Task<Reservation> CancelAsync(string reservationId, string reason, CancellationToken cancellationToken = default)
            => _inner.CancelAsync(reservationId, reason, cancellationToken);

        public Task<Reservation?> GetAsync(string reservationId, CancellationToken cancellationToken = default)
            => _inner.GetAsync(reservationId, cancellationToken);

        public Task<int> ExpireStaleHoldsAsync(CancellationToken cancellationToken = default)
            => _inner.ExpireStaleHoldsAsync(cancellationToken);
    }
}
