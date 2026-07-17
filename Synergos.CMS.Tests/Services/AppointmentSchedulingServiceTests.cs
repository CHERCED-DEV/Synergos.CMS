using Synergos.CMS.Application.Services;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="AppointmentSchedulingService"/> (ADR 0098 H2b) — lógica
/// pura de conflicto/overbooking, sin IO. Cubre sin-solape, solape estricto,
/// solape dentro/fuera de tolerancia, citas canceladas y lista vacía.
/// </summary>
public sealed class AppointmentSchedulingServiceTests
{
    private readonly AppointmentSchedulingService _sut = new();
    private static readonly DateTime D = new(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

    private static AppointmentSlot Slot(DateTime start, DateTime end, string status = "booked") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start, end, status, null, DateTime.UtcNow, Guid.NewGuid());

    [Fact]
    public void EmptyExisting_NoConflict() =>
        Assert.False(_sut.HasConflict(System.Array.Empty<AppointmentSlot>(), D, D.AddHours(1), 0));

    [Fact]
    public void Adjacent_NoOverlap_NoConflict()
    {
        var existing = new[] { Slot(D, D.AddHours(1)) };
        Assert.False(_sut.HasConflict(existing, D.AddHours(1), D.AddHours(2), 0));   // 10-11 vs 9-10
    }

    [Fact]
    public void Overlap_NoTolerance_Conflict()
    {
        var existing = new[] { Slot(D, D.AddHours(1)) };
        Assert.True(_sut.HasConflict(existing, D.AddMinutes(30), D.AddMinutes(90), 0));   // 30 min solape
    }

    [Fact]
    public void Overlap_WithinTolerance_NoConflict()
    {
        var existing = new[] { Slot(D, D.AddHours(1)) };
        // 9:50–10:50 → 10 min de solape, tolerancia 15 → permitido.
        Assert.False(_sut.HasConflict(existing, D.AddMinutes(50), D.AddMinutes(110), 15));
    }

    [Fact]
    public void Overlap_BeyondTolerance_Conflict()
    {
        var existing = new[] { Slot(D, D.AddHours(1)) };
        // 9:40–10:40 → 20 min de solape, tolerancia 15 → conflicto.
        Assert.True(_sut.HasConflict(existing, D.AddMinutes(40), D.AddMinutes(100), 15));
    }

    [Fact]
    public void CancelledAppointment_Ignored()
    {
        var existing = new[] { Slot(D, D.AddHours(1), status: "cancelled") };
        Assert.False(_sut.HasConflict(existing, D.AddMinutes(30), D.AddMinutes(90), 0));
    }

    [Fact]
    public void Overlaps_Helper()
    {
        Assert.True(AppointmentSchedulingService.Overlaps(D, D.AddHours(1), D.AddMinutes(30), D.AddMinutes(90)));
        Assert.False(AppointmentSchedulingService.Overlaps(D, D.AddHours(1), D.AddHours(1), D.AddHours(2)));
    }
}
