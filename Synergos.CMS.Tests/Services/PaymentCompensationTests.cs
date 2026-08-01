using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Fija los arreglos de la fase 5 del ADR 0116: qué pasa cuando el pago NO sale.
/// </summary>
/// <remarks>
/// <para>La investigación encontró que siete de los ocho verticales usaban mal
/// el motor de pago, cada uno distinto, y todos en la misma dirección: el
/// negocio avanzaba aunque el cobro no hubiera ocurrido. Con un PSP que
/// auto-aprueba nunca se notó.</para>
///
/// <para>Estos tests miran el <b>efecto sobre el recurso</b> —el cupo, la
/// reserva— y no el mensaje devuelto. Un mensaje de error puede salir igual
/// mientras el cupo se regala.</para>
/// </remarks>
public sealed class PaymentCompensationTests
{
    // ── Salud: el cupo no se regala ──────────────────────────────────────

    private static IPaymentProvider PspThatFailsCapture()
    {
        var psp = Substitute.For<IPaymentProvider>();
        psp.ProviderKey.Returns("test");
        psp.CreateSessionAsync(Arg.Any<PaymentSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentSession("sess_1", PaymentStatus.Authorized, null, "test"));
        psp.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentOutcome("sess_1", PaymentStatus.Failed, 0m, "tarjeta rechazada"));
        psp.VoidAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentOutcome("sess_1", PaymentStatus.Cancelled));
        return psp;
    }

    private static IPaymentProvider PspThatCaptures()
    {
        var psp = Substitute.For<IPaymentProvider>();
        psp.ProviderKey.Returns("test");
        psp.CreateSessionAsync(Arg.Any<PaymentSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentSession("sess_1", PaymentStatus.Authorized, null, "test"));
        psp.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentOutcome("sess_1", PaymentStatus.Captured, 50_000m));
        return psp;
    }

    /// <summary>Un médico que atiende todos los días, para que el slot elegido sea válido.</summary>
    private static MedicalDoctor Doctor() => new(
        Id: "doc-1", FullName: "Dra. Elena Ríos", Specialty: "Medicina interna",
        LicenseNumber: "LIC-1", Rating: 4.8, YearsExperience: 12, AvatarUrl: null,
        WorkingDays: Enum.GetValues<DayOfWeek>(),
        SlotStartHour: 0, SlotEndHour: 23, SlotMinutes: 30);

    private static EhrPatient Patient() => new(
        Id: "pat-1", FullName: "Ana Torres", DocumentId: "CC1", Gender: "F",
        DateOfBirth: new DateOnly(1990, 5, 3), AgeYears: 36, Phone: "300",
        Email: "ana@x.co", City: "Bogotá", BloodType: "O+",
        Allergies: Array.Empty<string>(), ChronicConditions: Array.Empty<string>(),
        PrimaryDoctorId: null, AvatarUrl: null);

    /// <summary>
    /// Todo sustituido salvo el sujeto: esto prueba la compensación de pago, no
    /// los datos sembrados del directorio médico.
    /// </summary>
    private static StubClinicalSchedulingService Scheduling(
        IPaymentProvider psp, IReservationService reservations)
    {
        var doctors = Substitute.For<IDoctorDirectory>();
        doctors.GetAsync("doc-1", Arg.Any<CancellationToken>()).Returns(Doctor());

        var patients = Substitute.For<IPatientRegistry>();
        patients.GetAsync("pat-1", Arg.Any<CancellationToken>()).Returns(Patient());

        return new StubClinicalSchedulingService(
            reservations, psp, doctors, patients, nowUtc: null, seed: false);
    }

    private static BookAppointmentRequest Booking()
        => new(PatientId: "pat-1", DoctorId: "doc-1",
               Slot: DateTime.UtcNow.Date.AddDays(3).AddHours(10));

    [Fact]
    public async Task Salud_si_el_copago_NO_captura_la_cita_no_queda_agendada()
    {
        var reservations = Substitute.For<IReservationService>();
        reservations.HoldItemAsync(Arg.Any<TravelItemReservationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new Reservation("hold_1", ReservationStatus.Held, "x", "y",
                DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                "Ana", "ana@x.co", 50_000m, "COP"));

        var sut = Scheduling(PspThatFailsCapture(), reservations);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.BookAsync(Booking(), CancellationToken.None));
    }

    [Fact]
    public async Task Salud_si_el_copago_NO_captura_se_SUELTA_el_cupo()
    {
        // Lo que de verdad importa. Antes se llamaba a ConfirmAsync pasara lo
        // que pasara y el pago sólo teñía una etiqueta: el slot quedaba tomado.
        // En una agenda médica un cupo regalado es una cita que otro paciente
        // no pudo pedir.
        var reservations = Substitute.For<IReservationService>();
        reservations.HoldItemAsync(Arg.Any<TravelItemReservationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new Reservation("hold_1", ReservationStatus.Held, "x", "y",
                DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                "Ana", "ana@x.co", 50_000m, "COP"));

        var psp = PspThatFailsCapture();
        var sut = Scheduling(psp, reservations);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.BookAsync(Booking(), CancellationToken.None));

        await reservations.DidNotReceive()
            .ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await reservations.Received(1)
            .CancelAsync("hold_1", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Salud_si_el_copago_NO_captura_se_ANULA_la_retencion_de_fondos()
    {
        // VoidAsync (ADR 0116 fase 1) es la pieza que hacía falta: no es un
        // reembolso —no hubo cobro— sino soltar los fondos retenidos.
        var reservations = Substitute.For<IReservationService>();
        reservations.HoldItemAsync(Arg.Any<TravelItemReservationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new Reservation("hold_1", ReservationStatus.Held, "x", "y",
                DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                "Ana", "ana@x.co", 50_000m, "COP"));

        var psp = PspThatFailsCapture();
        var sut = Scheduling(psp, reservations);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.BookAsync(Booking(), CancellationToken.None));

        await psp.Received(1).VoidAsync("sess_1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Salud_con_el_copago_capturado_la_cita_SI_queda_agendada()
    {
        // El contrapeso: el arreglo no puede dejar el flujo bueno inservible.
        var reservations = Substitute.For<IReservationService>();
        var hold = new Reservation("hold_1", ReservationStatus.Held, "x", "y",
            DateOnly.FromDateTime(DateTime.UtcNow), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            "Ana", "ana@x.co", 50_000m, "COP");
        reservations.HoldItemAsync(Arg.Any<TravelItemReservationRequest>(), Arg.Any<CancellationToken>())
            .Returns(hold);
        reservations.ConfirmAsync("hold_1", "sess_1", Arg.Any<CancellationToken>())
            .Returns(hold with { Status = ReservationStatus.Confirmed });

        var sut = Scheduling(PspThatCaptures(), reservations);

        var appointment = await sut.BookAsync(Booking(), CancellationToken.None);

        Assert.Equal("booked", appointment.Status);
        await reservations.Received(1)
            .ConfirmAsync("hold_1", "sess_1", Arg.Any<CancellationToken>());
    }
}
