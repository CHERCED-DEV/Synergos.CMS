using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El host genérico (núcleo N1): un flujo que declara sus tipos arranca con saga, binding y fachada
/// derivados; uno mal declarado NO arranca, y el mensaje nombra el flujo y lo que falta.
/// </summary>
/// <remarks>
/// <para><b>Cada defecto parte de una definición que arranca</b> (el control) y cambia UNA cosa, y antes de
/// juzgar comprueba que el cambio entró. Los pasos son guionados: lo que se prueba aquí es el arranque y la
/// fachada, no las capacidades (eso es <c>FlujoDeMuestraTests</c>, contra las reales).</para>
/// </remarks>
public sealed class HostDeFlujosTests
{
    private const string Cita = """
        {
          "clave": "prueba.cita",
          "entrada": ["paciente", "recurso", "ventana", "monto"],
          "tipos": {
            "paciente": { "de": "sujeto" },
            "recurso": "texto",
            "ventana": "ventana",
            "monto": "dinero",
            "contacto": { "de": "contacto" }
          },
          "salida": ["cobroId", "reservaId"],
          "efimera": { "cerrar": ["contacto"] },
          "fases": {
            "abrir": ["apartar", "autorizar"],
            "cerrar": ["capturar", "confirmar", "avisar"]
          },
          "pasos": {
            "apartar": {
              "tipo": "booking.apartar", "lee": ["recurso", "ventana", "paciente"], "escribe": ["apartadoId"], "llave": "hold",
              "reserva": { "consumado_por": "confirmar", "antes": "booking.liberar", "despues": "booking.cancelar" }
            },
            "autorizar": {
              "tipo": "payments.autorizar", "lee": ["paciente", "monto"], "escribe": ["cobroId"], "llave": "authorize",
              "omitir_si_cero": "monto",
              "reserva": { "consumado_por": "capturar", "antes": "payments.anular", "despues": "payments.devolver" }
            },
            "capturar": { "tipo": "payments.capturar", "lee": ["cobroId"], "llave": "capture", "cierra_reserva": "autorizar" },
            "confirmar": { "tipo": "booking.confirmar", "lee": ["apartadoId"], "escribe": ["reservaId"], "llave": "confirm", "cierra_reserva": "apartar" },
            "avisar": {
              "tipo": "notifications.avisar", "lee": ["paciente", "contacto", "monto"], "llave": "avisar",
              "plantilla": "prueba.cita.confirmada", "al_fallar": "seguir"
            }
          }
        }
        """;

    private static readonly Ref Paciente = Ref.Create("prueba.paciente", "u-1");
    private static readonly TimeWindow Hora = TimeWindow.Of(
        new DateTimeOffset(2026, 10, 20, 15, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 20, 15, 30, 0, TimeSpan.Zero));

    private static string Mutar(string antes, string despues)
    {
        Assert.True(Cita.Contains(antes, StringComparison.Ordinal),
            $"La mutación no entra: la definición ya no dice «{antes}». Revisar este test antes que el host.");
        return Cita.Replace(antes, despues, StringComparison.Ordinal);
    }

    /// <summary>Puertos falsos: lo que la capacidad contestaría, sin red.</summary>
    private sealed class Falsos : IBookingPort, IPaymentsPort, INotificationsPort, IDeshacerAgenda, IDeshacerCobros, IDeshacerExistencias
    {
        public List<string> Llamadas { get; } = [];
        public bool RechazaAutorizar { get; set; }

        public Task<Result<string>> HallarAsync(Ref sujeto, CancellationToken ct) => Ok("rs-1", $"hallar {sujeto}");
        public Task<Result<string>> ApartarAsync(string recurso, TimeWindow ventana, Ref paraQuien, IdempotencyKey llave, CancellationToken ct)
            => Ok("bh-1", $"apartar {recurso} {llave.Value}");
        public Task<Result<string>> ConfirmarAsync(string apartado, IdempotencyKey llave, CancellationToken ct) => Ok("rv-1", $"confirmar {apartado}");
        public Task<Result<string>> AutorizarAsync(Ref paraQue, Ref pagador, Money monto, IdempotencyKey llave, CancellationToken ct)
        {
            Llamadas.Add($"autorizar {monto}");
            return Task.FromResult(RechazaAutorizar
                ? Result.Rejected<string>(Rejection.Conflict("payments.payment_declined", "guionado"))
                : Result.Ok("pg-1"));
        }
        public Task<Result<string>> CapturarAsync(string cobroId, IdempotencyKey llave, CancellationToken ct) => Ok("pg-1", $"capturar {cobroId}");
        public Task<Result<string>> AvisarAsync(Ref destinatario, string direccion, string plantilla,
            IReadOnlyDictionary<string, string> valores, IdempotencyKey llave, CancellationToken ct) => Ok("dl-1", $"avisar {direccion}");
        public Task<Result<string>> LiberarAsync(string apartado, CancellationToken ct) => Ok(apartado, $"liberar {apartado}");
        public Task<Result<string>> CancelarAsync(string reserva, CancellationToken ct) => Ok(reserva, $"cancelar {reserva}");
        public Task<Result<string>> AnularAsync(string cobro, CancellationToken ct) => Ok(cobro, $"anular {cobro}");
        public Task<Result<Money>> DevolvibleAsync(string cobro, CancellationToken ct) => Task.FromResult(Result.Ok(Money.Of(1000m, "COP")));
        public Task<Result<string>> DevolverAsync(string cobro, Money monto, string motivo, IdempotencyKey llave, CancellationToken ct)
            => Ok(cobro, $"devolver {cobro}");
        public Task<Result<string>> SoltarAsync(string apartado, CancellationToken ct) => Ok(apartado, $"soltar {apartado}");
        public Task<Result<string>> ReponerAsync(string pozo, int cantidad, IdempotencyKey llave, CancellationToken ct) => Ok(pozo, $"reponer {pozo}");

        private Task<Result<string>> Ok(string valor, string llamada)
        {
            Llamadas.Add(llamada);
            return Task.FromResult(Result.Ok(valor));
        }
    }

    private sealed class Banco : IDisposable
    {
        public Banco(string definicion = Cita)
        {
            Flujo = FlujoDef.Leer(definicion);
            var vocabulario = new SagaVocabulary("prueba", "la cita");
            var deshacer = new DeshacerComun(Puertos, Puertos, Puertos, []);
            Raiz = Path.Combine(Path.GetTempPath(), "host-flujos-" + Guid.NewGuid().ToString("N"));
            var almacen = new FileSystemSagaStore<SagaDeFlujo>(Options.Create(new SagaStorageOptions { Root = Raiz }));
            Motor = new SagaEngine<SagaDeFlujo>(
                almacen, new Compensator<SagaDeFlujo>(deshacer, TimeProvider.System, NullLogger<Compensator<SagaDeFlujo>>.Instance),
                new CompensationAlert(new SinRed(), vocabulario, Options.Create(new AlertOptions())),
                ArriendoDePrueba.Nuevo(), vocabulario, TimeProvider.System, NullLogger<SagaEngine<SagaDeFlujo>>.Instance);
            Registro = new RegistroDePasos(
            [
                new PasoAgendaApartar(Puertos), new PasoAgendaConfirmar(Puertos), new PasoPagosAutorizar(Puertos),
                new PasoPagosCapturar(Puertos), new PasoAvisar(Puertos),
            ]);
            Binding = new BindingDeFlujo(Flujo, TiposDeValor.Con(new TipoContacto()), Registro, deshacer.Tipos);
        }

        public Falsos Puertos { get; } = new();
        public string Raiz { get; }
        public FlujoDef Flujo { get; }
        public SagaEngine<SagaDeFlujo> Motor { get; }
        public RegistroDePasos Registro { get; }
        public BindingDeFlujo Binding { get; }

        public IReadOnlyList<string> Errores()
            => FlowValidator.Validar(Flujo, Registro, ContratoDelFlujo.De(Binding, Flujo.Fases.Select(f => f.Nombre).ToList()));

        public FachadaDeFlujo<SagaDeFlujo> Fachada()
        {
            var interprete = new FlowRunner<SagaDeFlujo>(
                Motor, Registro, Flujo, Binding, Flujo.Fases.Select(f => f.Nombre).ToList(), TimeProvider.System, NullLogger.Instance);
            return new FachadaDeFlujo<SagaDeFlujo>(Motor, interprete, Flujo, ReglasDeLaFachada.Para(new SagaVocabulary("prueba", "la cita")),
                s => s.Flujo == Flujo.Clave);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Raiz)) Directory.Delete(Raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }

        public static FlowContext Entrada(decimal monto = 50_000m) => new FlowContext()
            .Set("paciente", Paciente).Set("recurso", "rs-1").Set("ventana", Hora).Set("monto", Money.Of(monto, "COP"));
    }

    private sealed class SinRed : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Nadie debería avisar a una persona en estas pruebas.");
    }

    [Fact]
    public void Una_definicion_bien_declarada_arranca_y_su_binding_sale_de_ella()
    {
        using var banco = new Banco();

        Assert.Empty(banco.Errores());
        Assert.Equal(new[] { "cobroId", "apartadoId", "paciente", "monto" }, banco.Binding.Reconstruye);
        Assert.Equal(FormaDeReserva.Unica, banco.Binding.Reservas["apartar"]);
        Assert.Equal("paciente", banco.Binding.Sujeto);
    }

    public static TheoryData<string, string, string> Defectos() => new()
    {
        // una entrada sin tipo
        { "\"monto\": \"dinero\",", "", "«monto» entra y no declara su tipo" },
        // un tipo que nadie registró
        { "\"monto\": \"dinero\"", "\"monto\": \"moneda\"", "«moneda», y ese tipo de valor no está registrado" },
        // sin sujeto
        { "\"paciente\": { \"de\": \"sujeto\" }", "\"paciente\": \"ref\"", "exactamente un sujeto" },
        // el contacto en la entrada: un dato personal acabaría en la saga
        { "\"entrada\": [\"paciente\", \"recurso\", \"ventana\", \"monto\"]", "\"entrada\": [\"paciente\", \"recurso\", \"ventana\", \"monto\", \"contacto\"]", "no es sólo efímero" },
        // una compensación que nadie sabe deshacer
        { "\"antes\": \"booking.liberar\"", "\"antes\": \"booking.soltar\"", "«booking.soltar», y nadie sabe deshacer ese tipo" },
        // una fase que choca con una ruta del host
        { "\"cerrar\": [\"capturar\"", "\"cancelar\": [\"capturar\"", "se llama como una operación del host" },
        // una salida que nadie escribe
        { "\"salida\": [\"cobroId\", \"reservaId\"]", "\"salida\": [\"cobroId\", \"recibo\"]", "«recibo», que ningún paso escribe" },
    };

    [Theory]
    [MemberData(nameof(Defectos))]
    public void Una_definicion_mal_declarada_no_arranca(string antes, string despues, string mensaje)
    {
        using var banco = new Banco(Mutar(antes, despues));

        var errores = banco.Errores();

        Assert.Contains(errores, e => e.Contains(mensaje, StringComparison.Ordinal) && e.Contains("«prueba.cita»", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => banco.Fachada());
    }

    [Fact]
    public async Task Abre_cierra_y_guarda_lo_que_la_fase_que_cierra_necesita()
    {
        using var banco = new Banco();
        var fachada = banco.Fachada();

        var abierta = await fachada.AbrirAsync("cita-1", Paciente, Banco.Entrada(), CancellationToken.None);
        Assert.True(abierta.IsOk, abierta.Rejection?.ToString());
        Assert.Equal("pg-1", abierta.Value.Datos["cobroId"].Valor!.Value.GetString());
        Assert.Equal("bh-1", abierta.Value.Datos["apartadoId"].Valor!.Value.GetString());

        var cerrada = await fachada.ContinuarAsync("cerrar", "cita-1", Paciente,
            new FlowContext().Set("contacto", new Contacto("ana@ejemplo.co", "Ana", null, null)), CancellationToken.None);

        Assert.True(cerrada.IsOk, cerrada.Rejection?.ToString());
        Assert.Equal(SagaStatus.Completed, cerrada.Value.Status);
        Assert.Equal("rv-1", cerrada.Value.Datos["reservaId"].Valor!.Value.GetString());
        Assert.DoesNotContain(cerrada.Value.Datos.Keys, k => k == "contacto");
        Assert.Contains("avisar ana@ejemplo.co", banco.Puertos.Llamadas);
    }

    [Fact]
    public async Task Un_cobro_rechazado_suelta_la_hora_y_devuelve_el_rechazo_original()
    {
        using var banco = new Banco();
        banco.Puertos.RechazaAutorizar = true;

        var r = await banco.Fachada().AbrirAsync("cita-1", Paciente, Banco.Entrada(), CancellationToken.None);

        Assert.Equal("payments.payment_declined", r.Rejection?.Code);
        Assert.Contains("liberar bh-1", banco.Puertos.Llamadas);
        Assert.Equal(SagaStatus.Compensated, banco.Motor.Find("cita-1")!.Status);
    }

    [Fact]
    public async Task Una_llave_cuya_saga_se_deshizo_abre_otra_y_una_viva_devuelve_esa()
    {
        // #41 y #166 por la fachada común: con la saga deshecha, la misma llave deja reintentar. El espejo
        // va porque «no devuelve la deshecha» lo pasaría también quien perdió la idempotencia entera.
        using var banco = new Banco();
        var fachada = banco.Fachada();
        banco.Puertos.RechazaAutorizar = true;
        Assert.False((await fachada.AbrirAsync("cita-1", Paciente, Banco.Entrada(), CancellationToken.None)).IsOk);
        Assert.False(fachada.DevolveriaLaMisma("cita-1", Paciente));

        banco.Puertos.RechazaAutorizar = false;
        var reintento = await fachada.AbrirAsync("cita-1", Paciente, Banco.Entrada(), CancellationToken.None);

        Assert.True(reintento.IsOk, reintento.Rejection?.ToString());
        Assert.True(reintento.Value.Status == SagaStatus.Running,
            $"{reintento.Value.Id} {reintento.Value.Status} {reintento.Value.LastError} | {string.Join(" · ", banco.Puertos.Llamadas)}");
        Assert.True(fachada.DevolveriaLaMisma("cita-1", Paciente));
        var otraVez = await fachada.AbrirAsync("cita-1", Paciente, Banco.Entrada(), CancellationToken.None);
        Assert.Equal(reintento.Value.Id, otraVez.Value.Id);
    }

    [Fact]
    public async Task Gratis_no_cobra_y_la_saga_completa_igual()
    {
        using var banco = new Banco();
        var fachada = banco.Fachada();

        Assert.True((await fachada.AbrirAsync("cita-1", Paciente, Banco.Entrada(0m), CancellationToken.None)).IsOk);
        var cerrada = await fachada.ContinuarAsync("cerrar", "cita-1", Paciente, new FlowContext().Set("contacto", null), CancellationToken.None);

        Assert.Equal(SagaStatus.Completed, cerrada.Value.Status);
        Assert.DoesNotContain(banco.Puertos.Llamadas, l => l.StartsWith("autorizar", StringComparison.Ordinal) || l.StartsWith("capturar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task La_saga_de_otro_no_existe_y_el_doble_clic_es_idempotente()
    {
        using var banco = new Banco();
        var fachada = banco.Fachada();
        Assert.True((await fachada.AbrirAsync("cita-1", Paciente, Banco.Entrada(), CancellationToken.None)).IsOk);

        var otro = Ref.Create("prueba.paciente", "u-2");
        Assert.Equal("prueba.saga_not_found", fachada.Consultar("cita-1", otro).Rejection?.Code);
        Assert.Equal("prueba.saga_not_found",
            (await fachada.ContinuarAsync("cerrar", "cita-1", otro, new FlowContext().Set("contacto", null), CancellationToken.None)).Rejection?.Code);

        Assert.True((await fachada.ContinuarAsync("cerrar", "cita-1", Paciente, new FlowContext().Set("contacto", null), CancellationToken.None)).IsOk);
        var otraVez = await fachada.ContinuarAsync("cerrar", "cita-1", Paciente, new FlowContext().Set("contacto", null), CancellationToken.None);
        Assert.True(otraVez.IsOk);
        Assert.Single(banco.Puertos.Llamadas, l => l.StartsWith("capturar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Una_llave_de_otro_flujo_no_devuelve_su_saga()
    {
        using var banco = new Banco();
        Assert.True((await banco.Fachada().AbrirAsync("llave-1", Paciente, Banco.Entrada(), CancellationToken.None)).IsOk);

        using var otroFlujo = new Banco(Cita.Replace("\"clave\": \"prueba.cita\"", "\"clave\": \"prueba.otra\"", StringComparison.Ordinal));
        var fachada = new FachadaDeFlujo<SagaDeFlujo>(banco.Motor,
            new FlowRunner<SagaDeFlujo>(banco.Motor, otroFlujo.Registro, otroFlujo.Flujo, otroFlujo.Binding, ["abrir", "cerrar"],
                TimeProvider.System, NullLogger.Instance),
            otroFlujo.Flujo, ReglasDeLaFachada.Para(new SagaVocabulary("prueba", "la otra")), s => s.Flujo == "prueba.otra");

        var r = await fachada.AbrirAsync("llave-1", Paciente, Banco.Entrada(), CancellationToken.None);

        Assert.Equal(FachadaDeFlujo<SagaDeFlujo>.LlaveDeOtroFlujo, r.Rejection?.Code);
        Assert.Equal("prueba.saga_not_found", fachada.Consultar("llave-1", Paciente).Rejection?.Code);
    }
}
