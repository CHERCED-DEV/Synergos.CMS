using Synergos.Api.Booking.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Cubre <see cref="BookingRules"/> — lo que la capacidad puede rechazar <b>sola</b>.
/// </summary>
/// <remarks>
/// Es el filtro del doc 07 §1 convertido en tests: si esta clase pasara sin rechazar nada,
/// Booking sería una base de datos con HTTP.
/// </remarks>
public sealed class BookingRulesTests
{
    private static readonly DateTimeOffset Lunes10 = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);   // lunes

    private static TimeWindow Ventana(DateTimeOffset inicio, int minutos)
        => TimeWindow.Starting(inicio, TimeSpan.FromMinutes(minutos));

    private static Resource Recurso(
        int capacity = 1,
        IReadOnlyList<OpeningRule>? opening = null,
        string tz = "UTC",
        int noticeMinutes = 0)
        => new("r1", Ref.Create("test.recurso", "x"), capacity, tz,
            opening ?? Array.Empty<OpeningRule>(), new CancellationPolicy(TimeSpan.FromMinutes(noticeMinutes)));

    // ── Horario ─────────────────────────────────────────────────────────────

    [Fact]
    public void Sin_horario_declarado_TODO_cabe_incluso_cruzando_la_medianoche()
    {
        // ESTE es el test que decide si Booking sirve a Salud Y a Travel. Una noche de hotel va
        // de las 15:00 de un día a las 11:00 del siguiente. Si el horario fuera obligatorio,
        // esta capacidad habría servido a la agenda clínica y a nada más.
        var hotel = Recurso();
        var noche = TimeWindow.Of(Lunes10.AddHours(5), Lunes10.AddDays(1).AddHours(1));

        Assert.Null(BookingRules.CheckOpeningHours(hotel, noche));
    }

    [Fact]
    public void Con_horario_declarado_una_ventana_fuera_se_rechaza()
    {
        var clinica = Recurso(opening: new[] { new OpeningRule(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(12, 0)) });

        var dentro = BookingRules.CheckOpeningHours(clinica, Ventana(Lunes10, 30));
        var fuera = BookingRules.CheckOpeningHours(clinica, Ventana(Lunes10.AddHours(4), 30));   // 14:00

        Assert.Null(dentro);
        Assert.Equal("booking.outside_opening_hours", fuera?.Code);
    }

    [Fact]
    public void El_horario_se_lee_en_el_HUSO_del_recurso_y_no_en_UTC()
    {
        // Sin esto, una clínica de Bogotá que atiende de 8 a 12 rechazaría sus propias citas
        // de la mañana y aceptaría las de la madrugada. Es el error que no se ve en un entorno
        // que corre en UTC y aparece el día del despliegue.
        var bogota = Recurso(tz: "America/Bogota",
            opening: new[] { new OpeningRule(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(12, 0)) });

        // 14:00 UTC = 09:00 en Bogotá (UTC-5): dentro del horario local.
        var mananaLocal = Ventana(new DateTimeOffset(2026, 3, 2, 14, 0, 0, TimeSpan.Zero), 30);
        // 10:00 UTC = 05:00 en Bogotá: fuera.
        var madrugadaLocal = Ventana(Lunes10, 30);

        Assert.Null(BookingRules.CheckOpeningHours(bogota, mananaLocal));
        Assert.NotNull(BookingRules.CheckOpeningHours(bogota, madrugadaLocal));
    }

    [Fact]
    public void Un_huso_desconocido_es_CONFLICTO_del_recurso_y_no_culpa_de_quien_reserva()
    {
        var roto = Recurso(tz: "Marte/Olympus",
            opening: new[] { new OpeningRule(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(12, 0)) });

        var bad = BookingRules.CheckOpeningHours(roto, Ventana(Lunes10, 30));

        Assert.Equal(RejectionKind.Conflict, bad?.Kind);
        Assert.Equal("booking.bad_timezone", bad?.Code);
    }

    // ── Cupo ────────────────────────────────────────────────────────────────

    [Fact]
    public void Dos_ventanas_CONTIGUAS_caben_en_un_recurso_de_capacidad_uno()
    {
        // El error clásico de agendas. Si 10:00–10:30 bloqueara 10:30–11:00, un consultorio
        // atendería una sola cita por día.
        var consultorio = Recurso(capacity: 1);
        var ocupado = new[] { Ventana(Lunes10, 30) };

        Assert.Null(BookingRules.CheckCapacity(consultorio, ocupado, Ventana(Lunes10.AddMinutes(30), 30)));
    }

    [Fact]
    public void El_solapamiento_agota_el_cupo()
    {
        var consultorio = Recurso(capacity: 1);
        var ocupado = new[] { Ventana(Lunes10, 60) };

        var bad = BookingRules.CheckCapacity(consultorio, ocupado, Ventana(Lunes10.AddMinutes(30), 60));

        Assert.Equal("booking.at_capacity", bad?.Code);
        Assert.Equal(RejectionKind.Conflict, bad?.Kind);
    }

    [Fact]
    public void Un_aula_de_capacidad_alta_admite_varias_a_la_vez_y_corta_en_el_tope()
    {
        var aula = Recurso(capacity: 3);
        var dos = new[] { Ventana(Lunes10, 60), Ventana(Lunes10, 60) };
        var tres = new[] { Ventana(Lunes10, 60), Ventana(Lunes10, 60), Ventana(Lunes10, 60) };

        Assert.Null(BookingRules.CheckCapacity(aula, dos, Ventana(Lunes10, 60)));
        Assert.NotNull(BookingRules.CheckCapacity(aula, tres, Ventana(Lunes10, 60)));
    }

    [Fact]
    public void Las_ventanas_de_OTRO_horario_no_gastan_cupo()
    {
        // Cuenta lo solapado, no las reservas del día: un aula puede tener cien reservas en la
        // semana y estar libre a las 10:00.
        var aula = Recurso(capacity: 1);
        var ocupado = new[] { Ventana(Lunes10.AddHours(5), 60), Ventana(Lunes10.AddDays(1), 60) };

        Assert.Null(BookingRules.CheckCapacity(aula, ocupado, Ventana(Lunes10, 60)));
    }

    // ── Pasado ──────────────────────────────────────────────────────────────

    [Fact]
    public void Una_ventana_que_ya_empezo_se_rechaza_como_INVALID()
    {
        // Invalid y no Conflict: no es que el estado no dé, es que la petición no tiene sentido.
        // La distinción decide si el cliente reintenta o corrige.
        var bad = BookingRules.CheckNotInThePast(Ventana(Lunes10, 30), Lunes10.AddMinutes(1));

        Assert.Equal(RejectionKind.Invalid, bad?.Kind);
        Assert.Null(BookingRules.CheckNotInThePast(Ventana(Lunes10, 30), Lunes10));   // justo a tiempo
    }

    // ── Hold ────────────────────────────────────────────────────────────────

    private static Hold UnHold(DateTimeOffset expira, bool released = false, string? reserva = null)
        => new("h1", "r1", Ventana(Lunes10, 30), Ref.Create("test.persona", "p1"), Lunes10.AddHours(-1), expira, released, reserva);

    [Fact]
    public void Un_hold_vencido_devuelve_EXPIRED_y_no_NotFound()
    {
        // Expired le dice al llamador "rehacé el flujo desde el hold". NotFound lo mandaría a
        // revisar un identificador que escribió bien.
        var bad = BookingRules.CheckHoldIsUsable(UnHold(Lunes10), Lunes10);

        Assert.Equal(RejectionKind.Expired, bad?.Kind);
        Assert.Equal("booking.hold_expired", bad?.Code);
    }

    [Fact]
    public void Un_hold_vigente_por_un_tick_todavia_sirve()
    {
        Assert.Null(BookingRules.CheckHoldIsUsable(UnHold(Lunes10), Lunes10.AddTicks(-1)));
    }

    [Fact]
    public void Un_hold_ya_confirmado_o_ya_suelto_no_se_puede_volver_a_usar()
    {
        var confirmado = BookingRules.CheckHoldIsUsable(UnHold(Lunes10.AddHours(1), reserva: "res-1"), Lunes10);
        var suelto = BookingRules.CheckHoldIsUsable(UnHold(Lunes10.AddHours(1), released: true), Lunes10);

        Assert.Equal("booking.hold_already_confirmed", confirmado?.Code);
        Assert.Equal("booking.hold_released", suelto?.Code);
    }

    [Fact]
    public void Un_hold_confirmado_deja_de_ocupar_cupo_COMO_HOLD()
    {
        // Si siguiera contando, cada confirmación gastaría dos plazas — y en un consultorio de
        // capacidad 1, la primera cita bloquearía el día entero.
        Assert.False(UnHold(Lunes10.AddHours(1), reserva: "res-1").IsActive(Lunes10));
        Assert.True(UnHold(Lunes10.AddHours(1)).IsActive(Lunes10));
    }

    // ── Cancelación ─────────────────────────────────────────────────────────

    private static Reservation UnaReserva(ReservationStatus status = ReservationStatus.Confirmed)
        => new("res1", "r1", "h1", Ventana(Lunes10, 30), Ref.Create("test.persona", "p1"), Lunes10.AddDays(-1), status);

    [Fact]
    public void La_politica_de_antelacion_se_aplica_contra_el_INICIO_de_la_reserva()
    {
        var politica = new CancellationPolicy(TimeSpan.FromHours(24));

        var aTiempo = BookingRules.CheckCancellable(UnaReserva(), politica, Lunes10.AddHours(-25));
        var justo = BookingRules.CheckCancellable(UnaReserva(), politica, Lunes10.AddHours(-24));
        var tarde = BookingRules.CheckCancellable(UnaReserva(), politica, Lunes10.AddHours(-23));

        Assert.Null(aTiempo);
        Assert.Null(justo);   // el límite exacto todavía cuenta
        Assert.Equal("booking.cancellation_too_late", tarde?.Code);
    }

    [Fact]
    public void Sin_politica_se_cancela_hasta_el_ultimo_instante()
    {
        Assert.Null(BookingRules.CheckCancellable(UnaReserva(), CancellationPolicy.Anytime, Lunes10));
    }

    [Fact]
    public void Cancelar_dos_veces_es_conflicto_y_no_pasa_desapercibido()
    {
        var bad = BookingRules.CheckCancellable(UnaReserva(ReservationStatus.Cancelled), CancellationPolicy.Anytime, Lunes10.AddDays(-1));

        Assert.Equal("booking.already_cancelled", bad?.Code);
    }

    [Fact]
    public void La_politica_de_cancelacion_NO_sabe_de_dinero()
    {
        // Si CancellationPolicy tuviera un monto o un porcentaje, Booking estaría cargando una
        // política comercial que cambia por negocio — y ahí dejaría de servirle al siguiente.
        var props = typeof(CancellationPolicy).GetProperties().Select(p => p.Name).ToList();

        Assert.Equal(new[] { nameof(CancellationPolicy.MinNoticeBeforeStart) }, props);
    }
}
