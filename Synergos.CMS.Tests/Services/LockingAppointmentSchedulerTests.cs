using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="LockingAppointmentScheduler"/> (ADR 0098 H2b). Usa un
/// <see cref="IPhiStore"/> en memoria. Cubre reserva, anti-overbooking (mismo
/// doctor), distinto doctor, rango inválido, cancelar (libera el slot) y listado.
/// </summary>
public sealed class LockingAppointmentSchedulerTests
{
    private readonly InMemoryPhiStore _store = new();
    private readonly Guid _doctor = Guid.NewGuid();
    private static readonly DateTime D = new(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

    private LockingAppointmentScheduler BuildSut(int overbooking = 0) =>
        new(_store, new AppointmentSchedulingService(),
            Options.Create(new HealthcareSettings { MaxOverbookingMinutes = overbooking }));

    private AppointmentRequest Req(DateTime start, DateTime end, Guid? doctor = null) =>
        new(Guid.NewGuid(), doctor ?? _doctor, start, end, Guid.NewGuid());

    [Fact]
    public async Task Book_Happy_ReturnsBookedSlot()
    {
        var r = await BuildSut().BookAsync(Req(D, D.AddHours(1)), CancellationToken.None);
        Assert.True(r.Booked);
        Assert.NotNull(r.Slot);
        Assert.Equal("booked", r.Slot!.Status);
    }

    [Fact]
    public async Task Book_OverlapSameDoctor_Conflict()
    {
        var sut = BuildSut();
        await sut.BookAsync(Req(D, D.AddHours(1)), CancellationToken.None);

        var r = await sut.BookAsync(Req(D.AddMinutes(30), D.AddMinutes(90)), CancellationToken.None);

        Assert.False(r.Booked);
        Assert.Equal("overbooking-conflict", r.ConflictReason);
    }

    [Fact]
    public async Task Book_OverlapDifferentDoctor_Ok()
    {
        var sut = BuildSut();
        await sut.BookAsync(Req(D, D.AddHours(1)), CancellationToken.None);

        var r = await sut.BookAsync(Req(D.AddMinutes(30), D.AddMinutes(90), doctor: Guid.NewGuid()), CancellationToken.None);

        Assert.True(r.Booked);   // otro doctor → sin conflicto
    }

    [Fact]
    public async Task Book_InvalidRange_Rejected()
    {
        var r = await BuildSut().BookAsync(Req(D.AddHours(1), D), CancellationToken.None);
        Assert.False(r.Booked);
        Assert.Equal("invalid-range", r.ConflictReason);
    }

    [Fact]
    public async Task Cancel_FreesSlot_AllowsRebook()
    {
        var sut = BuildSut();
        var first = await sut.BookAsync(Req(D, D.AddHours(1)), CancellationToken.None);

        var cancelled = await sut.CancelAsync(first.Slot!.AppointmentId, "paciente reagenda", CancellationToken.None);
        var rebook = await sut.BookAsync(Req(D, D.AddHours(1)), CancellationToken.None);

        Assert.True(cancelled);
        Assert.True(rebook.Booked);   // el slot cancelado ya no bloquea
    }

    [Fact]
    public async Task Cancel_Missing_ReturnsFalse()
    {
        Assert.False(await BuildSut().CancelAsync(Guid.NewGuid(), "n/a", CancellationToken.None));
    }

    [Fact]
    public async Task List_FiltersByDoctor_ExcludesCancelledByDefault()
    {
        var sut = BuildSut();
        await sut.BookAsync(Req(D, D.AddHours(1)), CancellationToken.None);
        var other = await sut.BookAsync(Req(D.AddHours(2), D.AddHours(3), doctor: Guid.NewGuid()), CancellationToken.None);
        await sut.CancelAsync(other.Slot!.AppointmentId, "x", CancellationToken.None);

        var mine = await sut.ListAsync(new AppointmentQuery(DoctorKey: _doctor), CancellationToken.None);

        var only = Assert.Single(mine);
        Assert.Equal(_doctor, only.DoctorKey);
        Assert.Equal("booked", only.Status);
    }

    private sealed class InMemoryPhiStore : IPhiStore
    {
        private readonly Dictionary<string, string> _data = new();
        private static string K(string rt, Guid key) => rt + "/" + key.ToString("N");

        public Task WriteAsync(string resourceType, Guid key, string json, CancellationToken ct)
        {
            _data[K(resourceType, key)] = json;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string resourceType, Guid key, CancellationToken ct) =>
            Task.FromResult(_data.TryGetValue(K(resourceType, key), out var v) ? v : null);

        public Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken ct) =>
            Task.FromResult((IReadOnlyList<string>)_data
                .Where(kv => kv.Key.StartsWith(resourceType + "/", StringComparison.Ordinal))
                .Select(kv => kv.Value).ToList());

        public Task<bool> DeleteAsync(string resourceType, Guid key, CancellationToken ct) =>
            Task.FromResult(_data.Remove(K(resourceType, key)));
    }
}
