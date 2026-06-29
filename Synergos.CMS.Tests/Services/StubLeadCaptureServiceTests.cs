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
