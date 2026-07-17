using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubLeadCaptureService"/> (seam <see cref="ILeadCaptureService"/>,
/// captura de leads del portal inmobiliario): los 4 casos canónicos (ADR 0075) —
/// empty/inválido (listado vacío) / happy (genera leadId) / filter (mensaje o
/// contacto faltante) / idempotent (cada captura es un lead nuevo) — más la
/// verificación del REUSO de IAuditTrailWriter + IAnalyticsTracker (ADRs 0037/0067).
/// </summary>
public class StubLeadCaptureServiceTests
{
    private static readonly DateTimeOffset Clock = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static VisitContact Contact() => new("Camila Restrepo", "camila@synergos.co", "+57 300 555 0000");

    [Fact] // inválido: listado vacío lanza
    public async Task Capture_NoListing_Throws()
    {
        var svc = new StubLeadCaptureService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CaptureAsync(string.Empty, Contact(), "Hola"));
    }

    [Fact] // happy: captura genera un leadId no vacío
    public async Task Capture_Happy_ReturnsLeadId()
    {
        var svc = new StubLeadCaptureService();
        var result = await svc.CaptureAsync("prop-001", Contact(), "Me interesa, ¿está disponible?");

        Assert.False(string.IsNullOrWhiteSpace(result.LeadId));
        Assert.StartsWith("lead_", result.LeadId);
    }

    [Fact] // filter: mensaje faltante lanza
    public async Task Capture_NoMessage_Throws()
    {
        var svc = new StubLeadCaptureService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CaptureAsync("prop-001", Contact(), "   "));
    }

    [Fact] // filter: contacto incompleto lanza
    public async Task Capture_BadContact_Throws()
    {
        var svc = new StubLeadCaptureService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.CaptureAsync("prop-001", new VisitContact("", "", null), "Hola"));
    }

    [Fact] // idempotent (semántica): cada captura es un lead nuevo, no colisiona
    public async Task Capture_EachCallIsNewLead()
    {
        var svc = new StubLeadCaptureService();
        var a = await svc.CaptureAsync("prop-001", Contact(), "Mensaje A");
        var b = await svc.CaptureAsync("prop-001", Contact(), "Mensaje B");

        Assert.NotEqual(a.LeadId, b.LeadId);
    }

    [Fact] // reuso: el lead deja rastro en el audit trail + emite el evento analytics
    public async Task Capture_WritesAuditAndAnalytics()
    {
        var audit = new CapturingAuditTrailWriter();
        var analytics = new CapturingAnalyticsTracker();
        var svc = new StubLeadCaptureService(audit, analytics, () => Clock);

        var result = await svc.CaptureAsync("prop-001", Contact(), "Interesado");

        Assert.Single(audit.Events);
        Assert.Equal("realty.lead-captured", audit.Events[0].Action);
        Assert.Equal(result.LeadId, audit.Events[0].Id);
        Assert.Equal("prop-001", audit.Events[0].Resource);

        Assert.Single(analytics.Events);
        Assert.Equal("realty.lead-captured", analytics.Events[0]);
    }

    // ── OLA 4 §2.6 — mini-CRM del agente (kanban + avance auditado) ─────

    [Fact] // happy: el lead capturado queda asignado al agente del inmueble y sale en su kanban
    public async Task GetForAgent_ReturnsCapturedLead()
    {
        var catalog = new StubPropertyCatalogProvider();
        var svc = new StubLeadCaptureService(null, null, catalog, () => Clock);

        var captured = await svc.CaptureAsync("prop-001", Contact(), "Interesado en Chicó");

        // prop-001 → agente "Laura Gómez" → agentId "laura-gomez".
        var leads = await svc.GetForAgentAsync("laura-gomez");
        Assert.Contains(leads, l => l.LeadId == captured.LeadId && l.Status == LeadStatus.Nuevo);
    }

    [Fact] // empty: agente desconocido → lista vacía (sin lanzar)
    public async Task GetForAgent_Unknown_ReturnsEmpty()
    {
        var svc = new StubLeadCaptureService();
        Assert.Empty(await svc.GetForAgentAsync("nadie"));
        Assert.Empty(await svc.GetForAgentAsync(""));
    }

    [Fact] // happy: avanzar cambia el estado y deja rastro en el audit
    public async Task Advance_ChangesStatus_AndAudits()
    {
        var audit = new CapturingAuditTrailWriter();
        var catalog = new StubPropertyCatalogProvider();
        var svc = new StubLeadCaptureService(audit, null, catalog, () => Clock);

        var captured = await svc.CaptureAsync("prop-002", Contact(), "Interesado en Laureles");
        var beforeCount = audit.Events.Count;

        var advanced = await svc.AdvanceLeadAsync(captured.LeadId, LeadStatus.Contactado);

        Assert.Equal(LeadStatus.Contactado, advanced.Status);
        Assert.Equal(beforeCount + 1, audit.Events.Count);
        Assert.Equal("realty.lead-advanced", audit.Events[^1].Action);

        var leads = await svc.GetForAgentAsync("andres-mejia");
        Assert.Contains(leads, l => l.LeadId == captured.LeadId && l.Status == LeadStatus.Contactado);
    }

    [Fact] // idempotent: avanzar al estado actual no re-audita
    public async Task Advance_SameStatus_IsIdempotent()
    {
        var audit = new CapturingAuditTrailWriter();
        var catalog = new StubPropertyCatalogProvider();
        var svc = new StubLeadCaptureService(audit, null, catalog, () => Clock);

        var captured = await svc.CaptureAsync("prop-001", Contact(), "Hola");
        var count = audit.Events.Count;

        var r = await svc.AdvanceLeadAsync(captured.LeadId, LeadStatus.Nuevo); // ya está en Nuevo
        Assert.Equal(LeadStatus.Nuevo, r.Status);
        Assert.Equal(count, audit.Events.Count); // sin evento nuevo
    }

    [Fact] // inválido: avanzar un lead inexistente lanza
    public async Task Advance_UnknownLead_Throws()
    {
        var svc = new StubLeadCaptureService();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            svc.AdvanceLeadAsync("lead_nope", LeadStatus.Cerrado));
    }

    // ── Fakes hand-written (evita el trap NSubstitute Returns-inside-Returns) ──

    private sealed class CapturingAuditTrailWriter : IAuditTrailWriter
    {
        public List<AuditEvent> Events { get; } = new();

        public Task WriteAsync(AuditEvent evt, CancellationToken cancellationToken)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }

        public IReadOnlyList<AuditEvent> GetRecent(int maxItems, string? actorEmailFilter = null, string? actionFilter = null)
            => Events;

        public IReadOnlyList<AuditEvent> GetByDateRange(DateTime fromUtc, DateTime toUtc, int maxItems, string? actorEmailFilter = null, string? actionFilter = null)
            => Events;

        public AuditEvent? GetById(string id) => null;
    }

    private sealed class CapturingAnalyticsTracker : IAnalyticsTracker
    {
        public List<string> Events { get; } = new();

        public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
            => Events.Add(eventName);
    }
}
