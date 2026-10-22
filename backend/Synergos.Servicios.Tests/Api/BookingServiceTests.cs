using Synergos.Api.Booking.Domain;
using Synergos.Api.Booking.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Cubre <see cref="BookingService"/> — el flujo completo hold → confirmar → cancelar.
/// </summary>
public sealed class BookingServiceTests
{
    /// <summary>Reloj que se mueve a mano. Sin esto, los bordes temporales no se prueban: se sufren.</summary>
    private sealed class RelojFalso : TimeProvider
    {
        private DateTimeOffset _now;
        public RelojFalso(DateTimeOffset inicio) => _now = inicio;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Avanzar(TimeSpan d) => _now += d;
    }

    /// <summary>Almacenes en memoria: probar el servicio no debería tocar disco.</summary>
    private sealed class MemoriaStores : IResourceStore, IHoldStore, IReservationStore, IIdempotencyLedger
    {
        private readonly Dictionary<string, Resource> _r = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Hold> _h = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Reservation> _v = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _k = new(StringComparer.Ordinal);

        Resource? IResourceStore.Find(string id) => _r.GetValueOrDefault(id);
        IReadOnlyList<Resource> IResourceStore.All() => _r.Values.ToList();
        public void Put(Resource x) => _r[x.Id] = x;

        Hold? IHoldStore.Find(string id) => _h.GetValueOrDefault(id);
        public IReadOnlyList<Hold> ForResource(string rid) => _h.Values.Where(x => x.ResourceId == rid).ToList();
        public void Put(Hold x) => _h[x.Id] = x;

        Reservation? IReservationStore.Find(string id) => _v.GetValueOrDefault(id);
        IReadOnlyList<Reservation> IReservationStore.ForResource(string rid) => _v.Values.Where(x => x.ResourceId == rid).ToList();
        IReadOnlyList<Reservation> IReservationStore.ForWhom(Ref forWhom) => _v.Values.Where(x => x.For == forWhom).ToList();
        public void Put(Reservation x) => _v[x.Id] = x;

        public string? Find(string scope, IdempotencyKey key) => _k.GetValueOrDefault($"{scope}|{key.Value}");

        public void Remember(string scope, IdempotencyKey key, string resultId) => _k[$"{scope}|{key.Value}"] = resultId;
    }

    private static readonly DateTimeOffset Lunes8 = new(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);

    private sealed record Contexto(BookingService Svc, RelojFalso Reloj);

    private static Contexto Nuevo()
    {
        var stores = new MemoriaStores();
        var reloj = new RelojFalso(Lunes8);
        return new Contexto(new BookingService(stores, stores, stores, stores, reloj), reloj);
    }

    private static IdempotencyKey Llave(string s) => IdempotencyKey.Of(s);

    // subjectId es un parámetro y no una constante porque el fixture del listado por actor
    // necesita DOS recursos distintos: con uno solo, "las reservas de esta persona" y "las
    // reservas de este recurso" devuelven lo mismo y no filtrar pasaría en verde.
    private static Resource RegistrarRecurso(
        BookingService svc, int capacity = 1, int noticeMinutes = 0, string subjectId = "x")
        => svc.RegisterResource(
            Ref.Create("test.recurso", subjectId), capacity, "UTC",
            Array.Empty<OpeningRule>(), new CancellationPolicy(TimeSpan.FromMinutes(noticeMinutes)),
            Llave(Guid.NewGuid().ToString("n"))).Value;

    private static TimeWindow Ventana(DateTimeOffset inicio, int minutos)
        => TimeWindow.Starting(inicio, TimeSpan.FromMinutes(minutos));

    // ── De la referencia a la agenda (HU #25) ───────────────────────────────

    [Fact]
    public void Se_puede_llegar_del_SUJETO_a_su_recurso()
    {
        // Sin esto, quien tiene la referencia de su entidad —un profesional, una sala— no puede
        // llegar a su agenda: el identificador del recurso lo genera ESTA capacidad al
        // registrarlo. Tendría que llevar un mapa aparte, que es una segunda verdad que se
        // desincroniza — justo lo que una capacidad existe para evitar.
        //
        // Faltaba, y lo destapó cablear la cita clínica contra los procesos vivos: Bff.Salud
        // exigía un resourceId que nadie río arriba podía conocer. Api.Inventory ya lo había
        // resuelto igual para las existencias.
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);

        var hallado = svc.GetResourceBySubject(Ref.Create("test.recurso", "x"));

        Assert.True(hallado.IsOk);
        Assert.Equal(recurso.Id, hallado.Value.Id);
    }

    [Fact]
    public void Un_sujeto_sin_recurso_registrado_da_not_found()
    {
        // Y con el mismo código que buscar por id: para el llamador, «no hay agenda para este
        // profesional» y «no existe ese recurso» llevan a la misma acción — registrarlo.
        var (svc, _) = Nuevo();
        RegistrarRecurso(svc);

        var r = svc.GetResourceBySubject(Ref.Create("test.recurso", "otro"));

        Assert.Equal("booking.resource_not_found", r.Rejection!.Code);
    }

    // ── El flujo completo ───────────────────────────────────────────────────

    [Fact]
    public void Hold_confirmar_cancelar_de_punta_a_punta()
    {
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);
        var ventana = Ventana(Lunes8.AddHours(2), 30);

        var hold = svc.CreateHold(recurso.Id, ventana, Ref.Create("salud.paciente", "p1"), null, Llave("k1"));
        Assert.True(hold.IsOk);

        var reserva = svc.ConfirmHold(hold.Value.Id, Llave("k2"));
        Assert.True(reserva.IsOk);
        Assert.Equal(ReservationStatus.Confirmed, reserva.Value.Status);
        Assert.Equal(hold.Value.Id, reserva.Value.HoldId);

        var cancelada = svc.CancelReservation(reserva.Value.Id);
        Assert.True(cancelada.IsOk);
        Assert.Equal(ReservationStatus.Cancelled, cancelada.Value.Status);
    }

    // ── La franja del recurso (núcleo, NB-1 de 011) ─────────────────────────

    private static Resource RegistrarConFranja(BookingService svc, int minutos)
        => svc.RegisterResource(
            Ref.Create("test.recurso", "franja"), 1, "UTC",
            new[] { new OpeningRule(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(12, 0)) },
            CancellationPolicy.Anytime, Llave(Guid.NewGuid().ToString("n")), minutos).Value;

    [Fact]
    public void El_recurso_guarda_su_franja_y_una_franja_sin_largo_no_se_registra()
    {
        var (svc, _) = Nuevo();

        Assert.Equal(30, RegistrarConFranja(svc, 30).SlotMinutes);
        foreach (var mala in new[] { 0, -30, BookingService.MaxSlotMinutes + 1 })
        {
            var r = svc.RegisterResource(Ref.Create("test.recurso", $"m{mala}"), 1, "UTC", Array.Empty<OpeningRule>(),
                CancellationPolicy.Anytime, Llave($"mala-{mala}"), mala);
            Assert.Equal("booking.bad_slot_minutes", r.Rejection?.Code);
        }
    }

    [Fact]
    public void Apartar_una_ventana_que_no_es_franja_se_rechaza_y_NO_toma_cupo()
    {
        // El orden importa: «arreglá lo que mandaste» antes que el cupo. Y nada queda apartado: la
        // franja buena sigue libre para quien la pida bien.
        var (svc, _) = Nuevo();
        var recurso = RegistrarConFranja(svc, 30);

        var larga = svc.CreateHold(recurso.Id, Ventana(Lunes8.AddHours(2), 180), Ref.Create("t.p", "1"), null, Llave("larga"));
        var partida = svc.CreateHold(recurso.Id, Ventana(Lunes8.AddHours(2).AddMinutes(15), 30), Ref.Create("t.p", "1"), null, Llave("partida"));
        var buena = svc.CreateHold(recurso.Id, Ventana(Lunes8.AddHours(2), 30), Ref.Create("t.p", "2"), null, Llave("buena"));

        Assert.Equal("booking.bad_slot_length", larga.Rejection?.Code);
        Assert.Equal("booking.off_slot_grid", partida.Rejection?.Code);
        Assert.True(buena.IsOk);
    }

    [Fact]
    public void La_disponibilidad_dice_por_que_una_ventana_no_es_franja()
    {
        var (svc, _) = Nuevo();
        var recurso = RegistrarConFranja(svc, 30);

        var r = svc.CheckAvailability(recurso.Id, Ventana(Lunes8.AddHours(2), 45)).Value;

        Assert.False(r.Available);
        Assert.Equal("booking.bad_slot_length", r.Reason?.Code);
    }

    // ── Las franjas libres (núcleo, NB-2 de 011) ────────────────────────────

    private static readonly DateTimeOffset Lunes0 = new(2026, 3, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Las_franjas_de_un_dia_son_las_de_su_grilla_dentro_del_horario()
    {
        // Abre de 08:00 a 12:00 con franjas de 30: ocho, de las 08:00 a las 11:30. Ninguna fuera.
        var (svc, _) = Nuevo();
        var recurso = RegistrarConFranja(svc, 30);

        var lista = svc.ListSlots(recurso.Id, Lunes0, Lunes0.AddDays(1)).Value;

        Assert.Equal(30, lista.SlotMinutes);
        Assert.Equal(Enumerable.Range(0, 8).Select(i => Lunes8.AddMinutes(30 * i)), lista.Items.Select(s => s.Window.Start));
        Assert.All(lista.Items, s => Assert.Equal(TimeSpan.FromMinutes(30), s.Window.End - s.Window.Start));
        Assert.All(lista.Items, s => Assert.Equal(1, s.Free));
    }

    [Fact]
    public void Lo_libre_descuenta_lo_que_apartar_descuenta_y_nada_mas()
    {
        // La MISMA ocupación que apartar: un apartado vivo y una reserva vigente llenan su franja; un
        // apartado vencido y una reserva cancelada, no. Si la lista contara otra cosa, prometería franjas
        // que apartar después niega.
        var (svc, reloj) = Nuevo();
        var recurso = RegistrarConFranja(svc, 30);
        var nueve = Lunes8.AddHours(1);

        svc.CreateHold(recurso.Id, Ventana(nueve, 30), Ref.Create("t.p", "vivo"), TimeSpan.FromMinutes(30), Llave("vivo"));
        svc.CreateHold(recurso.Id, Ventana(nueve.AddMinutes(30), 30), Ref.Create("t.p", "vence"), TimeSpan.FromMinutes(10), Llave("vence"));
        var aDiez = svc.CreateHold(recurso.Id, Ventana(nueve.AddHours(1), 30), Ref.Create("t.p", "r"), null, Llave("r")).Value;
        svc.ConfirmHold(aDiez.Id, Llave("r-c"));
        var aDiezYMedia = svc.CreateHold(recurso.Id, Ventana(nueve.AddMinutes(90), 30), Ref.Create("t.p", "x"), null, Llave("x")).Value;
        svc.CancelReservation(svc.ConfirmHold(aDiezYMedia.Id, Llave("x-c")).Value.Id);
        reloj.Avanzar(TimeSpan.FromMinutes(11));

        var libre = svc.ListSlots(recurso.Id, Lunes0, Lunes0.AddDays(1)).Value.Items.ToDictionary(s => s.Window.Start, s => s.Free);

        Assert.Equal(0, libre[nueve]);
        Assert.Equal(1, libre[nueve.AddMinutes(30)]);
        Assert.Equal(0, libre[nueve.AddHours(1)]);
        Assert.Equal(1, libre[nueve.AddMinutes(90)]);
    }

    [Fact]
    public void Lo_que_ya_empezo_no_sale_y_el_rango_recorta()
    {
        var (svc, reloj) = Nuevo();
        var recurso = RegistrarConFranja(svc, 30);

        var hastaLasDiez = svc.ListSlots(recurso.Id, Lunes8.AddHours(-1), Lunes8.AddHours(2)).Value.Items;
        reloj.Avanzar(TimeSpan.FromMinutes(40));
        var despues = svc.ListSlots(recurso.Id, Lunes8.AddHours(-1), Lunes8.AddHours(2)).Value.Items;

        Assert.Equal(4, hastaLasDiez.Count);                       // 08:00, 08:30, 09:00, 09:30
        Assert.Equal(new[] { Lunes8.AddHours(1), Lunes8.AddMinutes(90) }, despues.Select(s => s.Window.Start));
    }

    [Fact]
    public void Cada_franja_listada_se_puede_apartar_tambien_en_el_huso_del_recurso()
    {
        // Abre a las 08:20 hora de Bogotá con franjas de 40 hasta las 10:00: 08:20 y 09:00 locales, o sea
        // 13:20 y 14:00 UTC. La de las 09:40 no cabe entera y no sale. Y la lista no promete nada que
        // apartar niegue.
        var (svc, _) = Nuevo();
        var recurso = svc.RegisterResource(Ref.Create("test.recurso", "bogota"), 1, "America/Bogota",
            new[] { new OpeningRule(DayOfWeek.Monday, new TimeOnly(8, 20), new TimeOnly(10, 0)) },
            CancellationPolicy.Anytime, Llave("bogota"), 40).Value;

        var franjas = svc.ListSlots(recurso.Id, Lunes0, Lunes0.AddDays(1)).Value.Items;

        Assert.Equal(new[] { Lunes0.AddHours(13).AddMinutes(20), Lunes0.AddHours(14) }, franjas.Select(s => s.Window.Start));
        Assert.All(franjas, s => Assert.True(
            svc.CreateHold(recurso.Id, s.Window, Ref.Create("t.p", s.Window.Start.ToString("HHmm")), null, Llave($"f{s.Window.Start:HHmm}")).IsOk));
    }

    [Fact]
    public void Sin_franja_o_sin_horario_no_hay_grilla_y_el_rango_tiene_tope()
    {
        var (svc, _) = Nuevo();
        var sinFranja = RegistrarRecurso(svc);
        var conFranja = RegistrarConFranja(svc, 30);

        Assert.Equal("booking.slots_undefined", svc.ListSlots(sinFranja.Id, Lunes0, Lunes0.AddDays(1)).Rejection?.Code);
        Assert.Equal("booking.range_too_long", svc.ListSlots(conFranja.Id, Lunes0, Lunes0.AddDays(8)).Rejection?.Code);
        Assert.Equal("booking.bad_range", svc.ListSlots(conFranja.Id, Lunes0, Lunes0).Rejection?.Code);
        Assert.Equal("booking.resource_not_found", svc.ListSlots("no-existe", Lunes0, Lunes0.AddDays(1)).Rejection?.Code);
    }

    // ── Revisar un recurso (núcleo, NB-7 de 011 y NN-6 de 005) ──────────────

    private static readonly OpeningRule[] DeOchoADoce = { new(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(12, 0)) };

    private static Reservation Reservar(BookingService svc, Resource recurso, DateTimeOffset inicio, int minutos, string quien)
    {
        var hold = svc.CreateHold(recurso.Id, Ventana(inicio, minutos), Ref.Create("t.p", quien), null, Llave($"h-{quien}")).Value;
        return svc.ConfirmHold(hold.Id, Llave($"c-{quien}")).Value;
    }

    [Fact]
    public void Revisar_cambia_lo_que_el_negocio_cambia_y_no_el_sujeto_ni_el_huso()
    {
        var (svc, _) = Nuevo();
        var recurso = RegistrarConFranja(svc, 30);
        var nuevoHorario = new[] { new OpeningRule(DayOfWeek.Tuesday, new TimeOnly(9, 0), new TimeOnly(13, 0)) };

        var r = svc.ReviseResource(recurso.Id, 3, nuevoHorario, new CancellationPolicy(TimeSpan.FromHours(1)), null, Llave("rev")).Value;

        var guardado = svc.GetResource(recurso.Id).Value;
        Assert.Equal(r.Resource, guardado);
        Assert.Equal((3, (int?)null, TimeSpan.FromHours(1)), (guardado.Capacity, guardado.SlotMinutes, guardado.Policy.MinNoticeBeforeStart));
        Assert.Equal(nuevoHorario, guardado.Opening);
        Assert.Equal((recurso.Subject, recurso.TimeZoneId), (guardado.Subject, guardado.TimeZoneId));
        Assert.Empty(r.Outside);
    }

    [Fact]
    public void Revisar_no_cancela_nada_y_dice_que_reservas_vigentes_quedan_fuera()
    {
        // Cupos de 2 a 1 y el horario de 08:00–12:00 a 08:30–11:00. Fuera: la segunda de las 09:00 (la
        // PRIMERA conserva su lugar) y la de las 11:30. No salen la cancelada ni la de las 08:00, que también
        // quedaría fuera pero ya terminó. Y ninguna se canceló.
        var (svc, reloj) = Nuevo();
        var recurso = svc.RegisterResource(Ref.Create("test.recurso", "rev"), 2, "UTC", DeOchoADoce,
            CancellationPolicy.Anytime, Llave("rev"), 30).Value;
        var yaPaso = Reservar(svc, recurso, Lunes8, 30, "paso");
        reloj.Avanzar(TimeSpan.FromMinutes(1));
        var primera = Reservar(svc, recurso, Lunes8.AddHours(1), 30, "primera");
        reloj.Avanzar(TimeSpan.FromMinutes(1));
        var segunda = Reservar(svc, recurso, Lunes8.AddHours(1), 30, "segunda");
        var tarde = Reservar(svc, recurso, Lunes8.AddMinutes(210), 30, "tarde");
        svc.CancelReservation(Reservar(svc, recurso, Lunes8.AddHours(2), 30, "cancelada").Id);
        reloj.Avanzar(TimeSpan.FromMinutes(31));

        var r = svc.ReviseResource(recurso.Id, 1,
            new[] { new OpeningRule(DayOfWeek.Monday, new TimeOnly(8, 30), new TimeOnly(11, 0)) },
            CancellationPolicy.Anytime, 30, Llave("menos")).Value;

        Assert.Equal(
            new[] { (segunda.Id, "booking.at_capacity"), (tarde.Id, "booking.outside_opening_hours") }.OrderBy(x => x.Item1),
            r.Outside.Select(o => (o.Reservation.Id, o.Reason.Code)).OrderBy(x => x.Item1));
        Assert.All(new[] { yaPaso, primera, segunda, tarde },
            x => Assert.Equal(ReservationStatus.Confirmed, svc.GetReservation(x.Id).Value.Status));
    }

    [Fact]
    public void Revisar_con_otra_franja_dice_cuales_ya_no_son_franja()
    {
        var (svc, _) = Nuevo();
        var recurso = RegistrarConFranja(svc, 30);
        var media = Reservar(svc, recurso, Lunes8.AddMinutes(90), 30, "media");

        var r = svc.ReviseResource(recurso.Id, 1, DeOchoADoce, CancellationPolicy.Anytime, 60, Llave("hora")).Value;

        Assert.Equal((media.Id, "booking.bad_slot_length"), r.Outside.Select(o => (o.Reservation.Id, o.Reason.Code)).Single());
    }

    [Fact]
    public void Revisar_otra_vez_con_la_misma_llave_no_reescribe_y_con_la_de_otro_recurso_es_conflicto()
    {
        var (svc, _) = Nuevo();
        var uno = RegistrarConFranja(svc, 30);
        var otro = RegistrarRecurso(svc, subjectId: "otro");

        svc.ReviseResource(uno.Id, 3, DeOchoADoce, CancellationPolicy.Anytime, 30, Llave("k"));
        var repetida = svc.ReviseResource(uno.Id, 5, DeOchoADoce, CancellationPolicy.Anytime, 30, Llave("k"));
        var ajena = svc.ReviseResource(otro.Id, 2, DeOchoADoce, CancellationPolicy.Anytime, 30, Llave("k"));

        Assert.Equal(3, repetida.Value.Resource.Capacity);
        Assert.Equal(3, svc.GetResource(uno.Id).Value.Capacity);
        Assert.Equal("booking.idempotency_conflict", ajena.Rejection?.Code);
        Assert.Equal(1, svc.GetResource(otro.Id).Value.Capacity);
    }

    [Fact]
    public void Revisar_rechaza_lo_que_registrar_rechaza()
    {
        var (svc, _) = Nuevo();
        var recurso = RegistrarConFranja(svc, 30);

        Assert.Equal("booking.bad_capacity",
            svc.ReviseResource(recurso.Id, 0, DeOchoADoce, CancellationPolicy.Anytime, 30, Llave("a")).Rejection?.Code);
        Assert.Equal("booking.bad_slot_minutes",
            svc.ReviseResource(recurso.Id, 1, DeOchoADoce, CancellationPolicy.Anytime, 0, Llave("b")).Rejection?.Code);
        Assert.Equal("booking.resource_not_found",
            svc.ReviseResource("no-existe", 1, DeOchoADoce, CancellationPolicy.Anytime, 30, Llave("c")).Rejection?.Code);
        Assert.Equal(30, svc.GetResource(recurso.Id).Value.SlotMinutes);
    }

    [Fact]
    public void Un_hold_vigente_BLOQUEA_el_cupo_aunque_nadie_haya_confirmado()
    {
        // Es la razón de ser del hold. Sin esto, dos personas pagarían la misma cita y el
        // conflicto aparecería después del cobro — donde ya cuesta plata.
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);
        var ventana = Ventana(Lunes8.AddHours(2), 30);

        svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "1"), null, Llave("k1"));
        var segundo = svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "2"), null, Llave("k2"));

        Assert.False(segundo.IsOk);
        Assert.Equal("booking.at_capacity", segundo.Rejection!.Code);
    }

    [Fact]
    public void Cuando_el_hold_VENCE_el_cupo_vuelve_a_estar_libre()
    {
        // Un cliente que se cae a mitad del flujo no suelta nada. Sin vencimiento, cada intento
        // abandonado se queda con un cupo para siempre.
        var (svc, reloj) = Nuevo();
        var recurso = RegistrarRecurso(svc);
        var ventana = Ventana(Lunes8.AddHours(5), 30);

        svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "1"), TimeSpan.FromMinutes(10), Llave("k1"));
        reloj.Avanzar(TimeSpan.FromMinutes(11));

        var otro = svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "2"), null, Llave("k2"));

        Assert.True(otro.IsOk);
    }

    [Fact]
    public void Soltar_un_hold_libera_el_cupo_de_inmediato()
    {
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);
        var ventana = Ventana(Lunes8.AddHours(2), 30);

        var hold = svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "1"), null, Llave("k1"));
        svc.ReleaseHold(hold.Value.Id);

        Assert.True(svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "2"), null, Llave("k2")).IsOk);
    }

    [Fact]
    public void Un_hold_confirmado_NO_gasta_dos_plazas()
    {
        // Si el hold siguiera contando después de confirmar, un recurso de capacidad 1 quedaría
        // bloqueado por su propia reserva y no aceptaría ni la ventana siguiente.
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);

        var h1 = svc.CreateHold(recurso.Id, Ventana(Lunes8.AddHours(2), 30), Ref.Create("t.p", "1"), null, Llave("k1"));
        svc.ConfirmHold(h1.Value.Id, Llave("k2"));

        var disponible = svc.CheckAvailability(recurso.Id, Ventana(Lunes8.AddHours(2), 30));

        Assert.True(disponible.IsOk);
        Assert.Equal(1, disponible.Value.Taken);   // la reserva, no la reserva + el hold
        Assert.False(disponible.Value.Available);
    }

    [Fact]
    public void Cancelar_una_reserva_devuelve_el_cupo()
    {
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);
        var ventana = Ventana(Lunes8.AddHours(2), 30);

        var hold = svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "1"), null, Llave("k1"));
        var reserva = svc.ConfirmHold(hold.Value.Id, Llave("k2"));
        svc.CancelReservation(reserva.Value.Id);

        Assert.True(svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "2"), null, Llave("k3")).IsOk);
    }

    // ── Idempotencia ────────────────────────────────────────────────────────

    [Fact]
    public void Reintentar_con_la_MISMA_llave_devuelve_el_mismo_hold_y_no_crea_otro()
    {
        // Es el caso real: la API no contestó por un timeout y el llamador no sabe si se
        // ejecutó. Sin esto, reintentar toma DOS cupos del mismo recurso para la misma persona.
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc, capacity: 5);
        var ventana = Ventana(Lunes8.AddHours(2), 30);

        var a = svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "1"), null, Llave("misma"));
        var b = svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "1"), null, Llave("misma"));

        Assert.True(a.IsOk && b.IsOk);
        Assert.Equal(a.Value.Id, b.Value.Id);
        Assert.Equal(1, svc.CheckAvailability(recurso.Id, ventana).Value.Taken);
    }

    [Fact]
    public void El_reintento_funciona_con_el_ULTIMO_cupo_y_no_choca_con_su_propio_hold()
    {
        // El test de arriba usaba capacidad 5 y por eso NO vio el defecto: con capacidad de
        // sobra, el reintento pasaba la verificación de cupo por casualidad. Con capacidad 1 —el
        // caso real de un consultorio— el reintento se topaba con el hold que él mismo había
        // creado y salía rechazado por at_capacity, que es exactamente lo que la llave existía
        // para evitar. Lo destapó un run con el proceso vivo, no un test.
        //
        // La corrección: la idempotencia se resuelve ANTES que cualquier regla que dependa del
        // estado.
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc, capacity: 1);
        var ventana = Ventana(Lunes8.AddHours(2), 30);

        var a = svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "1"), null, Llave("misma"));
        var b = svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "1"), null, Llave("misma"));

        Assert.True(a.IsOk);
        Assert.True(b.IsOk, $"el reintento tenía que devolver el mismo hold, y devolvió {b.Rejection}");
        Assert.Equal(a.Value.Id, b.Value.Id);
    }

    [Fact]
    public void El_reintento_de_una_CONFIRMACION_devuelve_la_misma_reserva()
    {
        // La misma trampa del lado de confirmar: sin resolver la idempotencia primero, el
        // reintento se topa con hold_already_confirmed —el estado que dejó el primer intento—
        // en vez de devolver la reserva que ya existe.
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);
        var hold = svc.CreateHold(recurso.Id, Ventana(Lunes8.AddHours(2), 30), Ref.Create("t.p", "1"), null, Llave("h"));

        var a = svc.ConfirmHold(hold.Value.Id, Llave("misma"));
        var b = svc.ConfirmHold(hold.Value.Id, Llave("misma"));

        Assert.True(a.IsOk);
        Assert.True(b.IsOk, $"el reintento tenía que devolver la misma reserva, y devolvió {b.Rejection}");
        Assert.Equal(a.Value.Id, b.Value.Id);
    }

    [Fact]
    public void El_reintento_de_una_CANCELACION_con_la_misma_llave_devuelve_la_cancelada_y_otra_llave_choca()
    {
        // Núcleo T052: la operación posterior cancela y su devolución cae; el reintento repite la MISMA llave y tiene que
        // seguir, no topar con already_cancelled. Otra llave —o ninguna— es otra intención, y una cancelada ya lo está:
        // es lo que impide que una operación nueva devuelva dos veces.
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);
        var hold = svc.CreateHold(recurso.Id, Ventana(Lunes8.AddHours(2), 30), Ref.Create("t.p", "1"), null, Llave("h"));
        var reserva = svc.ConfirmHold(hold.Value.Id, Llave("c")).Value;

        var a = svc.CancelReservation(reserva.Id, Llave("cancel-1"));
        var b = svc.CancelReservation(reserva.Id, Llave("cancel-1"));

        Assert.True(a.IsOk);
        Assert.True(b.IsOk, $"el reintento tenía que devolver la cancelada, y devolvió {b.Rejection}");
        Assert.Equal((ReservationStatus.Cancelled, a.Value.CancelledAt), (b.Value.Status, b.Value.CancelledAt));
        Assert.Equal("booking.already_cancelled", svc.CancelReservation(reserva.Id, Llave("cancel-2")).Rejection?.Code);
        Assert.Equal("booking.already_cancelled", svc.CancelReservation(reserva.Id).Rejection?.Code);

        // La llave es de ESA reserva: la misma en otra no la da por cancelada.
        var otra = svc.ConfirmHold(svc.CreateHold(recurso.Id, Ventana(Lunes8.AddHours(4), 30), Ref.Create("t.p", "1"), null, Llave("h2")).Value.Id, Llave("c2")).Value;
        Assert.Equal(ReservationStatus.Confirmed, svc.GetReservation(otra.Id).Value.Status);
        var deLaOtra = svc.CancelReservation(otra.Id, Llave("cancel-1"));
        Assert.True(deLaOtra.IsOk);
        Assert.Equal(ReservationStatus.Cancelled, deLaOtra.Value.Status);
    }

    [Fact]
    public void El_reintento_de_un_REGISTRO_devuelve_el_mismo_recurso()
    {
        var (svc, _) = Nuevo();

        var a = svc.RegisterResource(Ref.Create("t.r", "x"), 1, "UTC", Array.Empty<OpeningRule>(), CancellationPolicy.Anytime, Llave("misma"));
        var b = svc.RegisterResource(Ref.Create("t.r", "x"), 1, "UTC", Array.Empty<OpeningRule>(), CancellationPolicy.Anytime, Llave("misma"));

        Assert.Equal(a.Value.Id, b.Value.Id);
        Assert.Single(svc.ListResources(0, 10).Items);
    }

    [Fact]
    public void La_llave_se_recuerda_por_AMBITO_y_no_globalmente()
    {
        // Un cliente puede usar la misma llave para el hold y para la confirmación del mismo
        // intento lógico. Si el ámbito fuera global, la confirmación devolvería el hold.
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);
        var hold = svc.CreateHold(recurso.Id, Ventana(Lunes8.AddHours(2), 30), Ref.Create("t.p", "1"), null, Llave("igual"));

        var reserva = svc.ConfirmHold(hold.Value.Id, Llave("igual"));

        Assert.True(reserva.IsOk);
        Assert.NotEqual(hold.Value.Id, reserva.Value.Id);
    }

    [Fact]
    public void Confirmar_dos_veces_el_mismo_hold_con_llaves_distintas_es_CONFLICTO()
    {
        // La idempotencia protege el reintento, no el doble uso: dos llaves distintas son dos
        // intenciones distintas, y la segunda tiene que fallar visiblemente.
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);
        var hold = svc.CreateHold(recurso.Id, Ventana(Lunes8.AddHours(2), 30), Ref.Create("t.p", "1"), null, Llave("k1"));

        svc.ConfirmHold(hold.Value.Id, Llave("k2"));
        var otra = svc.ConfirmHold(hold.Value.Id, Llave("k3"));

        Assert.False(otra.IsOk);
        Assert.Equal("booking.hold_already_confirmed", otra.Rejection!.Code);
    }

    // ── Validación y bordes ─────────────────────────────────────────────────

    [Fact]
    public void Un_hold_sobre_un_recurso_inexistente_es_NOT_FOUND()
    {
        var (svc, _) = Nuevo();

        var bad = svc.CreateHold("no-existe", Ventana(Lunes8.AddHours(2), 30), Ref.Create("t.p", "1"), null, Llave("k1"));

        Assert.Equal(RejectionKind.NotFound, bad.Rejection!.Kind);
    }

    [Fact]
    public void Un_TTL_fuera_de_rango_se_rechaza_como_INVALID()
    {
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);
        var ventana = Ventana(Lunes8.AddDays(1), 30);

        var cero = svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "1"), TimeSpan.Zero, Llave("k1"));
        var eterno = svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", "1"), TimeSpan.FromDays(30), Llave("k2"));

        Assert.Equal(RejectionKind.Invalid, cero.Rejection!.Kind);
        Assert.Equal("booking.bad_ttl", eterno.Rejection!.Code);
    }

    [Fact]
    public void Un_recurso_con_capacidad_cero_no_se_registra()
    {
        var (svc, _) = Nuevo();

        var bad = svc.RegisterResource(Ref.Create("t.r", "x"), 0, "UTC",
            Array.Empty<OpeningRule>(), CancellationPolicy.Anytime, Llave("k1"));

        Assert.Equal("booking.bad_capacity", bad.Rejection!.Code);
    }

    [Fact]
    public void La_disponibilidad_explica_POR_QUE_no_y_no_solo_que_no()
    {
        // Sin el código de causa, el cliente solo puede mostrar "no disponible" a alguien que
        // no sabe qué cambiar — ¿otra hora? ¿otro día? ¿otro recurso?
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);

        var pasado = svc.CheckAvailability(recurso.Id, Ventana(Lunes8.AddHours(-2), 30));

        Assert.True(pasado.IsOk);
        Assert.False(pasado.Value.Available);
        Assert.Equal("booking.window_in_the_past", pasado.Value.Reason!.Code);
    }

    [Fact]
    public void El_Ref_del_titular_vuelve_INTACTO_y_sin_interpretarse()
    {
        // La agnosticidad, comprobada de punta a punta: Booking guardó un vocabulario que no
        // entiende y lo devolvió igual.
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);
        var quien = Ref.Create("salud.paciente", "urn:mrn:8891:a");

        var hold = svc.CreateHold(recurso.Id, Ventana(Lunes8.AddHours(2), 30), quien, null, Llave("k1"));
        var reserva = svc.ConfirmHold(hold.Value.Id, Llave("k2"));

        Assert.Equal(quien, reserva.Value.For);
        Assert.Equal("urn:mrn:8891:a", reserva.Value.For.Id);
    }

    [Fact]
    public void Cancelar_fuera_de_plazo_se_rechaza_con_la_politica_del_RECURSO()
    {
        var (svc, reloj) = Nuevo();
        var recurso = RegistrarRecurso(svc, noticeMinutes: 60);
        var hold = svc.CreateHold(recurso.Id, Ventana(Lunes8.AddHours(2), 30), Ref.Create("t.p", "1"), null, Llave("k1"));
        var reserva = svc.ConfirmHold(hold.Value.Id, Llave("k2"));

        reloj.Avanzar(TimeSpan.FromMinutes(90));   // faltan 30 min y la política exige 60

        var bad = svc.CancelReservation(reserva.Value.Id);

        Assert.Equal("booking.cancellation_too_late", bad.Rejection!.Code);
    }

    [Fact]
    public void El_listado_de_reservas_es_ESTABLE_entre_llamadas_iguales()
    {
        // Sin desempate, dos peticiones idénticas devuelven órdenes distintos y la lista
        // "parpadea" sin que nada haya cambiado.
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc, capacity: 10);
        var ventana = Ventana(Lunes8.AddHours(2), 30);

        for (var i = 0; i < 5; i++)
        {
            var h = svc.CreateHold(recurso.Id, ventana, Ref.Create("t.p", $"{i}"), null, Llave($"h{i}"));
            svc.ConfirmHold(h.Value.Id, Llave($"r{i}"));
        }

        var a = svc.ListReservations(recurso.Id, null, 0, 10).Value.Items.Select(x => x.Id);
        var b = svc.ListReservations(recurso.Id, null, 0, 10).Value.Items.Select(x => x.Id);

        Assert.Equal(a, b);
    }

    [Fact]
    public void Soltar_un_hold_YA_suelto_no_es_un_error()
    {
        // Rechazarlo obligaría a cada cliente a distinguir "no pude" de "ya estaba", que es
        // justo el ruido que un release debería ahorrarle.
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc);
        var hold = svc.CreateHold(recurso.Id, Ventana(Lunes8.AddHours(2), 30), Ref.Create("t.p", "1"), null, Llave("k1"));

        svc.ReleaseHold(hold.Value.Id);

        Assert.True(svc.ReleaseHold(hold.Value.Id).IsOk);
    }

    // ── Del actor a sus reservas (HU #124) ──────────────────────────────────
    //
    // El simétrico de la HU #25. Allá se cerró "del sujeto a su recurso"; acá faltaba la
    // dirección que se usa más: "mis citas", "mis visitas", "mis reservas de viaje".
    //
    // EL FIXTURE LLEVA DOS ACTORES Y DOS RECURSOS a propósito. Con un solo actor, filtrar y no
    // filtrar devuelven lo mismo y el defecto pasa en VERDE; con un solo recurso, el filtro por
    // actor no se distingue del filtro por recurso que ya existía.
    private static Reservation Reservar(
        BookingService svc, Resource recurso, Ref para, DateTimeOffset inicio, string semilla)
    {
        var hold = svc.CreateHold(recurso.Id, Ventana(inicio, 30), para, null, Llave($"h-{semilla}"));
        return svc.ConfirmHold(hold.Value.Id, Llave($"r-{semilla}")).Value;
    }

    private sealed record Agenda(BookingService Svc, Resource Consultorio, Resource Sala, Ref Ana, Ref Beto);

    private static Agenda AgendaCompartida()
    {
        var (svc, _) = Nuevo();
        var consultorio = RegistrarRecurso(svc, capacity: 10, subjectId: "consultorio");
        var sala = RegistrarRecurso(svc, capacity: 10, subjectId: "sala");
        var ana = Ref.Create("test.persona", "ana");
        var beto = Ref.Create("test.persona", "beto");

        Reservar(svc, consultorio, ana, Lunes8.AddHours(2), "ana-consultorio");
        Reservar(svc, sala, ana, Lunes8.AddHours(4), "ana-sala");
        Reservar(svc, consultorio, beto, Lunes8.AddHours(3), "beto-consultorio");

        return new Agenda(svc, consultorio, sala, ana, beto);
    }

    [Fact]
    public void Un_actor_SIN_reservas_devuelve_vacio_y_no_falla()
    {
        // El caso vacío, y la decisión que lo acompaña: un Ref es vocabulario de QUIEN LLAMA y
        // esta capacidad no puede saber si existe —comprobarlo exigiría interpretarlo—. Así que
        // "no tiene citas" es la respuesta verdadera. Con not_found, cada portal tendría que
        // tratar una bandeja vacía como un fallo.
        var a = AgendaCompartida();

        var res = a.Svc.ListReservations(null, Ref.Create("test.persona", "nadie"), 0, 10);

        Assert.True(res.IsOk);
        Assert.Empty(res.Value.Items);
        Assert.Equal(0, res.Value.Total);
    }

    [Fact]
    public void Las_reservas_de_un_actor_salen_de_TODOS_sus_recursos()
    {
        // El caso feliz, y la razón de ser de la HU: quien pregunta "mis citas" no sabe por qué
        // recurso preguntar —el identificador del recurso lo genera ESTA capacidad—, y sus citas
        // no están todas en el mismo. Con las dos de Ana en un solo recurso, barrer recursos de a
        // uno habría bastado y este endpoint no haría falta.
        var a = AgendaCompartida();

        var mias = a.Svc.ListReservations(null, a.Ana, 0, 10).Value;

        Assert.Equal(2, mias.Total);
        Assert.Equal(new[] { a.Consultorio.Id, a.Sala.Id }, mias.Items.Select(r => r.ResourceId));
        Assert.All(mias.Items, r => Assert.Equal(a.Ana, r.For));
    }

    [Fact]
    public void Dos_actores_NO_se_ven_las_reservas()
    {
        // El caso filtro. Beto comparte recurso y día con Ana: si el filtro se cayera, su reserva
        // aparecería en la bandeja de ella. Es el defecto que este test existe para cazar.
        var a = AgendaCompartida();

        var deBeto = a.Svc.ListReservations(null, a.Beto, 0, 10).Value;

        Assert.Single(deBeto.Items);
        Assert.Equal(a.Beto, deBeto.Items[0].For);
        Assert.DoesNotContain(deBeto.Items, r => r.For == a.Ana);
    }

    [Fact]
    public void Listar_por_actor_dos_veces_devuelve_lo_MISMO()
    {
        // El caso idempotente: una lectura repetida no cambia nada ni devuelve otro orden. Sin
        // desempate estable la lista "parpadea" sin que nada haya cambiado — y acá importa más
        // que en el listado por recurso, porque dos reservas de la misma persona en recursos
        // distintos pueden empezar a la misma hora.
        var a = AgendaCompartida();

        var una = a.Svc.ListReservations(null, a.Ana, 0, 10).Value.Items.Select(r => r.Id).ToList();
        var otra = a.Svc.ListReservations(null, a.Ana, 0, 10).Value.Items.Select(r => r.Id).ToList();

        Assert.Equal(una, otra);
    }

    [Fact]
    public void Los_dos_filtros_juntos_INTERSECAN()
    {
        // Es lo único que pueden significar dos filtros sobre la misma lista. Y el fixture lo
        // exige: Ana tiene dos reservas y el consultorio tiene dos, así que un OR o un filtro
        // ignorado devolverían 2 o 3, nunca 1.
        var a = AgendaCompartida();

        var aqui = a.Svc.ListReservations(a.Consultorio.Id, a.Ana, 0, 10).Value;

        Assert.Single(aqui.Items);
        Assert.Equal(a.Consultorio.Id, aqui.Items[0].ResourceId);
        Assert.Equal(a.Ana, aqui.Items[0].For);
    }

    [Fact]
    public void Sin_NINGUN_filtro_se_rechaza_nombrando_la_regla_y_no_el_campo()
    {
        // Listar todo crece con el almacén y no cabe en una página. El código se llamaba
        // resource_id_required —el instinto correcto dicho sobre el único filtro que existía—; con
        // dos filtros ese nombre MENTIRÍA, porque un for también sirve.
        var a = AgendaCompartida();

        var bad = a.Svc.ListReservations(null, null, 0, 10);

        Assert.False(bad.IsOk);
        Assert.Equal("booking.filter_required", bad.Rejection!.Code);
    }

    [Fact]
    public void El_Ref_se_compara_ENTERO_y_no_por_su_identificador()
    {
        // §0.B.13: el Ref se guarda y se devuelve, nunca se ramifica ni se parte. Comparar sólo
        // el Id haría que dos vocabularios distintos con el mismo identificador —un "1" de salud
        // y un "1" de viajes— se vieran las reservas. No se nota hasta el segundo dominio, que es
        // exactamente cuando ya es caro.
        var (svc, _) = Nuevo();
        var recurso = RegistrarRecurso(svc, capacity: 10);
        Reservar(svc, recurso, Ref.Create("salud.paciente", "1"), Lunes8.AddHours(2), "salud");
        Reservar(svc, recurso, Ref.Create("viajes.huesped", "1"), Lunes8.AddHours(3), "viajes");

        var deSalud = svc.ListReservations(null, Ref.Create("salud.paciente", "1"), 0, 10).Value;

        Assert.Single(deSalud.Items);
        Assert.Equal("salud.paciente", deSalud.Items[0].For.Kind);
    }
}
