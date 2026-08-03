using Synergos.Api.Booking.Domain;
using Synergos.Api.Booking.Storage;
using Synergos.Core;

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
    private sealed class MemoriaStores : IResourceStore, IHoldStore, IReservationStore, IIdempotencyStore
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

    private static Resource RegistrarRecurso(BookingService svc, int capacity = 1, int noticeMinutes = 0)
        => svc.RegisterResource(
            Ref.Create("test.recurso", "x"), capacity, "UTC",
            Array.Empty<OpeningRule>(), new CancellationPolicy(TimeSpan.FromMinutes(noticeMinutes)),
            Llave(Guid.NewGuid().ToString("n"))).Value;

    private static TimeWindow Ventana(DateTimeOffset inicio, int minutos)
        => TimeWindow.Starting(inicio, TimeSpan.FromMinutes(minutos));

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

        var a = svc.ListReservations(recurso.Id, 0, 10).Value.Items.Select(x => x.Id);
        var b = svc.ListReservations(recurso.Id, 0, 10).Value.Items.Select(x => x.Id);

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
}
