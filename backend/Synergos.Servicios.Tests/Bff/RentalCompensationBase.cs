using System.Net;
using Synergos.Bff.Alquiler.Domain;
using Synergos.Bff.Core;
using static Synergos.CMS.Tests.Bff.DobleDeAlquiler;

namespace Synergos.CMS.Tests.Bff;

/// <summary>Lo que el oráculo mira de un alquiler, por cualquiera de los dos motores.</summary>
/// <param name="Ok">Si la operación salió.</param>
/// <param name="Codigo">El código del rechazo, si lo hubo.</param>
/// <param name="Id">El alquiler (la saga); vacío si la operación no salió.</param>
/// <param name="GarantiaId">El cobro de la garantía, autorizado y sin capturar.</param>
/// <param name="CobroId">El cobro del alquiler.</param>
/// <param name="Apartados">Cada unidad apartada, con el recurso sobre el que se apartó.</param>
/// <param name="Estado">Como lo proyecta el motor para quien lo lee: <c>reserved</c>, <c>returned</c>…</param>
/// <param name="GarantiaRetenida">Cuánto sigue retenido; nulo cuando no se sabe.</param>
/// <param name="DanioCobrado">Cuánto se cobró de la garantía al cerrar.</param>
public sealed record VistaDeRenta(
    bool Ok, string? Codigo, string Id, string? GarantiaId, string? CobroId,
    IReadOnlyList<(string Hold, string Recurso)> Apartados, string? Estado, decimal? GarantiaRetenida, decimal DanioCobrado);

/// <summary>
/// Quién maneja el motor en el oráculo: el imperativo construye <c>RentalFlow</c>; el declarado levanta el
/// host de <c>alquiler.renta</c>. Los dos sobre el MISMO doble (<see cref="DobleDeAlquiler"/>).
/// </summary>
public abstract class ConductorDeRenta
{
    protected ConductorDeRenta(CapacidadesFalsas caps) => Caps = caps;

    /// <summary>Las capacidades guionadas, con lo que se les pidió.</summary>
    public CapacidadesFalsas Caps { get; }

    /// <summary>Reserva y deja confirmado: aparta, retiene la garantía, cobra y confirma.</summary>
    public abstract Task<VistaDeRenta> ReservarAsync(string id = "alq-1", int unidades = 1, decimal garantia = 400000m);

    /// <summary>El equipo volvió, con el daño que haya.</summary>
    public abstract Task<VistaDeRenta> DevolverAsync(string id, decimal danio);

    /// <summary>El inquilino declara que devolvió el equipo; la garantía sigue retenida.</summary>
    public abstract Task<VistaDeRenta> DeclararAsync(string id);

    /// <summary>Se cancela un alquiler confirmado: el personal con la penalidad que fije, el inquilino sin ella.</summary>
    public abstract Task<VistaDeRenta> CancelarAsync(string id, decimal penalidad, bool porPersonal);

    /// <summary>En qué estado quedó la saga; nulo si no existe.</summary>
    public abstract SagaStatus? Estado(string id);

    /// <summary>Cuántas compensaciones le quedan pendientes.</summary>
    public abstract int Pendientes(string id);

    /// <summary>Adelanta el reloj del motor.</summary>
    public abstract void Avanzar(TimeSpan cuanto);

    /// <summary>Si el barrido de abandono o el de compensaciones tendría algo que mirar AHORA.</summary>
    public abstract bool HayCandidatasAlBarrido();
}

/// <summary>
/// El oráculo de la RESERVA del alquiler (HU #147), por cualquier motor: los dos cobros con vidas distintas, lo
/// que se deshace y cómo, la llave, y la saga que el barrido no toca.
/// </summary>
/// <remarks>
/// <para><b>Lo que este dominio aporta y los cuatro anteriores no: DOS COBROS CON VIDAS DISTINTAS.</b> El
/// alquiler se autoriza y se CAPTURA al reservar; la garantía se autoriza y se queda <b>sin capturar</b> mientras
/// el equipo está fuera. Los cuatro flujos anteriores autorizan para capturar, así que ninguno tenía ese estado
/// intermedio — y es el que decide si deshacer significa anular o devolver.</para>
///
/// <para><b>Y la saga se CIERRA al reservar</b>, que es la propiedad que más barato se pierde: una que siguiera
/// <c>Running</c> hasta la devolución la daría por muerta el barrido de abandono a la hora, soltando la ventana y
/// anulando la garantía de un alquiler VIVO. Por eso hay un test que adelanta el reloj cuatro días y exige que el
/// barrido no la toque.</para>
///
/// <para><b>Partido en base y conductor</b> (línea 005, research R12): las aserciones son las de siempre y las
/// corre cada motor. Lo que sólo tiene sentido en uno —la proyección del imperativo, los sujetos de sus cobros—
/// se queda en su clase.</para>
/// </remarks>
public abstract class RentalReservaBase
{
    /// <summary>Un conductor nuevo sobre esas capacidades.</summary>
    protected abstract ConductorDeRenta Nuevo(CapacidadesFalsas caps);

    // ── Los dos cobros tienen vidas distintas ──────────────────────────────

    /// <summary>
    /// La garantía se AUTORIZA y no se captura; el alquiler sí.
    /// </summary>
    /// <remarks>
    /// Es la propiedad entera del vertical. Si la garantía se capturara al reservar, devolver el
    /// equipo sin daño obligaría a un reembolso —plata que se mueve dos veces y comisión que se
    /// paga dos veces— en vez de una anulación que no mueve nada.
    /// </remarks>
    [Fact]
    public async Task La_garantia_se_RETIENE_sin_capturar_y_el_alquiler_se_cobra()
    {
        var c = Nuevo(FelizEstricto());

        var r = await c.ReservarAsync();

        Assert.True(r.Ok);
        Assert.Equal("pg-dep", r.GarantiaId);
        Assert.Equal("pg-ren", r.CobroId);

        // Dos autorizaciones, UNA captura, y sobre el alquiler.
        Assert.Equal(2, c.Caps.VecesEn("POST", "/v1/payments"));
        Assert.Equal(1, c.Caps.Veces("POST", "/capture"));
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/payments/pg-ren/capture"));
        Assert.Equal(0, c.Caps.Veces("POST", "/v1/payments/pg-dep/capture"));
    }

    /// <summary>Sin garantía que retener, no se autoriza una de cero.</summary>
    /// <remarks>
    /// Un alquiler sin depósito es legítimo —una herramienta de mano— y una autorización de cero
    /// la rechazaría la pasarela. El fixture lo exige con <c>garantia: 0</c>, que es el caso que
    /// la rama SÍ produce.
    /// </remarks>
    [Fact]
    public async Task Sin_garantia_no_se_autoriza_una_de_cero()
    {
        var c = Nuevo(FelizEstricto());

        var r = await c.ReservarAsync(garantia: 0m);

        Assert.True(r.Ok);
        Assert.Null(r.GarantiaId);
        Assert.Equal(1, c.Caps.VecesEn("POST", "/v1/payments"));
    }

    // ── Lo que se deshace, y CÓMO ──────────────────────────────────────────

    /// <summary>
    /// Si no se puede retener la garantía, la ventana vuelve y NO se cobra nada.
    /// </summary>
    [Fact]
    public async Task Sin_garantia_retenible_se_suelta_la_ventana_y_no_se_cobra()
    {
        var caps = FelizEstricto();
        caps.Falla("POST /v1/payments", HttpStatusCode.PaymentRequired, "payments.declined");
        var c = Nuevo(caps);

        var r = await c.ReservarAsync();

        Assert.False(r.Ok);
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/holds/h-1/release"));
        Assert.Equal(0, c.Caps.Veces("POST", "/capture"));
        Assert.Equal(SagaStatus.Compensated, c.Estado("alq-1"));
    }

    /// <summary>
    /// Si el alquiler no se puede cobrar, la garantía se ANULA — no se devuelve.
    /// </summary>
    /// <remarks>
    /// Devolverla sería el error caro y callado: la garantía nunca se capturó, así que un
    /// <c>refund</c> lo rechazaría la pasarela y la retención se quedaría viva hasta vencer sola,
    /// con el dinero del arrendatario inmovilizado y el alquiler ya cancelado.
    /// </remarks>
    [Fact]
    public async Task Si_no_se_cobra_el_alquiler_la_garantia_se_ANULA()
    {
        var caps = FelizEstricto();
        caps.Falla("POST /v1/payments/pg-ren/capture", HttpStatusCode.PaymentRequired, "payments.declined");
        var c = Nuevo(caps);

        var r = await c.ReservarAsync();

        Assert.False(r.Ok);
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/payments/pg-dep/void"));
        Assert.Equal(0, c.Caps.Veces("POST", "/v1/payments/pg-dep/refund"));
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/holds/h-1/release"));
    }

    /// <summary>
    /// Confirmado un apartado, deshacerlo deja de ser «soltar» y pasa a ser «cancelar».
    /// </summary>
    /// <remarks>
    /// <para>Y el cobro ya capturado deja de anularse y pasa a devolverse. Las DOS reescrituras
    /// viven en el mismo escenario porque el fallo llega DESPUÉS de capturar y DESPUÉS de
    /// confirmar el primero de dos apartados: es el único punto donde las dos formas coexisten
    /// (<c>compensation_changes_character</c>).</para>
    ///
    /// <para>Con el doble estricto esto no puede pasar por accidente: si el flujo no reescribiera
    /// la compensación, soltar <c>h-1</c> daría 409 y la compensación quedaría colgada.</para>
    /// </remarks>
    [Fact]
    public async Task Al_confirmar_soltar_pasa_a_cancelar_y_anular_pasa_a_devolver()
    {
        var caps = FelizEstricto();
        caps.Falla("POST /v1/holds/h-2/confirm", HttpStatusCode.Conflict, "booking.hold_expired");
        var c = Nuevo(caps);

        var r = await c.ReservarAsync(unidades: 2);

        Assert.False(r.Ok);

        // El primero ya era reserva: se CANCELA, no se suelta.
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/reservations/res-1/cancel"));

        // El segundo nunca llegó a confirmarse: ése sí se suelta.
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/holds/h-2/release"));

        // Y el alquiler ya estaba capturado: se DEVUELVE, no se anula.
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/payments/pg-ren/refund"));
        Assert.Equal(0, c.Caps.Veces("POST", "/v1/payments/pg-ren/void"));

        // La garantía, en cambio, nunca se capturó: ésa sí se anula.
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/payments/pg-dep/void"));

        Assert.Equal(SagaStatus.Compensated, c.Estado("alq-1"));
    }

    // ── La saga se cierra al reservar ──────────────────────────────────────

    /// <summary>
    /// Reservado, la saga queda <c>Completed</c> y sin nada pendiente.
    /// </summary>
    /// <remarks>
    /// <b>Y el barrido de abandono no la toca cuatro días después</b>, que es lo que de verdad hay
    /// que vigilar: una saga que siguiera <c>Running</c> hasta la devolución sería deshecha por el
    /// barrido a los <c>AbandonAfterMinutes</c> — soltando la ventana y anulando la garantía de un
    /// alquiler vivo, sin que nada fallara y con el equipo ya en la obra.
    /// </remarks>
    [Fact]
    public async Task La_saga_se_cierra_al_reservar_y_el_barrido_no_la_toca()
    {
        var c = Nuevo(FelizEstricto());

        var r = await c.ReservarAsync();

        Assert.True(r.Ok);
        Assert.Equal(SagaStatus.Completed, c.Estado(r.Id));
        Assert.Equal(0, c.Pendientes(r.Id));

        // Cuatro días después —un alquiler perfectamente normal— sigue sin ser candidata.
        c.Avanzar(TimeSpan.FromDays(4));
        Assert.False(c.HayCandidatasAlBarrido());
    }

    // ── La llave, y lo que NO encierra ─────────────────────────────────────

    /// <summary>
    /// Un alquiler deshecho entero NO encierra a quien lo intentó.
    /// </summary>
    /// <remarks>
    /// <para>Es el defecto #41 aplicado acá: la llave se deriva de QUÉ se alquila —equipo, fechas,
    /// arrendatario— así que nunca cambia. Si devolver la saga muerta fuera «idempotencia», quien
    /// intentó alquilar un andamio y no pudo no podría volver a intentarlo jamás.</para>
    ///
    /// <para>La regla vive en <c>SagaEngine.Abrir</c> y <b>no</b> en el flujo, que es la mitad
    /// que importa: escrita acá se corregiría una vez y se olvidaría en el siguiente
    /// orquestador.</para>
    /// </remarks>
    [Fact]
    public async Task Un_alquiler_deshecho_deja_volver_a_intentar_con_la_MISMA_llave()
    {
        var caps = FelizEstricto();
        caps.Falla("POST /v1/payments", HttpStatusCode.PaymentRequired, "payments.declined");
        var c = Nuevo(caps);

        var primero = await c.ReservarAsync();
        Assert.False(primero.Ok);
        Assert.Equal(SagaStatus.Compensated, c.Estado("alq-1"));

        // La pasarela vuelve en sí y el arrendatario reintenta con la misma llave.
        caps.Secuencia("POST /v1/payments",
            """{"id":"pg-dep","status":"Authorized","amount":{"amount":400000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""",
            """{"id":"pg-ren","status":"Authorized","amount":{"amount":350000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""");
        caps.Secuencia("POST /v1/holds",
            """{"id":"h-1","resourceId":"rec-andamio","expiresAt":"2026-09-01T10:10:00+00:00"}""");

        var segundo = await c.ReservarAsync();

        Assert.True(segundo.Ok);

        // Con identidad PROPIA: sobrescribir la muerta borraría qué falló.
        Assert.NotEqual("alq-1", segundo.Id);
        Assert.Equal(SagaStatus.Compensated, c.Estado("alq-1"));
    }

    /// <summary>Un reintento sobre un alquiler VIVO devuelve ése, sin alquilar dos veces.</summary>
    [Fact]
    public async Task Un_reintento_sobre_un_alquiler_vivo_devuelve_el_mismo()
    {
        var c = Nuevo(FelizEstricto());

        var primero = await c.ReservarAsync();
        var segundo = await c.ReservarAsync();

        Assert.True(primero.Ok);
        Assert.True(segundo.Ok);
        Assert.Equal(primero.Id, segundo.Id);
        Assert.Equal(1, c.Caps.VecesEn("POST", "/v1/holds"));
        Assert.Equal(2, c.Caps.VecesEn("POST", "/v1/payments"));   // los dos de la PRIMERA vez
    }

    // ── Lo que se rechaza antes de tocar una capacidad ─────────────────────

    /// <summary>Más unidades de las que el flujo admite se rechaza.</summary>
    [Fact]
    public async Task Mas_unidades_de_las_que_caben_se_rechaza()
    {
        var c = Nuevo(FelizEstricto());

        var r = await c.ReservarAsync(unidades: RentalFlow.MaxUnits + 1);

        Assert.False(r.Ok);
        Assert.Equal("alquiler.bad_quantity", r.Codigo);
        Assert.Empty(c.Caps.Llamadas);
    }

    /// <summary>
    /// N unidades son N apartados, y ninguno es un pozo contable.
    /// </summary>
    /// <remarks>
    /// Un hold de <c>Api.Booking</c> es UNA unidad de capacidad —<c>CreateHoldRequest</c> no lleva
    /// cantidad— así que alquilar dos andamios son dos apartados. Y se mira POR DÓNDE pregunta: si
    /// alguno se hubiera ido a <c>Api.Inventory</c>, aparecería un <c>/v1/items</c>.
    /// </remarks>
    [Fact]
    public async Task Dos_unidades_son_dos_apartados_sobre_el_mismo_recurso()
    {
        var c = Nuevo(FelizEstricto());

        var r = await c.ReservarAsync(unidades: 2);

        Assert.True(r.Ok);
        Assert.Equal(2, c.Caps.VecesEn("POST", "/v1/holds"));
        Assert.Equal(2, r.Apartados.Count);
        Assert.All(r.Apartados, h => Assert.Equal("rec-andamio", h.Recurso));
        Assert.Equal(0, c.Caps.Veces("GET", "/v1/items"));
        Assert.Equal(0, c.Caps.Veces("POST", "/v1/items"));

        // Y sólo DOS cobros, no uno por unidad: se alquila un lote, no dos alquileres.
        Assert.Equal(2, c.Caps.VecesEn("POST", "/v1/payments"));
    }

    /// <summary>
    /// Las llaves de apartar y confirmar son las MISMAS en los dos motores: <c>hold:{i}</c> y <c>confirm:{i}</c>,
    /// desde 0.
    /// </summary>
    /// <remarks>
    /// <c>Api.Booking</c> sólo devuelve el mismo apartado ante la misma llave: con el interruptor entre motores,
    /// uno tiene que reconocer lo que apartó el otro. Las dos unidades son IDÉNTICAS (mismo recurso, mismas
    /// fechas), así que la llave sólo puede distinguirlas por su posición (NN-3 de la línea 005, NV-1 de N2).
    /// </remarks>
    [Fact]
    public async Task Las_llaves_de_las_unidades_son_las_mismas_en_los_dos_motores()
    {
        var c = Nuevo(FelizEstricto());

        Assert.True((await c.ReservarAsync(unidades: 2)).Ok);

        Assert.Equal(["alq-1|hold:0", "alq-1|hold:1"],
            c.Caps.Llamadas.Where(l => l.Method == "POST" && l.Path.EndsWith("/v1/holds", StringComparison.Ordinal)).Select(l => l.Key));
        Assert.Equal(["alq-1|confirm:0", "alq-1|confirm:1"],
            c.Caps.Llamadas.Where(l => l.Method == "POST" && l.Path.EndsWith("/confirm", StringComparison.Ordinal)).Select(l => l.Key));
    }
}

/// <summary>
/// El oráculo de la RESERVA y de la DEVOLUCIÓN días después —declarar y confirmar con el daño—, por cualquier motor.
/// </summary>
/// <remarks>
/// Partido de <see cref="RentalLiquidacionBase"/> (línea 005, T017) para que el camino declarado herede la
/// devolución cuando la tiene y la cancelación cuando la tenga (T020), sin <c>Skip</c> entre medias.
/// </remarks>
public abstract class RentalDevolucionBase : RentalReservaBase
{
    // ── Devolver ───────────────────────────────────────────────────────────

    /// <summary>Devolver sin daño ANULA la garantía: nunca fue un cobro.</summary>
    [Fact]
    public async Task Devolver_sin_dano_anula_la_garantia()
    {
        var c = Nuevo(FelizEstricto());
        Assert.True((await c.ReservarAsync()).Ok);

        var r = await c.DevolverAsync("alq-1", 0m);

        Assert.True(r.Ok);
        Assert.Equal("returned", r.Estado);
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/payments/pg-dep/void"));
        Assert.Equal(0, c.Caps.Veces("POST", "/v1/payments/pg-dep/capture"));

        // Devuelto el equipo, ya no hay nada retenido.
        Assert.Equal(0m, r.GarantiaRetenida);
    }

    /// <summary>
    /// Con daño se captura la garantía y se devuelve la diferencia.
    /// </summary>
    /// <remarks>
    /// El fixture cobra <b>120 000 de una garantía de 400 000</b> a propósito: con el daño igual a
    /// la garantía, «capturar y devolver el sobrante» y «capturar y ya» darían el mismo resultado
    /// y la rama del sobrante pasaría en verde sin ejecutarse.
    /// </remarks>
    [Fact]
    public async Task Con_dano_se_captura_la_garantia_y_se_devuelve_el_sobrante()
    {
        var c = Nuevo(FelizEstricto());
        Assert.True((await c.ReservarAsync()).Ok);

        var r = await c.DevolverAsync("alq-1", 120000m);

        Assert.True(r.Ok);
        Assert.Equal(120000m, r.DanioCobrado);
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/payments/pg-dep/capture"));

        var devolucion = c.Caps.Llamadas.Single(
            l => l.Path.EndsWith("/v1/payments/pg-dep/refund", StringComparison.Ordinal));
        Assert.Contains("280000", devolucion.Body!, StringComparison.Ordinal);
    }

    /// <summary>Cobrar más daño que garantía se rechaza.</summary>
    /// <remarks>
    /// No hay con qué: la retención es por un monto y la pasarela no da más. Aceptarlo dejaría un
    /// alquiler cerrado diciendo que cobró algo que nadie cobró.
    /// </remarks>
    [Fact]
    public async Task El_dano_no_puede_superar_la_garantia()
    {
        var c = Nuevo(FelizEstricto());
        Assert.True((await c.ReservarAsync()).Ok);

        var r = await c.DevolverAsync("alq-1", 500000m);

        Assert.False(r.Ok);
        Assert.Equal("alquiler.damage_exceeds_deposit", r.Codigo);
        Assert.Equal(0, c.Caps.Veces("POST", "/v1/payments/pg-dep/capture"));
    }

    /// <summary>Cerrar dos veces no cobra dos veces.</summary>
    /// <remarks>
    /// Sin esto, un reintento por timeout capturaría la garantía otra vez — y ahí hay plata de
    /// verdad, no un cupo.
    /// </remarks>
    [Fact]
    public async Task Cerrar_dos_veces_no_vuelve_a_capturar()
    {
        var c = Nuevo(FelizEstricto());
        Assert.True((await c.ReservarAsync()).Ok);

        Assert.True((await c.DevolverAsync("alq-1", 120000m)).Ok);
        var otra = await c.DevolverAsync("alq-1", 120000m);

        Assert.True(otra.Ok);
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/payments/pg-dep/capture"));
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/payments/pg-dep/refund"));
    }

    // ── Declarar y confirmar (decisión 5 del lote 1, #203) ─────────────────

    /// <summary>
    /// Declarar no mueve plata: la garantía sigue retenida, y la libera la confirmación del personal.
    /// </summary>
    /// <remarks>
    /// Es la decisión entera: el inquilino no puede liberar su propia garantía diciendo «lo devolví sin daño». Con
    /// una sola llamada a <c>/v1/payments/*</c> al declarar, el cupo de su tarjeta volvería antes de que nadie
    /// mirara el equipo.
    /// </remarks>
    [Fact]
    public async Task Declarar_no_mueve_plata_y_confirmar_despues_anula_la_garantia()
    {
        var c = Nuevo(FelizEstricto());
        Assert.True((await c.ReservarAsync()).Ok);
        var pagosAntes = c.Caps.Llamadas.Count(l => l.Path.Contains("/v1/payments", StringComparison.Ordinal));

        var declarada = await c.DeclararAsync("alq-1");

        Assert.True(declarada.Ok);
        Assert.Equal("declared", declarada.Estado);
        Assert.Equal(400000m, declarada.GarantiaRetenida);
        Assert.Equal(pagosAntes, c.Caps.Llamadas.Count(l => l.Path.Contains("/v1/payments", StringComparison.Ordinal)));

        var devuelta = await c.DevolverAsync("alq-1", 0m);

        Assert.True(devuelta.Ok);
        Assert.Equal("returned", devuelta.Estado);
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/payments/pg-dep/void"));
    }

    /// <summary>Declarar dos veces es declarar una, y lo devuelto ya no se declara.</summary>
    [Fact]
    public async Task Declarar_es_idempotente_y_no_vale_desde_otro_estado()
    {
        var c = Nuevo(FelizEstricto());
        Assert.True((await c.ReservarAsync()).Ok);

        Assert.True((await c.DeclararAsync("alq-1")).Ok);
        var otraVez = await c.DeclararAsync("alq-1");
        Assert.True(otraVez.Ok);
        Assert.Equal("declared", otraVez.Estado);

        Assert.True((await c.DevolverAsync("alq-1", 0m)).Ok);
        var tarde = await c.DeclararAsync("alq-1");
        Assert.False(tarde.Ok);
        Assert.Equal("alquiler.not_declarable", tarde.Codigo);
    }
}

/// <summary>
/// El oráculo entero de lo que pasa días después: la devolución (<see cref="RentalDevolucionBase"/>) y la
/// cancelación, por cualquier motor.
/// </summary>
public abstract class RentalLiquidacionBase : RentalDevolucionBase
{
    /// <summary>
    /// Cancelar CANCELA las reservas; devolver no.
    /// </summary>
    /// <remarks>
    /// <b>Y el estado sale distinto</b>, que es lo que costó ver: los dos caminos dejan
    /// <c>ReservationId</c> puestos, así que derivar el estado de «¿tiene reservas?» habría
    /// contestado «devuelto» de todo alquiler cancelado — una fabricación que sale de datos reales
    /// y por eso no se ve rara. Por eso el test exige los dos valores y no sólo uno.
    /// </remarks>
    [Fact]
    public async Task Cancelar_libera_la_ventana_y_devolver_no()
    {
        var c = Nuevo(FelizEstricto());
        Assert.True((await c.ReservarAsync()).Ok);

        var cancelada = await c.CancelarAsync("alq-1", 50000m, porPersonal: true);

        Assert.True(cancelada.Ok);
        Assert.Equal("cancelled", cancelada.Estado);
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/reservations/res-1/cancel"));

        // Y el mismo alquiler DEVUELTO no cancela nada: el equipo salió y volvió.
        var otro = Nuevo(FelizEstricto());
        Assert.True((await otro.ReservarAsync()).Ok);
        var devuelta = await otro.DevolverAsync("alq-1", 0m);

        Assert.True(devuelta.Ok);
        Assert.Equal("returned", devuelta.Estado);
        Assert.Equal(0, otro.Caps.Veces("POST", "/v1/reservations/res-1/cancel"));
    }

    // ── Cancelar lo confirmado (research R7, 005-P3) ───────────────────────

    /// <summary>
    /// Cancelar un alquiler confirmado DEVUELVE lo cobrado entero, cancela las reservas y anula la garantía.
    /// </summary>
    /// <remarks>
    /// Es el defecto vivo de la línea 005: antes se soltaban las unidades y la garantía y el alquiler capturado se
    /// quedaba cobrado por algo que nadie iba a usar. Mueve plata: por eso 005-P3 se cierra además en vivo.
    /// </remarks>
    [Fact]
    public async Task Cancelar_un_confirmado_devuelve_el_alquiler_entero_y_anula_la_garantia()
    {
        var c = Nuevo(FelizEstricto());
        Assert.True((await c.ReservarAsync()).Ok);

        var cancelada = await c.CancelarAsync("alq-1", 0m, porPersonal: false);

        Assert.True(cancelada.Ok);
        Assert.Equal("cancelled", cancelada.Estado);
        var devolucion = c.Caps.Llamadas.Single(l => l.Path.EndsWith("/v1/payments/pg-ren/refund", StringComparison.Ordinal));
        Assert.Contains("350000", devolucion.Body!, StringComparison.Ordinal);
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/payments/pg-dep/void"));
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/reservations/res-1/cancel"));
    }

    /// <summary>El inquilino no cancela desde el día de inicio: ese día ya es del personal.</summary>
    [Fact]
    public async Task El_inquilino_no_cancela_desde_el_dia_de_inicio()
    {
        var c = Nuevo(FelizEstricto());
        Assert.True((await c.ReservarAsync()).Ok);

        // Semana empieza el 5 de septiembre; el reloj del motor va al 5 a las 10:00 UTC (las 05:00 en Bogotá).
        c.Avanzar(TimeSpan.FromDays(4));
        var r = await c.CancelarAsync("alq-1", 0m, porPersonal: false);

        Assert.False(r.Ok);
        Assert.Equal("alquiler.already_started", r.Codigo);
        Assert.Equal(0, c.Caps.Veces("POST", "/refund"));
        Assert.Equal(0, c.Caps.Veces("POST", "/v1/reservations/res-1/cancel"));
    }

    /// <summary>
    /// A las 23:30 de Bogotá del día anterior el inquilino TODAVÍA cancela, aunque en UTC ya sea el día de inicio.
    /// </summary>
    /// <remarks>
    /// El borde que separa contar «hoy» en el huso del despliegue de contarlo en UTC: de 19:00 a 24:00 en Bogotá,
    /// el día UTC ya es el siguiente, y quien cancela la noche anterior se quedaría sin poder.
    /// </remarks>
    [Fact]
    public async Task El_inquilino_cancela_hasta_la_noche_anterior_en_el_huso_del_despliegue()
    {
        var c = Nuevo(FelizEstricto());
        Assert.True((await c.ReservarAsync()).Ok);

        // Del 1 de septiembre 10:00 UTC al 5 a las 04:30 UTC: el 4 a las 23:30 en Bogotá.
        c.Avanzar(new TimeSpan(3, 18, 30, 0));
        var r = await c.CancelarAsync("alq-1", 0m, porPersonal: false);

        Assert.True(r.Ok, r.Codigo);
        Assert.Equal("cancelled", r.Estado);
    }

    /// <summary>
    /// El personal cancela con penalidad: captura la garantía, devuelve G − P, y el alquiler se devuelve entero.
    /// </summary>
    [Fact]
    public async Task El_personal_cancela_con_penalidad_y_el_alquiler_se_devuelve_igual()
    {
        var c = Nuevo(FelizEstricto());
        Assert.True((await c.ReservarAsync()).Ok);
        c.Avanzar(TimeSpan.FromDays(4));

        var r = await c.CancelarAsync("alq-1", 50000m, porPersonal: true);

        Assert.True(r.Ok);
        Assert.Equal(50000m, r.DanioCobrado);
        Assert.Equal(1, c.Caps.Veces("POST", "/v1/payments/pg-dep/capture"));
        Assert.Contains("350000", c.Caps.Llamadas.Single(l => l.Path.EndsWith("/v1/payments/pg-dep/refund", StringComparison.Ordinal)).Body!, StringComparison.Ordinal);
        Assert.Contains("350000", c.Caps.Llamadas.Single(l => l.Path.EndsWith("/v1/payments/pg-ren/refund", StringComparison.Ordinal)).Body!, StringComparison.Ordinal);
    }
}
