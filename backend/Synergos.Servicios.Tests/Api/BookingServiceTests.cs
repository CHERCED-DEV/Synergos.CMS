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
