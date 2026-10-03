using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

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

    // ── La pregunta por paciente (HU #111) ────────────────────────────────────────
    //
    // Existe para que la ficha del paciente deje de barrer la ventana día a día: eran 91
    // llamadas por carga. Los tres casos de siempre — vacío / filtro por paciente / filtro por
    // ventana — porque son las dos mitades que el bucle hacía de este lado.

    [Fact] // vacío: un paciente sin citas es una lista vacía, no un null.
    public async Task GetForPatient_SinCitas_DevuelveVacio()
    {
        var (svc, _) = Make(seed: true);
        var hoy = DateOnly.FromDateTime(Now);

        var list = await svc.GetForPatientAsync("pat-sin-citas", hoy.AddDays(-30), hoy.AddDays(60));

        Assert.Empty(list);
    }

    [Fact] // filtro: sólo las del paciente que se pide — lo que antes filtraba el controller.
    public async Task GetForPatient_SoloLasDeEsePaciente()
    {
        var (svc, _) = Make(seed: true);
        var hoy = DateOnly.FromDateTime(Now);

        var list = await svc.GetForPatientAsync("pat-jorge-medina", hoy.AddDays(-30), hoy.AddDays(60));

        Assert.NotEmpty(list);
        Assert.All(list, a => Assert.Equal("pat-jorge-medina", a.PatientId));
    }

    [Fact] // filtro: la ventana ACOTA de verdad — si no, «por paciente» traería su historia entera.
    public async Task GetForPatient_LaVentanaDeja_FueraLoQueNoCae_EnElla()
    {
        var (svc, _) = Make(seed: true);
        var hoy = DateOnly.FromDateTime(Now);

        // Las citas sembradas son de HOY; una ventana que empieza mañana no puede traerlas.
        var fuera = await svc.GetForPatientAsync("pat-jorge-medina", hoy.AddDays(1), hoy.AddDays(60));
        var dentro = await svc.GetForPatientAsync("pat-jorge-medina", hoy, hoy);

        Assert.Empty(fuera);
        Assert.NotEmpty(dentro);
    }

    // ── El copago (CMS#196) ─────────────────────────────────────────────────

    /// <summary>
    /// El copago que se le muestra al paciente es el que se captura al agendar. La pantalla pintaba
    /// «Sin costo» —una constante compilada en cero— mientras este motor capturaba 80.000.
    /// </summary>
    [Fact]
    public async Task El_copago_que_se_muestra_es_el_que_se_captura_al_agendar()
    {
        var pagos = new PagosQueRecuerdan();
        var svc = new StubClinicalSchedulingService(
            new StubReservationService(), pagos, new StubDoctorDirectory(), new StubPatientRegistry(), () => Now, false);

        var copago = await svc.CopayAsync();
        await svc.BookAsync(Req());

        Assert.NotNull(copago);
        Assert.True(copago!.Amount > 0m);
        Assert.Equal(copago.Amount, pagos.Ultima!.Amount);
        Assert.Equal(copago.Currency, pagos.Ultima.Currency);
    }

    // ── «La fecha» es la del consultorio (zona del sitio) ─────────────────────────────
    //
    // El seam promete la fecha del consultorio y el stub comparaba la de UTC: en Bogotá, una cita a
    // las 22:30 del 2 caía en el día 3, y las citas sembradas «a las 9:00» eran las 4:00 de allá.
    // El reloj va cerca de la medianoche UTC, donde las dos lecturas dan días distintos.

    private static readonly TimeZoneInfo Bogota = new Synergos.CMS.Application.Configuration.ListadosSettings().Zona()!;

    private static readonly DateTime CercaDeMedianocheUtc = new(2026, 10, 3, 3, 30, 0, DateTimeKind.Utc);

    private static IClinicalSchedulingService EnBogota(bool seed)
        => new StubClinicalSchedulingService(new StubReservationService(), new StubPaymentProvider(),
            new StubDoctorDirectory(), new StubPatientRegistry(), () => CercaDeMedianocheUtc, seed, Bogota);

    [Fact]
    public async Task La_fecha_de_una_cita_es_la_del_consultorio_y_no_la_de_UTC()
    {
        var svc = EnBogota(seed: false);
        await svc.BookAsync(new BookAppointmentRequest("pat-camila-restrepo", "doc-ana-rios", CercaDeMedianocheUtc));

        Assert.Single(await svc.GetByDateAsync(new DateOnly(2026, 10, 2)));
        Assert.Empty(await svc.GetByDateAsync(new DateOnly(2026, 10, 3)));
        Assert.Single(await svc.GetForPatientAsync("pat-camila-restrepo", new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 2)));
    }

    [Fact]
    public async Task Las_citas_sembradas_son_de_la_manana_del_consultorio()
    {
        var svc = EnBogota(seed: true);

        var jorge = Assert.Single(await svc.GetByDateAsync(new DateOnly(2026, 10, 2), "doc-carlos-mejia"));

        // Las 9:00 del 2 en Bogotá son las 14:00Z.
        Assert.Equal(new DateTime(2026, 10, 2, 14, 0, 0, DateTimeKind.Utc), jorge.StartUtc);
    }

    private sealed class PagosQueRecuerdan : IPaymentProvider, IDisposable
    {
        private readonly StubPaymentProvider _real = new();

        public string ProviderKey => _real.ProviderKey;

        public PaymentSessionRequest? Ultima { get; private set; }

        public void Dispose() => _real.Dispose();

        public Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest request, CancellationToken ct = default)
        {
            Ultima = request;
            return _real.CreateSessionAsync(request, ct);
        }

        public Task<PaymentOutcome> GetStatusAsync(string sessionId, CancellationToken ct = default)
            => _real.GetStatusAsync(sessionId, ct);

        public Task<PaymentOutcome> CaptureAsync(string sessionId, decimal? amount = null, CancellationToken ct = default)
            => _real.CaptureAsync(sessionId, amount, ct);

        public Task<PaymentOutcome> VoidAsync(string sessionId, CancellationToken ct = default)
            => _real.VoidAsync(sessionId, ct);

        public Task<PaymentOutcome> RefundAsync(string sessionId, decimal? amount = null, CancellationToken ct = default)
            => _real.RefundAsync(sessionId, amount, ct);
    }

}
