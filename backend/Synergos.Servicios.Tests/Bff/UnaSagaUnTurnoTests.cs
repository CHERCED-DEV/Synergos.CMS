using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Eventos.Domain;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Una saga, un turno (ADR 0140 F3): «cerrar» no se intercala con «cancelar», con otro «cerrar» ni con
/// el barrido, contra el <c>Program</c> real de <c>Bff.Eventos</c> y las capacidades reales.
/// </summary>
/// <remarks>
/// <para><b>Las dos peticiones están en vuelo A LA VEZ, sin esperas.</b> Una compuerta detiene UNA
/// llamada del orquestador a una capacidad —el aviso, después de capturar y consumir; o la anulación
/// del cobro, a media cancelación— y la segunda operación se manda mientras la primera está parada ahí.
/// Es la ventana que midió la verificación de la F3, sin depender de cuánto tarda nadie.</para>
///
/// <para><b>Lo que se afirma primero es el invariante</b>, porque es lo que no puede romperse nunca:
/// una compra <c>Completed</c> tiene el cobro capturado sin devolver y el aforo consumido; una
/// <c>Compensated</c> no cobró nada y repuso el aforo. Después, quién ganó: la que tenía el turno
/// termina, y la otra recibe un transitorio sin haber tocado nada.</para>
/// </remarks>
public sealed class UnaSagaUnTurnoTests
{
    private static readonly string Ana = CompraDeEventosReal.Sujeto("m1");
    private static readonly string AvisoDeAna = CompraDeEventosReal.Contacto("ana@ejemplo.co");
    private static readonly TimeSpan Plazo = TimeSpan.FromSeconds(30);

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

    private static Func<IHttpClientFactory, IHttpClientFactory> EnElAviso(Action<Compuerta> guardar)
        => f =>
        {
            var c = new Compuerta(f, "notifications",
                p => p.Method == HttpMethod.Post && p.RequestUri!.AbsolutePath.EndsWith("/v1/deliveries", StringComparison.Ordinal));
            guardar(c);
            return c;
        };

    private static Func<IHttpClientFactory, IHttpClientFactory> EnLaAnulacion(Action<Compuerta> guardar)
        => f =>
        {
            var c = new Compuerta(f, "payments",
                p => p.Method == HttpMethod.Post && p.RequestUri!.AbsolutePath.EndsWith("/void", StringComparison.Ordinal));
            guardar(c);
            return c;
        };

    private static async Task<string> Abierta(CompraDeEventosReal compra, string llave)
    {
        var (estado, cuerpo) = await compra.Abrir(Ana, CompraDeEventosReal.Negocio(0m), llave);
        Assert.Equal(HttpStatusCode.Created, estado);
        return cuerpo.GetProperty("id").GetString()!;
    }

    /// <summary>
    /// El invariante: Completed ⇒ capturado sin devolver y aforo consumido; Compensated ⇒ nada cobrado y
    /// aforo repuesto. Cualquier otro estado final es incoherente. Devuelve el estado.
    /// </summary>
    private static async Task<string> Coherente(CompraDeEventosReal compra, string id)
    {
        var (_, saga) = await compra.Sobre(id, null, Ana);
        var estado = saga.GetProperty("status").GetString()!;
        var pago = (await compra.Lista("payments", $"v1/payments?forKind=eventos.compra&forId={Uri.EscapeDataString(id)}")).Single();
        var cobro = pago.GetProperty("status").GetString();
        var devuelto = pago.GetProperty("refunded").GetProperty("amount").GetDecimal();
        var monto = pago.GetProperty("amount").GetProperty("amount").GetDecimal();

        using var inventario = compra.Capacidades.CreateClient("inventory");
        var pozo = JsonDocument.Parse(await inventario.GetStringAsync("v1/items?subjectKind=eventos.aforo&subjectId=evt-1/GEN")).RootElement;
        var existencias = pozo.GetProperty("onHand").GetInt32();
        var apartadas = pozo.GetProperty("held").GetInt32();
        var foto = $"saga {estado}, cobro {cobro} devuelto {devuelto} de {monto}, aforo {existencias} con {apartadas} apartadas";

        switch (estado)
        {
            case "Completed":
                Assert.True(cobro == "Captured" && devuelto == 0m && existencias == 8 && apartadas == 0, foto);
                break;
            case "Compensated":
                Assert.True((cobro == "Voided" || (cobro == "Captured" && devuelto == monto)) && existencias == 10 && apartadas == 0, foto);
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

    [Fact]
    public async Task Cancelar_mientras_cerrar_avisa_no_devuelve_lo_cobrado_y_la_compra_queda_completada()
    {
        Compuerta? compuerta = null;
        using var compra = new CompraDeEventosReal(envolver: EnElAviso(c => compuerta = c));
        var id = await Abierta(compra, "cerrar-cancelar");

        var cerrar = compra.Sobre(id, "confirm", Ana, AvisoDeAna);
        await compuerta!.Entro.Task.WaitAsync(Plazo);   // «cerrar» ya capturó y consumió: está avisando
        var (estadoCancelar, cancelar) = await compra.Sobre(id, "cancel", Ana);
        compuerta.Suelta.SetResult();
        var (estadoCerrar, cerrada) = await cerrar;

        Assert.Equal("Completed", await Coherente(compra, id));
        Assert.Equal(HttpStatusCode.OK, estadoCerrar);
        Assert.Equal("Completed", cerrada.GetProperty("status").GetString());
        EsTransitorio(estadoCancelar, cancelar, "eventos.compensation_in_flight");

        // Y vuelta la calma, cancelar ya no puede deshacer una compra hecha.
        var (despues, _) = await compra.Sobre(id, "cancel", Ana);
        Assert.Equal(HttpStatusCode.Conflict, despues);
        Assert.Equal("Completed", await Coherente(compra, id));
    }

    [Fact]
    public async Task Un_segundo_cerrar_mientras_el_primero_avisa_no_deshace_la_compra()
    {
        Compuerta? compuerta = null;
        using var compra = new CompraDeEventosReal(envolver: EnElAviso(c => compuerta = c));
        var id = await Abierta(compra, "doble-cerrar");

        var primero = compra.Sobre(id, "confirm", Ana, AvisoDeAna);
        await compuerta!.Entro.Task.WaitAsync(Plazo);
        var (estadoSegundo, segundo) = await compra.Sobre(id, "confirm", Ana, AvisoDeAna);
        compuerta.Suelta.SetResult();
        var (estadoPrimero, _) = await primero;

        Assert.Equal("Completed", await Coherente(compra, id));
        Assert.Equal(HttpStatusCode.OK, estadoPrimero);
        EsTransitorio(estadoSegundo, segundo, FlowRunner<TicketingSaga>.Ocupada);

        // El reintento del doble clic contesta lo mismo que la primera: idempotente.
        var (reintento, otra) = await compra.Sobre(id, "confirm", Ana, AvisoDeAna);
        Assert.Equal(HttpStatusCode.OK, reintento);
        Assert.Equal("Completed", otra.GetProperty("status").GetString());
    }

    [Fact]
    public async Task El_barrido_de_abandono_no_deshace_una_compra_que_se_esta_cerrando()
    {
        Compuerta? compuerta = null;
        using var compra = new CompraDeEventosReal(envolver: EnElAviso(c => compuerta = c));
        var id = await Abierta(compra, "cerrar-barrido");
        var motor = compra.Servicios.GetRequiredService<SagaEngine<TicketingSaga>>();
        var barrido = new CompensationSweeper<TicketingSaga>(
            compra.Servicios.GetRequiredService<IServiceScopeFactory>(),
            compra.Servicios.GetRequiredService<IOptionsMonitor<SweepOptions>>(),
            new DentroDeDosHoras(),
            NullLogger<CompensationSweeper<TicketingSaga>>.Instance);

        var cerrar = compra.Sobre(id, "confirm", Ana, AvisoDeAna);
        await compuerta!.Entro.Task.WaitAsync(Plazo);
        await barrido.UnaVueltaAsync(motor, CancellationToken.None);   // para él, la compra lleva dos horas abierta
        compuerta.Suelta.SetResult();
        var (estadoCerrar, _) = await cerrar;

        Assert.Equal("Completed", await Coherente(compra, id));
        Assert.Equal(HttpStatusCode.OK, estadoCerrar);
    }

    [Fact]
    public async Task Cerrar_mientras_cancelar_deshace_no_cobra_y_la_compra_queda_deshecha()
    {
        Compuerta? compuerta = null;
        using var compra = new CompraDeEventosReal(envolver: EnLaAnulacion(c => compuerta = c));
        var id = await Abierta(compra, "cancelar-cerrar");

        var cancelar = compra.Sobre(id, "cancel", Ana);
        await compuerta!.Entro.Task.WaitAsync(Plazo);   // «cancelar» soltó el aforo y va a anular el cobro
        var (estadoCerrar, cerrar) = await compra.Sobre(id, "confirm", Ana, AvisoDeAna);
        compuerta.Suelta.SetResult();
        var (estadoCancelar, _) = await cancelar;

        Assert.Equal("Compensated", await Coherente(compra, id));
        Assert.Equal(HttpStatusCode.OK, estadoCancelar);
        EsTransitorio(estadoCerrar, cerrar, FlowRunner<TicketingSaga>.Ocupada);
    }

    private sealed class DentroDeDosHoras : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddHours(2);
    }
}
