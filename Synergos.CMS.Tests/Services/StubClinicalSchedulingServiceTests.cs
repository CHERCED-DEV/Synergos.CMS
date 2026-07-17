using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubClinicalSchedulingService"/> (seam
/// <see cref="IClinicalSchedulingService"/>, agenda del EHR-lite — OLA 5): los 4
/// casos canónicos (ADR 0075) — empty / happy / filter / idempotent — ejerciendo el
/// REUSO REAL del motor: compone <see cref="StubReservationService"/> +
/// <see cref="StubPaymentProvider"/> (cita = ítem reservable polimórfico, flujo
/// HoldItem → pagar → Confirm idéntico a hoteles/aerolíneas).
/// </summary>
public class StubClinicalSchedulingServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 29, 7, 0, 0, DateTimeKind.Utc); // lunes

    private static (IClinicalSchedulingService Svc, IReservationService Reservations) Make(bool seed = false)
    {
        var reservations = new StubReservationService();
        var payments = new StubPaymentProvider();
        var doctors = new StubDoctorDirectory();
        var patients = new StubPatientRegistry();
        var svc = new StubClinicalSchedulingService(reservations, payments, doctors, patients, () => Now, seed);
        return (svc, reservations);
    }

    private static BookAppointmentRequest Req(DateTime? slot = null)
        => new("pat-camila-restrepo", "doc-ana-rios", slot ?? Now.AddHours(2));

    [Fact] // empty: día sin citas → lista vacía (no lanza)
    public async Task GetByDate_NoAppointments_ReturnsEmpty()
    {
        var (svc, _) = Make(seed: false);

        var list = await svc.GetByDateAsync(new DateOnly(2030, 1, 1));

        Assert.Empty(list);
    }

    [Fact] // happy: Book aparta el slot con el motor de reservas, paga y confirma
    public async Task Book_ReusesReservationEngine_AndConfirms()
    {
        var (svc, reservations) = Make(seed: false);

        var appt = await svc.BookAsync(Req());

        Assert.Equal("booked", appt.Status);
        Assert.False(string.IsNullOrWhiteSpace(appt.ReservationId));
        Assert.Equal("Camila Restrepo", appt.PatientName);
        Assert.Equal("Medicina Interna", appt.Specialty);

        // La reserva subyacente existe y quedó Confirmed (máquina de estados reusada).
        var reservation = await reservations.GetAsync(appt.ReservationId);
        Assert.NotNull(reservation);
        Assert.Equal(ReservationStatus.Confirmed, reservation!.Status);
        Assert.False(string.IsNullOrWhiteSpace(reservation.PaymentSessionId)); // ligada a la sesión de pago
    }

    [Fact] // happy: la cita reservada aparece en la agenda del día
    public async Task Book_ThenGetByDate_ListsAppointment()
    {
        var (svc, _) = Make(seed: false);
        var slot = Now.AddHours(3);

        var appt = await svc.BookAsync(Req(slot));
        var list = await svc.GetByDateAsync(DateOnly.FromDateTime(slot));

        Assert.Contains(list, a => a.Id == appt.Id);
    }

    [Fact] // filter: GetByDate filtra por médico
    public async Task GetByDate_FiltersByDoctor()
    {
        var (svc, _) = Make(seed: false);
        await svc.BookAsync(new BookAppointmentRequest("pat-camila-restrepo", "doc-ana-rios", Now.AddHours(2)));
        await svc.BookAsync(new BookAppointmentRequest("pat-andres-pardo", "doc-diego-soto", Now.AddHours(4)));

        var onlyAna = await svc.GetByDateAsync(DateOnly.FromDateTime(Now), "doc-ana-rios");

        Assert.NotEmpty(onlyAna);
        Assert.All(onlyAna, a => Assert.Equal("doc-ana-rios", a.DoctorId));
    }

    [Fact] // conflicto: reservar dos veces el MISMO slot/médico lanza (anti doble-reserva)
    public async Task Book_SameSlotTwice_Throws()
    {
        var (svc, _) = Make(seed: false);
        var slot = Now.AddHours(2);
        await svc.BookAsync(new BookAppointmentRequest("pat-camila-restrepo", "doc-ana-rios", slot));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.BookAsync(new BookAppointmentRequest("pat-valentina-cruz", "doc-ana-rios", slot)));
    }

    [Fact] // inválido: paciente inexistente lanza
    public async Task Book_UnknownPatient_Throws()
    {
        var (svc, _) = Make(seed: false);
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.BookAsync(new BookAppointmentRequest("pat-no-existe", "doc-ana-rios", Now.AddHours(2))));
    }

    [Fact] // inválido: médico inexistente lanza
    public async Task Book_UnknownDoctor_Throws()
    {
        var (svc, _) = Make(seed: false);
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.BookAsync(new BookAppointmentRequest("pat-camila-restrepo", "doc-no-existe", Now.AddHours(2))));
    }

    [Fact] // demo seed: con seed=true el día base trae el schedule sembrado
    public async Task SeededDay_HasAppointments()
    {
        var (svc, _) = Make(seed: true);

        var today = await svc.GetByDateAsync(DateOnly.FromDateTime(Now));

        Assert.NotEmpty(today);
        Assert.Contains(today, a => a.Status == "checked-in" || a.Status == "booked");
        // Ordenadas por hora ascendente.
        var starts = today.Select(a => a.StartUtc).ToList();
        Assert.Equal(starts.OrderBy(s => s), starts);
    }
}
