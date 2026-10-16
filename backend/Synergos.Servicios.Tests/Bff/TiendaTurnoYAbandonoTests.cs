using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El turno, el abandono y la guardia de la compra de la tienda por la puerta (004-tienda-compra, T023): lo que
/// el host genérico promete a cualquier flujo, medido sobre <c>tienda.compra</c> contra el <c>Program</c> real de
/// <c>Bff.Tienda</c> y las capacidades reales.
/// </summary>
/// <remarks>
/// <para><b>Las dos operaciones están en vuelo A LA VEZ, sin esperas</b>, como en <c>UnaSagaUnTurnoTests</c>:
/// una compuerta detiene UNA llamada del orquestador —el aviso al final de «cerrar», o la anulación del cobro a
/// media cancelación— y la otra operación se manda mientras la primera está parada ahí.</para>
///
/// <para><b>Lo primero que se afirma es el invariante</b>: una compra <c>Completed</c> tiene el cobro capturado
/// sin devolver, las existencias consumidas y el pedido cumplido; una <c>Compensated</c> no cobró nada, devolvió
/// las existencias y anuló el pedido. Después, quién ganó.</para>
/// </remarks>
public sealed class TiendaTurnoYAbandonoTests
{
    private const string Anular = "POST /v1/payments/*/void";

    private static readonly TimeSpan Plazo = TimeSpan.FromSeconds(30);

    /// <summary>A quién avisa la tienda cuando una compensación se rinde: una guardia de prueba.</summary>
    private static readonly Dictionary<string, string> Guardia = new()
    {
        ["Tienda:Alerts:ToKind"] = "guardia",
        ["Tienda:Alerts:ToId"] = "tienda",
        ["Tienda:Alerts:Address"] = "guardia@ejemplo.co",
    };

    /// <summary>Detiene la primera llamada a una capacidad que cumpla la condición, hasta que el test la suelta.</summary>
    private sealed class Compuerta(IHttpClientFactory interno, string capacidad, Func<HttpRequestMessage, bool> cual)
        : IHttpClientFactory
    {
        private int _usada;

        public TaskCompletionSource Entro { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Suelta { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HttpClient CreateClient(string name)
        {
            var dentro = interno.CreateClient(name);
            return new HttpClient(new Paso(this, dentro, name)) { BaseAddress = dentro.BaseAddress, Timeout = Plazo * 2 };
        }

        private bool Detiene(string nombre, HttpRequestMessage peticion)
            => nombre == capacidad && cual(peticion) && Interlocked.Exchange(ref _usada, 1) == 0;

        private sealed class Paso(Compuerta dueno, HttpClient dentro, string nombre) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
            {
                if (dueno.Detiene(nombre, peticion))
                {
                    dueno.Entro.TrySetResult();
                    await dueno.Suelta.Task.WaitAsync(Plazo, ct);
                }

                var copia = new HttpRequestMessage(peticion.Method, peticion.RequestUri) { Content = peticion.Content };
                foreach (var h in peticion.Headers) copia.Headers.TryAddWithoutValidation(h.Key, h.Value);
                return await dentro.SendAsync(copia, ct);
            }
        }
    }

    /// <summary>La compuerta en el aviso al comprador: «cerrar» ya capturó, consumió, despachó y cumplió.</summary>
    private static Func<IHttpClientFactory, IHttpClientFactory> EnElAviso(Action<Compuerta> guardar)
        => f =>
        {
            var c = new Compuerta(f, "notifications",
                p => p.Method == HttpMethod.Post && p.RequestUri!.AbsolutePath.EndsWith("/v1/deliveries", StringComparison.Ordinal));
            guardar(c);
            return c;
        };

    /// <summary>La compuerta en la anulación del cobro: «cancelar» va a mitad de deshacer.</summary>
    private static Func<IHttpClientFactory, IHttpClientFactory> EnLaAnulacion(Action<Compuerta> guardar)
        => f =>
        {
            var c = new Compuerta(f, "payments",
                p => p.Method == HttpMethod.Post && p.RequestUri!.AbsolutePath.EndsWith("/void", StringComparison.Ordinal));
            guardar(c);
            return c;
        };

    private static async Task<string> Abierta(CompraDeTiendaReal t, string llave)
    {
        var (estado, abierta) = await t.Abrir(llave: llave);
        Assert.True(estado == HttpStatusCode.Created, abierta.ToString());
        return abierta.GetProperty("id").GetString()!;
    }

    /// <summary>
    /// El invariante. Completed ⇒ capturado sin devolver, existencias consumidas (8, 9 y 6 de 10) sin apartados
    /// y pedido cumplido; Compensated ⇒ nada cobrado, las diez de cada uno de vuelta y pedido anulado. Cualquier
    /// otro final es incoherente. Devuelve el estado.
    /// </summary>
    private static async Task<string> Coherente(CompraDeTiendaReal t, string id, string cobro, string pedido)
    {
        var (_, saga) = await t.Consultar(id);
        var estado = saga.GetProperty("estado").GetString()!;
        var pago = await t.Leer("payments", $"v1/payments/{cobro}");
        var status = pago.GetProperty("status").GetString();
        var devuelto = pago.GetProperty("refunded").GetProperty("amount").GetDecimal();
        var monto = pago.GetProperty("amount").GetProperty("amount").GetDecimal();
        var orden = (await t.Leer("orders", $"v1/orders/{pedido}")).GetProperty("status").GetString();
        var existentes = new[] { await t.Existentes("p-1"), await t.Existentes("p-2:azul"), await t.Existentes("p-3") };
        var disponibles = new[] { await t.Disponibles("p-1"), await t.Disponibles("p-2:azul"), await t.Disponibles("p-3") };
        var foto = $"saga {estado}, cobro {status} devuelto {devuelto} de {monto}, pedido {orden}, "
                   + $"existencias {string.Join('/', existentes)} con {string.Join('/', disponibles)} disponibles";

        switch (estado)
        {
            case "Completed":
                Assert.True(status == "Captured" && devuelto == 0m && orden == "Fulfilled"
                            && existentes.SequenceEqual([8, 9, 6]) && disponibles.SequenceEqual(existentes), foto);
                break;
            case "Compensated":
                Assert.True((status == "Voided" || (status == "Captured" && devuelto == monto)) && orden == "Cancelled"
                            && existentes.SequenceEqual([10, 10, 10]) && disponibles.SequenceEqual(existentes), foto);
                break;
            default:
                Assert.Fail($"La compra no terminó ni completada ni deshecha: {foto}");
                break;
        }
        return estado;
    }

    private static void EsTransitorio(HttpStatusCode estado, JsonElement cuerpo, string codigo)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, estado);
        Assert.Equal(codigo, cuerpo.GetProperty("code").GetString());
        Assert.True(cuerpo.GetProperty("transient").GetBoolean());
    }

    private static CompensationSweeper<SagaDeFlujo> Barrido(CompraDeTiendaReal t, TimeProvider reloj)
        => new(
            t.Servicios.GetRequiredService<IServiceScopeFactory>(),
            t.Servicios.GetRequiredService<IOptionsMonitor<SweepOptions>>(),
            reloj,
            NullLogger<CompensationSweeper<SagaDeFlujo>>.Instance);

    // ── El turno ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cancelar_mientras_cerrar_avisa_no_devuelve_lo_cobrado_y_la_compra_queda_completada()
    {
        Compuerta? compuerta = null;
        using var t = new CompraDeTiendaReal(envolver: EnElAviso(c => compuerta = c));
        var id = await Abierta(t, "cerrar-cancelar");
        var (cobro, pedido) = (t.Dato(id, "cobroId"), t.Dato(id, "pedido"));

        var cerrar = t.Cerrar(id);
        await compuerta!.Entro.Task.WaitAsync(Plazo);   // «cerrar» ya capturó, consumió, despachó y cumplió: está avisando
        var (estadoCancelar, cancelar) = await t.Cancelar(id);
        compuerta.Suelta.SetResult();
        var (estadoCerrar, cerrada) = await cerrar;

        Assert.Equal("Completed", await Coherente(t, id, cobro, pedido));
        Assert.True(estadoCerrar == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        EsTransitorio(estadoCancelar, cancelar, "tienda.compensation_in_flight");
        Assert.DoesNotContain(t.Llamadas, l => l.EndsWith("/void", StringComparison.Ordinal) || l.EndsWith("/refunds", StringComparison.Ordinal));

        // Y vuelta la calma, cancelar ya no puede deshacer una compra hecha.
        var (despues, _) = await t.Cancelar(id);
        Assert.Equal(HttpStatusCode.Conflict, despues);
        Assert.Equal("Completed", await Coherente(t, id, cobro, pedido));
    }

    [Fact]
    public async Task Un_segundo_cerrar_mientras_el_primero_avisa_no_captura_ni_avisa_dos_veces()
    {
        Compuerta? compuerta = null;
        using var t = new CompraDeTiendaReal(envolver: EnElAviso(c => compuerta = c));
        var id = await Abierta(t, "doble-cerrar");
        var (cobro, pedido) = (t.Dato(id, "cobroId"), t.Dato(id, "pedido"));

        var primero = t.Cerrar(id);
        await compuerta!.Entro.Task.WaitAsync(Plazo);
        var (estadoSegundo, segundo) = await t.Cerrar(id);
        compuerta.Suelta.SetResult();
        var (estadoPrimero, _) = await primero;

        Assert.Equal("Completed", await Coherente(t, id, cobro, pedido));
        Assert.Equal(HttpStatusCode.OK, estadoPrimero);
        EsTransitorio(estadoSegundo, segundo, FlowRunner<SagaDeFlujo>.Ocupada);
        Assert.Equal(1, t.Llamadas.Count(l => l.EndsWith("/capture", StringComparison.Ordinal)));
        Assert.Equal(1, t.Veces("POST /v1/deliveries"));
    }

    [Fact]
    public async Task Cerrar_mientras_cancelar_deshace_no_cobra_y_la_compra_queda_deshecha()
    {
        Compuerta? compuerta = null;
        using var t = new CompraDeTiendaReal(envolver: EnLaAnulacion(c => compuerta = c));
        var id = await Abierta(t, "cancelar-cerrar");
        var (cobro, pedido) = (t.Dato(id, "cobroId"), t.Dato(id, "pedido"));

        var cancelar = t.Cancelar(id);
        await compuerta!.Entro.Task.WaitAsync(Plazo);   // «cancelar» va a anular el cobro
        var (estadoCerrar, cerrar) = await t.Cerrar(id);
        compuerta.Suelta.SetResult();
        var (estadoCancelar, _) = await cancelar;

        Assert.Equal("Compensated", await Coherente(t, id, cobro, pedido));
        Assert.Equal(HttpStatusCode.OK, estadoCancelar);
        EsTransitorio(estadoCerrar, cerrar, FlowRunner<SagaDeFlujo>.Ocupada);
        Assert.DoesNotContain(t.Llamadas, l => l.EndsWith("/capture", StringComparison.Ordinal) || l.EndsWith("/consume", StringComparison.Ordinal));
    }

    // ── El abandono ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Una_compra_abierta_y_abandonada_la_deshace_el_barrido_y_una_reciente_no()
    {
        using var t = new CompraDeTiendaReal();
        var id = await Abierta(t, "abandonada");
        var (cobro, pedido) = (t.Dato(id, "cobroId"), t.Dato(id, "pedido"));

        // El control: recién abierta, el barrido no la toca — lo único que la vuelve trabajo es el tiempo.
        await Barrido(t, TimeProvider.System).UnaVueltaAsync(t.Sagas, CancellationToken.None);
        Assert.Equal(SagaStatus.Running, t.Sagas.Find(id)!.Status);
        Assert.Equal(8, await t.Disponibles("p-1"));

        await Barrido(t, new DentroDeDosHoras()).UnaVueltaAsync(t.Sagas, CancellationToken.None);

        Assert.Equal("Compensated", await Coherente(t, id, cobro, pedido));
        Assert.Equal("Voided", (await t.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());
        Assert.All(t.Sagas.Find(id)!.Compensations, c => Assert.StartsWith("abandonada", c.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public async Task El_barrido_de_abandono_no_deshace_una_compra_que_se_esta_cerrando()
    {
        Compuerta? compuerta = null;
        using var t = new CompraDeTiendaReal(envolver: EnElAviso(c => compuerta = c));
        var id = await Abierta(t, "cerrar-barrido");
        var (cobro, pedido) = (t.Dato(id, "cobroId"), t.Dato(id, "pedido"));

        var cerrar = t.Cerrar(id);
        await compuerta!.Entro.Task.WaitAsync(Plazo);
        await Barrido(t, new DentroDeDosHoras()).UnaVueltaAsync(t.Sagas, CancellationToken.None);   // para él, lleva dos horas abierta
        compuerta.Suelta.SetResult();
        var (estadoCerrar, _) = await cerrar;

        Assert.Equal("Completed", await Coherente(t, id, cobro, pedido));
        Assert.Equal(HttpStatusCode.OK, estadoCerrar);
    }

    // ── La guardia ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Una_anulacion_que_se_rinde_avisa_UNA_vez_a_la_guardia_y_el_reintento_manual_la_termina()
    {
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        using var t = new CompraDeTiendaReal(cae: [Anular], reloj: reloj, ajustes: Guardia);
        var id = await Abierta(t, "se-rinde");
        var (cobro, pedido) = (t.Dato(id, "cobroId"), t.Dato(id, "pedido"));
        var anulaciones = $"POST /v1/payments/{cobro}/void";

        // Cancelar suelta las existencias y anula el pedido; la anulación del cobro choca con Payments caída.
        var (estado, cancelada) = await t.Cancelar(id);
        Assert.True(estado == HttpStatusCode.OK, cancelada.ToString());
        Assert.Equal("Compensating", cancelada.GetProperty("estado").GetString());
        Assert.Equal(10, await t.Disponibles("p-1"));
        Assert.Equal("Cancelled", (await t.Leer("orders", $"v1/orders/{pedido}")).GetProperty("status").GetString());

        // El barrido insiste con su retroceso —a lo sumo una hora— hasta rendirse.
        var barrido = Barrido(t, reloj);
        for (var vuelta = 0; vuelta < 3 * CompensationLimits.MaxAttempts && t.Sagas.Find(id)!.Status != SagaStatus.CompensationFailed; vuelta++)
        {
            reloj.Avanzar(TimeSpan.FromMinutes(61));
            await barrido.UnaVueltaAsync(t.Sagas, CancellationToken.None);
        }

        var rendida = t.Sagas.Find(id)!;
        Assert.Equal(SagaStatus.CompensationFailed, rendida.Status);
        Assert.Equal(CompensationLimits.MaxAttempts, rendida.Compensations.Single(c => c.IsStuck).Attempts);
        Assert.Equal(CompensationLimits.MaxAttempts, t.Veces(anulaciones));
        Assert.Equal(1, await AvisosALaGuardia(t));

        var colgada = Assert.Single(await Compensaciones(t), c => c.GetProperty("sagaId").GetString() == id);
        Assert.Equal("tienda.compra", colgada.GetProperty("flujo").GetString());
        Assert.True(colgada.GetProperty("stuck").GetBoolean());
        Assert.Equal(JsonValueKind.String, colgada.GetProperty("alertedAtUtc").ValueKind);

        // Rendida y avisada, el barrido ya no la toca: ni otro intento ni otro correo.
        reloj.Avanzar(TimeSpan.FromDays(1));
        await barrido.UnaVueltaAsync(t.Sagas, CancellationToken.None);
        Assert.Equal(CompensationLimits.MaxAttempts, t.Veces(anulaciones));
        Assert.Equal(1, await AvisosALaGuardia(t));

        // La persona arregla la causa y pide el reintento: la anulación sale y la compra termina deshecha.
        t.Encender(Anular);
        using var reintento = await t.Orquestador.PostAsync($"v1/flujos/tienda.compra/{id}/reintentar", null);
        var cuerpo = await reintento.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(reintento.StatusCode == HttpStatusCode.OK, cuerpo.ToString());
        Assert.Equal("Compensated", cuerpo.GetProperty("estado").GetString());
        Assert.Equal("Compensated", await Coherente(t, id, cobro, pedido));
        Assert.DoesNotContain(await Compensaciones(t), c => c.GetProperty("sagaId").GetString() == id);
    }

    private static async Task<int> AvisosALaGuardia(CompraDeTiendaReal t)
        => (await t.Leer("notifications", "v1/deliveries?toKind=guardia&toId=tienda")).GetProperty("items").GetArrayLength();

    private static async Task<List<JsonElement>> Compensaciones(CompraDeTiendaReal t)
        => (await t.Orquestador.GetFromJsonAsync<JsonElement>("v1/flujos/compensaciones"))
            .GetProperty("items").EnumerateArray().ToList();

    private sealed class DentroDeDosHoras : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddHours(2);
    }

    /// <summary>El reloj del orquestador, que el test adelanta: el retroceso de cada intento lo decide él.</summary>
    private sealed class RelojDePrueba(DateTimeOffset inicio) : TimeProvider
    {
        private long _ticks = inicio.UtcTicks;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Avanzar(TimeSpan cuanto) => Interlocked.Add(ref _ticks, cuanto.Ticks);
    }
}
