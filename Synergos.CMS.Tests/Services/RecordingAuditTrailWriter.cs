using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Fake in-memory de <see cref="IAuditTrailWriter"/> para verificar el rastro de
/// acceso a PHI que emiten los seams clínicos del EHR-lite (OLA 5). Solo registra
/// los eventos escritos; las lecturas (GetRecent/GetByDateRange/GetById) operan
/// sobre el buffer in-memory. Suficiente para los tests de los stubs.
/// </summary>
internal sealed class RecordingAuditTrailWriter : IAuditTrailWriter
{
    public List<AuditEvent> Events { get; } = new();

    public Task WriteAsync(AuditEvent evt, CancellationToken cancellationToken)
    {
        Events.Add(evt);
        return Task.CompletedTask;
    }

    public IReadOnlyList<AuditEvent> GetRecent(int maxItems, string? actorEmailFilter = null, string? actionFilter = null)
        => Events
            .Where(e => actorEmailFilter is null || e.ActorEmail == actorEmailFilter)
            .Where(e => actionFilter is null || e.Action == actionFilter)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(maxItems)
            .ToList();

    public IReadOnlyList<AuditEvent> GetByDateRange(DateTime fromUtc, DateTime toUtc, int maxItems, string? actorEmailFilter = null, string? actionFilter = null)
        => Events
            .Where(e => e.OccurredAtUtc >= fromUtc && e.OccurredAtUtc <= toUtc)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(maxItems)
            .ToList();

    public AuditEvent? GetById(string id) => Events.FirstOrDefault(e => e.Id == id);
}
