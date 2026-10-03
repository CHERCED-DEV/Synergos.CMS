using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IClinicalSchedulingService"/> — agenda de citas STUB del
/// dashboard EHR-lite (OLA 5). Lógica pura (ADR 0002); estado en memoria del proceso.
/// </summary>
/// <remarks>
/// <strong>Reusa el motor de reservas + pago (spec §4), NO los reinventa:</strong>
/// la cita es un recurso reservable POLIMÓRFICO igual que habitación/asiento. El
/// flujo de <see cref="BookAsync"/> calca el de hoteles/aerolíneas:
/// <list type="number">
/// <item><see cref="IReservationService.HoldItemAsync"/> aparta el slot del médico
///   como <see cref="TravelItemReservationRequest"/> (ProductRef = doctor|slot),
///   con el mismo hold-timeout que room/seat.</item>
/// <item>El copago (opcional, nominal en demo) abre y captura UNA sesión vía
///   <see cref="IPaymentProvider"/> — idéntico a checkout de hoteles.</item>
/// <item><see cref="IReservationService.ConfirmAsync"/> liga la reserva a la sesión
///   y la deja Confirmed (máquina de estados Held→Confirmed reusada).</item>
/// </list>
/// El "producto" es el slot del médico y el comprobante es la cita; el motor los
/// trata como cualquier otra reserva. El adapter real (PMS / agenda integrada)
/// reemplaza el seam sin tocar el motor.
/// </remarks>
public sealed class StubClinicalSchedulingService : IClinicalSchedulingService
{
    /// <summary>Copago/fee nominal de consulta (COP) — el motor exige total &gt; 0.</summary>
    public const decimal ConsultationFee = 80_000m;
    public const string Currency = "COP";

    private readonly IReservationService _reservations;
    private readonly IPaymentProvider _payments;
    private readonly IDoctorDirectory _doctors;
    private readonly IPatientRegistry _patients;
    private readonly Func<DateTime> _nowUtc;

    /// <summary>
    /// La zona del consultorio: en ella es «la fecha» de <see cref="GetByDateAsync"/> y de
    /// <see cref="GetForPatientAsync"/> —lo que promete el seam— y la hora de las citas sembradas.
    /// </summary>
    private readonly TimeZoneInfo _zona;

    private readonly ConcurrentDictionary<string, ClinicalAppointment> _appointments = new(StringComparer.Ordinal);

    public StubClinicalSchedulingService(
        IReservationService reservations,
        IPaymentProvider payments,
        IDoctorDirectory doctors,
        IPatientRegistry patients)
        : this(reservations, payments, doctors, patients, null, seed: true)
    {
    }

    /// <summary>
    /// Ctor configurable con time source inyectable y opción de sembrar citas demo
    /// (para determinismo en tests, ADR 0002).
    /// </summary>
    /// <param name="zonaDelConsultorio">La zona del sitio (<c>Synergos:Listados:ZonaHoraria</c>): en
    /// ella es «la fecha» de una cita. Sin ella, UTC —lo que hacía siempre—, que en Bogotá corre el
    /// día a las siete de la noche.</param>
    public StubClinicalSchedulingService(
        IReservationService reservations,
        IPaymentProvider payments,
        IDoctorDirectory doctors,
        IPatientRegistry patients,
        Func<DateTime>? nowUtc,
        bool seed,
        TimeZoneInfo? zonaDelConsultorio = null)
    {
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _doctors = doctors ?? throw new ArgumentNullException(nameof(doctors));
        _patients = patients ?? throw new ArgumentNullException(nameof(patients));
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        _zona = zonaDelConsultorio ?? TimeZoneInfo.Utc;

        if (seed)
        {
            SeedDemoAppointments();
        }
    }

    public async Task<ClinicalAppointment> BookAsync(BookAppointmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PatientId))
        {
            throw new ArgumentException("El paciente es obligatorio.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.DoctorId))
        {
            throw new ArgumentException("El médico es obligatorio.", nameof(request));
        }

        var patient = await _patients.GetAsync(request.PatientId, cancellationToken)
            ?? throw new ArgumentException($"Paciente '{request.PatientId}' no encontrado.", nameof(request));
        var doctor = await _doctors.GetAsync(request.DoctorId, cancellationToken)
            ?? throw new ArgumentException($"Médico '{request.DoctorId}' no encontrado.", nameof(request));

        var startUtc = DateTime.SpecifyKind(request.Slot, DateTimeKind.Utc);
        var endUtc = startUtc.AddMinutes(doctor.SlotMinutes);

        // Anti doble-reserva del MISMO slot/médico (citas vivas).
        var clash = _appointments.Values.Any(a =>
            string.Equals(a.DoctorId, doctor.Id, StringComparison.Ordinal)
            && a.StartUtc == startUtc
            && !string.Equals(a.Status, "cancelled", StringComparison.Ordinal));
        if (clash)
        {
            throw new InvalidOperationException("El slot ya está ocupado para ese médico.");
        }

        // 1) Aparta el slot con el MOTOR DE RESERVAS (cita = ítem reservable polimórfico).
        var hold = await _reservations.HoldItemAsync(
            new TravelItemReservationRequest(
                ProductType: TravelProductType.Hotel, // tipo genérico; la identidad real va en ProductRef/Label
                ProductRef: $"{doctor.Id}|{startUtc:O}",
                ProductLabel: $"Cita {doctor.Specialty} — {doctor.FullName}",
                GuestName: patient.FullName,
                GuestEmail: patient.Email,
                TotalPrice: ConsultationFee,
                Currency: Currency),
            cancellationToken);

        // 2) Copago nominal vía el MOTOR DE PAGO (opcional; demuestra la reusabilidad).
        var session = await _payments.CreateSessionAsync(
            new PaymentSessionRequest(
                OrderReference: hold.Id,
                Amount: ConsultationFee,
                Currency: Currency,
                Items: new[] { new PaymentLineItem(doctor.Id, $"Copago consulta {doctor.Specialty}", ConsultationFee, 1) },
                CustomerEmail: patient.Email),
            cancellationToken);
        var capture = await _payments.CaptureAsync(session.SessionId, cancellationToken: cancellationToken);

        // 3) Si el copago NO capturó, se SUELTA el cupo y no se agenda.
        //
        //    Antes se llamaba a ConfirmAsync incondicionalmente y el resultado
        //    del pago sólo teñía la etiqueta ("booked" vs "pending"): el slot
        //    quedaba tomado igual. Con un PSP que auto-aprueba no se notaba;
        //    con Wompi es un cupo regalado por cada pago rechazado, y en una
        //    agenda médica un cupo regalado es una cita que otro paciente no
        //    pudo pedir.
        //
        //    VoidAsync (ADR 0116) es lo que faltaba para poder soltarlo: no es
        //    un reembolso —no hubo cobro— sino liberar la retención.
        if (capture.Status != PaymentStatus.Captured)
        {
            await BestEffort.RunAsync(
                () => _payments.VoidAsync(session.SessionId, cancellationToken),
                cancellationToken);
            await BestEffort.RunAsync(
                () => _reservations.CancelAsync(hold.Id, "copago no capturado", cancellationToken),
                cancellationToken);

            throw new InvalidOperationException(
                $"El copago no se pudo capturar ({capture.Status}). La cita no quedó agendada.");
        }

        // 4) Confirma la reserva ligándola a la sesión (máquina de estados reusada).
        var confirmed = await _reservations.ConfirmAsync(hold.Id, session.SessionId, cancellationToken);
        var status = confirmed.Status == ReservationStatus.Confirmed ? "booked" : "pending";

        var appointment = new ClinicalAppointment(
            Id: $"appt_{Guid.NewGuid():N}",
            PatientId: patient.Id,
            PatientName: patient.FullName,
            DoctorId: doctor.Id,
            DoctorName: doctor.FullName,
            Specialty: doctor.Specialty,
            StartUtc: startUtc,
            EndUtc: endUtc,
            Status: status,
            ReservationId: confirmed.Id);

        _appointments[appointment.Id] = appointment;
        return appointment;
    }

    /// <summary>El copago que <see cref="BookAsync"/> captura: <see cref="ConsultationFee"/>.</summary>
    public Task<ClinicalCopay?> CopayAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<ClinicalCopay?>(new ClinicalCopay(ConsultationFee, Currency));

    public Task<IReadOnlyList<ClinicalAppointment>> GetByDateAsync(DateOnly date, string? doctorId = null, CancellationToken cancellationToken = default)
    {
        var matches = _appointments.Values
            .Where(a => DiaEnElConsultorio(a.StartUtc) == date)
            .Where(a => string.IsNullOrWhiteSpace(doctorId) || string.Equals(a.DoctorId, doctorId, StringComparison.Ordinal))
            .OrderBy(a => a.StartUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<ClinicalAppointment>>(matches);
    }

    public Task<IReadOnlyList<ClinicalAppointment>> GetForPatientAsync(
        string patientId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var matches = _appointments.Values
            .Where(a => string.Equals(a.PatientId, patientId, StringComparison.Ordinal))
            .Where(a =>
            {
                var dia = DiaEnElConsultorio(a.StartUtc);
                return dia >= from && dia <= to;
            })
            .OrderBy(a => a.StartUtc)
            .ToList();
        return Task.FromResult<IReadOnlyList<ClinicalAppointment>>(matches);
    }

    /// <summary>
    /// El día del consultorio en que cae un instante de la agenda. «La fecha» que promete el seam
    /// es ésa, no la de UTC: medido, en Bogotá una cita a las 22:30 del 2 caía en el día 3.
    /// </summary>
    private DateOnly DiaEnElConsultorio(DateTime startUtc)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), _zona));

    // ── Semilla de citas demo (no pasa por el motor — son históricas/agendadas) ──
    private void SeedDemoAppointments()
    {
        // Las horas son las del CONSULTORIO —«las 9:00» de Jorge es la mañana en Bogotá—: se
        // anclaban en UTC, y con la agenda pintada en la hora del sitio la cita salía a las 4:00.
        var hoy = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(_nowUtc(), DateTimeKind.Utc), _zona).Date;
        DateTime A(int horas, int minutos = 0)
            => TimeZoneInfo.ConvertTimeToUtc(hoy.AddHours(horas).AddMinutes(minutos), _zona);
        var seed = new[]
        {
            CreateSeed("pat-jorge-medina", "Jorge Medina", "doc-carlos-mejia", "Dr. Carlos Mejía", "Cardiología", A(9), 40, "checked-in"),
            CreateSeed("pat-camila-restrepo", "Camila Restrepo", "doc-ana-rios", "Dra. Ana Ríos", "Medicina Interna", A(10, 30), 30, "booked"),
            CreateSeed("pat-valentina-cruz", "Valentina Cruz", "doc-ana-rios", "Dra. Ana Ríos", "Medicina Interna", A(11, 30), 30, "booked"),
            CreateSeed("pat-sara-gomez", "Sara Gómez", "doc-laura-vega", "Dra. Laura Vega", "Pediatría", A(8, 20), 20, "done"),
            CreateSeed("pat-andres-pardo", "Andrés Pardo", "doc-diego-soto", "Dr. Diego Soto", "Dermatología", A(14), 30, "booked"),
        };
        foreach (var a in seed)
        {
            _appointments[a.Id] = a;
        }
    }

    private static ClinicalAppointment CreateSeed(
        string patientId, string patientName, string doctorId, string doctorName,
        string specialty, DateTime startUtc, int minutes, string status)
        => new(
            Id: $"appt-seed-{doctorId}-{startUtc:HHmm}",
            PatientId: patientId,
            PatientName: patientName,
            DoctorId: doctorId,
            DoctorName: doctorName,
            Specialty: specialty,
            StartUtc: startUtc,
            EndUtc: startUtc.AddMinutes(minutes),
            Status: status,
            ReservationId: $"resv-seed-{doctorId}-{startUtc:HHmm}");
}
