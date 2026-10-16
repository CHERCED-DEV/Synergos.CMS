using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// La forma de la renta de Alquiler (NN-2 de 005, núcleo N1): dos <c>payments.autorizar</c> con llaves fijas
    /// distintas cuyas reservas cierran pasos distintos, la garantía con <c>omitir_si_cero</c>, un <c>entero</c>,
    /// un paso de dominio y dos cotizaciones antes del primero que reserva, y una salida de cuatro valores.
    /// </summary>
    private const string Renta = """
        {
          "clave": "prueba.renta",
          "entrada": ["inquilino", "equipo", "ventana", "unidades", "garantia"],
          "tipos": {
            "inquilino": { "de": "sujeto" },
            "equipo": { "tipo": "ref", "kind": "prueba.equipo" },
            "ventana": "ventana",
            "unidades": "entero",
            "garantia": "dinero"
          },
          "salida": ["total", "garantia", "garantiaId", "cobroId"],
          "fases": {
            "abrir": ["lineas", "cotizar", "desglose", "hallar", "apartar", "deposito", "alquiler"],
            "cerrar": ["capturar", "conservar", "confirmar"]
          },
          "pasos": {
            "lineas": { "tipo": "prueba.lineas", "lee": ["equipo", "unidades"], "escribe": ["aCotizar"] },
            "cotizar": { "tipo": "pricing.cotizar-sin-codigo", "lee": ["aCotizar"], "escribe": ["cotizacion"] },
            "desglose": { "tipo": "pricing.desglose", "lee": ["cotizacion"], "escribe": ["subtotal", "descuento", "impuesto", "total"] },
            "hallar": { "tipo": "booking.hallar", "lee": ["equipo"], "escribe": ["recursoId"] },
            "apartar": {
              "tipo": "booking.apartar", "lee": ["recursoId", "ventana", "inquilino"], "escribe": ["apartadoId"], "llave": "hold",
              "reserva": { "consumado_por": "confirmar", "antes": "booking.liberar", "despues": "booking.cancelar" }
            },
            "deposito": {
              "tipo": "payments.autorizar", "lee": ["inquilino", "garantia"], "escribe": ["garantiaId"], "llave": "deposit",
              "omitir_si_cero": "garantia",
              "reserva": { "consumado_por": "conservar", "antes": "payments.anular" }
            },
            "alquiler": {
              "tipo": "payments.autorizar", "lee": ["inquilino", "total"], "escribe": ["cobroId"], "llave": "rental",
              "omitir_si_cero": "total",
              "reserva": { "consumado_por": "capturar", "antes": "payments.anular", "despues": "payments.devolver" }
            },
            "capturar": { "tipo": "payments.capturar", "lee": ["cobroId"], "llave": "capture", "cierra_reserva": "alquiler" },
            "conservar": { "tipo": "prueba.conservar", "lee": ["garantiaId"], "cierra_reserva": "deposito" },
            "confirmar": { "tipo": "booking.confirmar", "lee": ["apartadoId"], "escribe": ["reservaId"], "llave": "confirm", "cierra_reserva": "apartar" }
          }
        }
        """;

    /// <summary>
    /// La forma de la oferta de 007 (NE-10, núcleo N3): UNA fase, sin reservas, que termina completada; un
    /// sujeto que no es un comprador, una <c>fecha</c> y una lista de ítems con sus campos.
    /// </summary>
    private const string Oferta = """
        {
          "clave": "prueba.oferta",
          "entrada": ["quien", "dia", "funciones"],
          "tipos": {
            "quien": { "de": "sujeto" },
            "dia": "fecha",
            "funciones": { "lista": { "sala": "texto", "aforo": "entero" } }
          },
          "salida": ["dia", "cuantas"],
          "fases": { "abrir": ["resumir"] },
          "pasos": { "resumir": { "tipo": "prueba.resumir", "lee": ["dia", "funciones"], "escribe": ["cuantas"] } }
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
    private sealed class Falsos : IPricingPort, IBookingPort, IPaymentsPort, INotificationsPort, IDeshacerAgenda, IDeshacerCobros, IDeshacerExistencias, IDeshacerPedidos, IConsentPort
    {
        public List<string> Llamadas { get; } = [];
        public bool RechazaAutorizar { get; set; }

        /// <summary>Si está, cada llamada se escribe en el registro, como lo haría el cliente de una capacidad.</summary>
        public ILogger? Log { get; set; }

        /// <summary>Rechaza sólo la autorización cuya llave termina en <c>|{esto}</c>.</summary>
        public string? RechazaLaLlave { get; set; }

        /// <summary>El cobro que devuelve cada llave por su último tramo; <c>pg-1</c> si no está.</summary>
        public Dictionary<string, string> CobroPorLlave { get; } = new(StringComparer.Ordinal);

        public Task<Result<QuoteView>> CotizarAsync(IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas, string? promocion, CancellationToken ct)
        {
            Llamadas.Add($"cotizar {string.Join(",", lineas.Select(l => $"{l.Sujeto}x{l.Cantidad}"))}");
            var m = Money.Of(10_000m * lineas.Sum(l => l.Cantidad), "COP");
            return Task.FromResult(Result.Ok(new QuoteView(m, Money.Zero("COP"), Money.Zero("COP"), m)));
        }

        public Task<Result<string>> HallarAsync(Ref sujeto, CancellationToken ct) => Ok("rs-1", $"hallar {sujeto}");
        public Task<Result<string>> VerificarAsync(Ref sujeto, string proposito, CancellationToken ct) => Ok("gr-1", $"permiso {proposito}");
        /// <summary>Cuándo vence el apartado falso: lo que <c>booking.apartar</c> escribe si la definición lo nombra.</summary>
        public static readonly DateTimeOffset VenceElApartado = new(2026, 10, 16, 15, 10, 0, TimeSpan.Zero);

        public async Task<Result<Apartado>> ApartarAsync(string recurso, TimeWindow ventana, Ref paraQuien, IdempotencyKey llave, CancellationToken ct)
            => (await Ok("bh-1", $"apartar {recurso} {llave.Value}")).Map(id => new Apartado(id, VenceElApartado));
        public Task<Result<string>> ConfirmarAsync(string apartado, IdempotencyKey llave, CancellationToken ct) => Ok("rv-1", $"confirmar {apartado}");
        public Task<Result<string>> AutorizarAsync(Ref paraQue, Ref pagador, Money monto, IdempotencyKey llave, CancellationToken ct)
        {
            Llamadas.Add($"autorizar {monto} {llave.Value}");
            Log?.LogInformation("Llamada {Llamada}", $"autorizar {monto}");
            var tramo = llave.Value[(llave.Value.LastIndexOf('|') + 1)..];
            return Task.FromResult(RechazaAutorizar || tramo == RechazaLaLlave
                ? Result.Rejected<string>(Rejection.Conflict("payments.payment_declined", "guionado"))
                : Result.Ok(CobroPorLlave.GetValueOrDefault(tramo, "pg-1")));
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
        Task<Result<string>> IDeshacerPedidos.CancelarAsync(string pedido, CancellationToken ct) => Ok(pedido, $"anular-pedido {pedido}");

        private Task<Result<string>> Ok(string valor, string llamada)
        {
            Llamadas.Add(llamada);
            Log?.LogInformation("Llamada {Llamada}", llamada);
            return Task.FromResult(Result.Ok(valor));
        }
    }

    /// <summary><c>prueba.lineas</c>: el equipo por las unidades, lo que cotiza la renta (un paso de dominio).</summary>
    private sealed class PasoLineasDeLaRenta : IPaso
    {
        public string Tipo => "prueba.lineas";
        public int Lecturas => 2;
        public int Escrituras => 1;
        public LlaveRequerida Llave => LlaveRequerida.Ninguna;
        public IReadOnlyList<Type> TiposDeEscritura => [typeof(IReadOnlyList<(Ref Sujeto, int Cantidad)>)];

        public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
        {
            IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas = [(entrada.Lee<Ref>(0), entrada.Lee<int>(1))];
            return Task.FromResult(SalidaDePaso.Sigue(lineas));
        }
    }

    /// <summary><c>prueba.conservar</c>: cierra la garantía sin capturarla (lo que NN-4 de 005 pide como paso común).</summary>
    private sealed class PasoConservar : IPaso
    {
        public string Tipo => "prueba.conservar";
        public int Lecturas => 1;
        public int Escrituras => 0;
        public LlaveRequerida Llave => LlaveRequerida.Ninguna;
        public IReadOnlyList<Type> TiposDeEscritura => [];

        public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct) => Task.FromResult(SalidaDePaso.Sigue());
    }

    /// <summary><c>prueba.resumir</c>: cuántas funciones trae la oferta de ese día (un paso de dominio sin capacidad).</summary>
    private sealed class PasoResumir : IPaso
    {
        public string Tipo => "prueba.resumir";
        public int Lecturas => 2;
        public int Escrituras => 1;
        public LlaveRequerida Llave => LlaveRequerida.Ninguna;
        public IReadOnlyList<Type> TiposDeEscritura => [typeof(int)];

        public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
        {
            _ = entrada.Lee<DateOnly>(0);
            return Task.FromResult(SalidaDePaso.Sigue(entrada.Lee<IReadOnlyList<FlowContext>>(1).Count));
        }
    }

    private static FlowContext EntradaDeRenta(int unidades, decimal garantia) => new FlowContext()
        .Set("inquilino", Paciente).Set("equipo", Ref.Create("prueba.equipo", "andamio")).Set("ventana", Hora)
        .Set("unidades", unidades).Set("garantia", Money.Of(garantia, "COP"));

    private sealed class Banco : IDisposable
    {
        private readonly ILoggerFactory? _logs;

        /// <param name="definicion">La definición.</param>
        /// <param name="logs">Si está, el motor, el intérprete y los puertos escriben en él (NB-9); si no, en nada.</param>
        public Banco(string definicion = Cita, ILoggerFactory? logs = null)
        {
            _logs = logs;
            Puertos.Log = logs?.CreateLogger("puertos");
            Flujo = FlujoDef.Leer(definicion);
            var vocabulario = new SagaVocabulary("prueba", "la cita");
            var deshacer = new DeshacerComun(Puertos, Puertos, Puertos, Puertos, []);
            Raiz = Path.Combine(Path.GetTempPath(), "host-flujos-" + Guid.NewGuid().ToString("N"));
            var almacen = new FileSystemSagaStore<SagaDeFlujo>(Options.Create(new SagaStorageOptions { Root = Raiz }));
            Motor = new SagaEngine<SagaDeFlujo>(
                almacen, new Compensator<SagaDeFlujo>(deshacer, TimeProvider.System, NullLogger<Compensator<SagaDeFlujo>>.Instance),
                new CompensationAlert(new SinRed(), vocabulario, Options.Create(new AlertOptions())),
                ArriendoDePrueba.Nuevo(), vocabulario, TimeProvider.System,
                logs?.CreateLogger<SagaEngine<SagaDeFlujo>>() ?? NullLogger<SagaEngine<SagaDeFlujo>>.Instance);
            Registro = new RegistroDePasos(
            [
                new PasoAgendaApartar(Puertos), new PasoAgendaConfirmar(Puertos), new PasoPagosAutorizar(Puertos),
                new PasoPagosCapturar(Puertos), new PasoAvisar(Puertos),
                new PasoCotizacionSinCodigo(Puertos), new PasoDesglose(), new PasoAgendaHallar(Puertos),
                new PasoLineasDeLaRenta(), new PasoConservar(), new PasoConsentimientoVerificar(Puertos), new PasoResumir(),
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
                Motor, Registro, Flujo, Binding, Flujo.Fases.Select(f => f.Nombre).ToList(), TimeProvider.System,
                _logs?.CreateLogger("flujo") ?? NullLogger.Instance);
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
    public async Task El_apartado_dice_cuando_vence_si_la_definicion_lo_nombra()
    {
        // NB-5 de 011 y NV-12 de 006: la pantalla sólo puede decir cuánto tiempo queda si lo dice el servidor.
        // La escritura nueva es OPCIONAL: la cita de arriba la ignora y sigue arrancando (todo este fichero).
        var conVence = Mutar("\"escribe\": [\"apartadoId\"], \"llave\": \"hold\"", "\"escribe\": [\"apartadoId\", \"vence\"], \"llave\": \"hold\"")
            .Replace("\"salida\": [\"cobroId\", \"reservaId\"]", "\"salida\": [\"cobroId\", \"reservaId\", \"vence\"]", StringComparison.Ordinal);
        Assert.Contains("\"reservaId\", \"vence\"]", conVence, StringComparison.Ordinal);

        using (var banco = new Banco(conVence))
        {
            Assert.Empty(banco.Errores());
            var abierta = await banco.Fachada().AbrirAsync("cita-1", Paciente, Banco.Entrada(), CancellationToken.None);

            var vence = abierta.Value.Datos["vence"];
            Assert.Equal("instante", vence.Tipo.TrimEnd('?'));
            Assert.Equal(Falsos.VenceElApartado, DateTimeOffset.Parse(vence.Valor!.Value.GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        }

        foreach (var escribe in new[] { "[]", "[\"apartadoId\", \"vence\", \"otro\"]" })
        {
            using var mal = new Banco(Mutar("\"escribe\": [\"apartadoId\"], \"llave\": \"hold\"", $"\"escribe\": {escribe}, \"llave\": \"hold\""));
            Assert.Contains(mal.Errores(), e => e.Contains("«booking.apartar» produce 2, de las que las últimas 1 se pueden no nombrar", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Un registro que guarda cada línea con los ámbitos que tenía abiertos, como los ve el formateador de consola
    /// (<c>IncludeScopes</c>): el texto de cada ámbito y sus valores por nombre.
    /// </summary>
    private sealed class RegistroConAmbitos : ILoggerProvider, ISupportExternalScope
    {
        private IExternalScopeProvider _ambitos = new LoggerExternalScopeProvider();

        public List<(string Mensaje, List<string> Textos, Dictionary<string, object?> Valores)> Lineas { get; } = [];

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _ambitos = scopeProvider;

        public ILogger CreateLogger(string categoryName) => new Registrador(this);

        public void Dispose() { }

        private sealed class Registrador(RegistroConAmbitos dueno) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => dueno._ambitos.Push(state);

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var textos = new List<string>();
                var valores = new Dictionary<string, object?>(StringComparer.Ordinal);
                dueno._ambitos.ForEachScope((ambito, _) =>
                {
                    textos.Add(ambito?.ToString() ?? string.Empty);
                    if (ambito is IEnumerable<KeyValuePair<string, object?>> pares)
                    {
                        foreach (var (k, v) in pares) valores[k] = v;
                    }
                }, (object?)null);
                lock (dueno.Lineas) dueno.Lineas.Add((formatter(state, exception), textos, valores));
            }
        }
    }

    [Fact]
    public async Task Cada_linea_de_una_fase_y_de_lo_que_se_deshace_lleva_la_saga_y_su_numero()
    {
        // NB-9 de 011: medido en vivo, el número de una reserva salía cero veces en el registro del orquestador.
        // Las llamadas a las capacidades las escribe quien llama, no el intérprete: si llevan la saga, la lleva
        // todo lo de en medio. Al abrir (apartar, autorizar) y al deshacer (liberar, anular).
        var registro = new RegistroConAmbitos();
        using var logs = LoggerFactory.Create(b => b.AddProvider(registro).SetMinimumLevel(LogLevel.Trace));
        using var banco = new Banco(logs: logs);
        var fachada = banco.Fachada();

        var abierta = await fachada.AbrirAsync("cita-1", Paciente, Banco.Entrada(), CancellationToken.None);
        Assert.True((await fachada.CancelarAsync(abierta.Value.Id, Paciente, CancellationToken.None)).IsOk);

        var llamadas = registro.Lineas.Where(l => l.Mensaje.StartsWith("Llamada ", StringComparison.Ordinal)).ToList();
        foreach (var hecho in new[] { "apartar", "autorizar", "liberar", "anular" })
        {
            Assert.Contains(llamadas, l => l.Mensaje.Contains(hecho, StringComparison.Ordinal));
        }
        Assert.All(llamadas, l =>
        {
            Assert.Equal("cita-1", l.Valores.GetValueOrDefault("Saga"));
            Assert.Equal("CITA1", l.Valores.GetValueOrDefault("NumeroDeSaga"));
            Assert.Contains("Saga:cita-1 Numero:CITA1", l.Textos);   // lo que imprime la consola, no el nombre de un tipo
        });
    }

    [Fact]
    public async Task Listar_no_trae_las_del_dueno_en_otro_flujo_del_mismo_almacen()
    {
        // NB-6: varios flujos genéricos de un orquestador comparten almacén. «Mis citas» no son «mis compras».
        using var banco = new Banco();
        var fachada = banco.Fachada();
        Assert.True((await fachada.AbrirAsync("cita-1", Paciente, Banco.Entrada(), CancellationToken.None)).IsOk);
        banco.Motor.Put(new SagaDeFlujo("otra-1", "prueba.otro", Paciente, SagaStatus.Completed,
            new Dictionary<string, ValorDeFlujo>(), new Dictionary<string, IReadOnlyList<ReservaDeFlujo>>(), [], null, DateTimeOffset.UtcNow));

        var (items, total) = fachada.Listar(Paciente, null, 0, 10);

        Assert.Equal(("cita-1", 1), (Assert.Single(items).Id, total));
        Assert.Equal(2, banco.Motor.DeDueno(Paciente).Count);   // el almacén sí la tiene: la deja fuera la fachada
    }

    [Fact]
    public async Task Pedir_permiso_declara_su_proposito_en_los_dos_sentidos()
    {
        // NN-3 de Salud: el propósito es un dato del flujo, como la plantilla del aviso, y se cruza igual.
        var conPermiso = Mutar("\"abrir\": [\"apartar\", \"autorizar\"]", "\"abrir\": [\"permiso\", \"apartar\", \"autorizar\"]")
            .Replace("\"pasos\": {", "\"pasos\": {\n    \"permiso\": { \"tipo\": \"consent.verificar\", \"lee\": [\"paciente\"], \"proposito\": \"salud.agenda\" },",
                StringComparison.Ordinal);
        Assert.Contains("\"proposito\": \"salud.agenda\"", conPermiso, StringComparison.Ordinal);

        using (var control = new Banco(conPermiso))
        {
            Assert.Empty(control.Errores());
            Assert.True((await control.Fachada().AbrirAsync("cita-1", Paciente, Banco.Entrada(), CancellationToken.None)).IsOk);
            Assert.Equal("permiso salud.agenda", control.Puertos.Llamadas[0]);
        }

        using (var sinProposito = new Banco(conPermiso.Replace(", \"proposito\": \"salud.agenda\"", string.Empty, StringComparison.Ordinal)))
        {
            Assert.Contains(sinProposito.Errores(), e => e.Contains("«permiso» es de tipo «consent.verificar», que comprueba un permiso con «proposito», y no lo declara", StringComparison.Ordinal));
        }

        var deMas = conPermiso.Replace("\"llave\": \"hold\",", "\"llave\": \"hold\", \"proposito\": \"salud.agenda\",", StringComparison.Ordinal);
        Assert.NotEqual(conPermiso, deMas);
        using (var conDeMas = new Banco(deMas))
        {
            Assert.Contains(conDeMas.Errores(), e => e.Contains("«apartar» declara un propósito y «booking.apartar» no comprueba permisos", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Lo_que_se_guarda_no_se_llama_como_una_credencial()
    {
        // La regla de `PropagacionDeIdentidadTests.Ninguna_saga_guarda_una_credencial`, en la saga genérica:
        // ahí el campo nuevo del record es una palabra en el JSON, y el barrido de la fuente no la ve.
        var conToken = Mutar("\"salida\": [\"cobroId\", \"reservaId\"]", "\"salida\": [\"cobroId\", \"reservaId\", \"tokenDePago\"]")
            .Replace("\"entrada\": [\"paciente\", \"recurso\", \"ventana\", \"monto\"]",
                "\"entrada\": [\"paciente\", \"recurso\", \"ventana\", \"monto\", \"tokenDePago\"]", StringComparison.Ordinal)
            .Replace("\"monto\": \"dinero\",", "\"monto\": \"dinero\", \"tokenDePago\": \"texto\",", StringComparison.Ordinal);
        Assert.Equal(3, conToken.Split("tokenDePago").Length - 1);

        using var banco = new Banco(conToken);
        Assert.Contains(banco.Errores(), e => e.Contains("«tokenDePago» se guarda en la saga y se llama como una credencial", StringComparison.Ordinal));

        // El control: la misma forma con otro nombre arranca.
        using var control = new Banco(conToken.Replace("tokenDePago", "referenciaDePago", StringComparison.Ordinal));
        Assert.Empty(control.Errores());
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

    // ── La forma de la renta (NN-2 de 005): el host la sirve sin código propio ──────────

    [Fact]
    public async Task La_renta_autoriza_dos_cobros_con_llaves_distintas_y_cada_reserva_la_cierra_su_paso()
    {
        using var banco = new Banco(Renta);
        Assert.Empty(banco.Errores());
        banco.Puertos.CobroPorLlave["deposit"] = "pg-garantia";
        banco.Puertos.CobroPorLlave["rental"] = "pg-renta";
        var fachada = banco.Fachada();

        var abierta = await fachada.AbrirAsync("renta-1", Paciente, EntradaDeRenta(2, 300_000m), CancellationToken.None);

        Assert.True(abierta.IsOk, abierta.Rejection?.ToString());
        Assert.Contains("cotizar prueba.equipo:andamiox2", banco.Puertos.Llamadas);
        Assert.Contains("autorizar 300000 COP renta-1|deposit", banco.Puertos.Llamadas);
        Assert.Contains("autorizar 20000 COP renta-1|rental", banco.Puertos.Llamadas);

        var cerrada = await fachada.ContinuarAsync("cerrar", "renta-1", Paciente, new FlowContext(), CancellationToken.None);

        Assert.True(cerrada.IsOk, cerrada.Rejection?.ToString());
        Assert.Equal(SagaStatus.Completed, cerrada.Value.Status);
        Assert.DoesNotContain(cerrada.Value.Compensations, c => c.IsPending);
        Assert.Contains("capturar pg-renta", banco.Puertos.Llamadas);
        Assert.DoesNotContain("capturar pg-garantia", banco.Puertos.Llamadas);
        Assert.Equal("pg-garantia", cerrada.Value.Datos["garantiaId"].Valor!.Value.GetString());
        Assert.Equal("pg-renta", cerrada.Value.Datos["cobroId"].Valor!.Value.GetString());
        Assert.True(cerrada.Value.Datos.ContainsKey("total") && cerrada.Value.Datos.ContainsKey("garantia"),
            "La salida de la renta (total, garantia, garantiaId, cobroId) se guarda entera.");
    }

    [Fact]
    public void Dos_pasos_con_la_misma_llave_no_arrancan()
    {
        // Con la garantía y el alquiler bajo una llave, la segunda autorización devolvería la primera: el equipo
        // apartado y un solo cobro. No hay nada que lo decida al correr; se dice al arrancar.
        Assert.Contains("\"llave\": \"rental\"", Renta, StringComparison.Ordinal);
        using var banco = new Banco(Renta.Replace("\"llave\": \"rental\"", "\"llave\": \"deposit\"", StringComparison.Ordinal));

        Assert.Contains(banco.Errores(), e => e.Contains("los pasos «alquiler» y «deposito» declaran la misma llave «deposit»", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Si_el_alquiler_se_rechaza_tras_la_garantia_se_anula_la_garantia_y_se_suelta_el_equipo()
    {
        using var banco = new Banco(Renta);
        banco.Puertos.CobroPorLlave["deposit"] = "pg-garantia";
        banco.Puertos.RechazaLaLlave = "rental";

        var r = await banco.Fachada().AbrirAsync("renta-1", Paciente, EntradaDeRenta(1, 300_000m), CancellationToken.None);

        Assert.Equal("payments.payment_declined", r.Rejection?.Code);
        Assert.Contains("anular pg-garantia", banco.Puertos.Llamadas);
        Assert.Contains("liberar bh-1", banco.Puertos.Llamadas);
        Assert.Equal(SagaStatus.Compensated, banco.Motor.Find("renta-1")!.Status);
    }

    [Fact]
    public async Task Sin_garantia_no_se_autoriza_ni_se_conserva_y_la_renta_completa()
    {
        using var banco = new Banco(Renta);
        var fachada = banco.Fachada();

        Assert.True((await fachada.AbrirAsync("renta-1", Paciente, EntradaDeRenta(1, 0m), CancellationToken.None)).IsOk);
        var cerrada = await fachada.ContinuarAsync("cerrar", "renta-1", Paciente, new FlowContext(), CancellationToken.None);

        Assert.True(cerrada.IsOk, cerrada.Rejection?.ToString());
        Assert.Equal(SagaStatus.Completed, cerrada.Value.Status);
        Assert.DoesNotContain(banco.Puertos.Llamadas, l => l.EndsWith("|deposit", StringComparison.Ordinal));
        Assert.Single(banco.Puertos.Llamadas, l => l.StartsWith("autorizar", StringComparison.Ordinal));
    }

    // ── Un flujo de una fase sin reservas (NE-10 de 007, núcleo N3) ─────────────────────────────

    [Fact]
    public async Task Un_flujo_de_una_fase_sin_reservas_abre_y_queda_completado()
    {
        using var banco = new Banco(Oferta);
        Assert.Empty(banco.Errores());
        IReadOnlyList<FlowContext> funciones =
        [
            new FlowContext().Set("sala", "A").Set("aforo", 100),
            new FlowContext().Set("sala", "B").Set("aforo", 50),
        ];

        var r = await banco.Fachada().AbrirAsync("oferta-1", Paciente,
            new FlowContext().Set("quien", Paciente).Set("dia", new DateOnly(2026, 10, 20)).Set("funciones", funciones),
            CancellationToken.None);

        Assert.True(r.IsOk, r.Rejection?.ToString());
        Assert.Equal(SagaStatus.Completed, r.Value.Status);
        Assert.Empty(r.Value.Compensations);
        Assert.Equal(2, r.Value.Datos["cuantas"].Valor!.Value.GetInt32());
        Assert.Equal("2026-10-20", r.Value.Datos["dia"].Valor!.Value.GetString());
        Assert.Empty(banco.Puertos.Llamadas);
    }

    // ── La clase de una referencia del cuerpo (NR-1 de 008): un dato de la definición ──────────

    private const string EquipoConClase = "\"equipo\": { \"tipo\": \"ref\", \"kind\": \"prueba.equipo\" }";

    [Fact]
    public void Una_referencia_del_cuerpo_sin_su_clase_no_arranca()
    {
        Assert.Contains(EquipoConClase, Renta, StringComparison.Ordinal);
        using (var control = new Banco(Renta)) Assert.Empty(control.Errores());

        foreach (var sinClase in new[] { "\"equipo\": \"ref\"", "\"equipo\": \"ref?\"" })
        {
            using var banco = new Banco(Renta.Replace(EquipoConClase, sinClase, StringComparison.Ordinal));

            Assert.Contains(banco.Errores(), e => e.Contains("«equipo» es una referencia que viene del cuerpo y no declara su clase", StringComparison.Ordinal)
                && e.Contains("«prueba.renta»", StringComparison.Ordinal));
            Assert.Throws<InvalidOperationException>(() => banco.Fachada());
        }
    }

    [Fact]
    public void Un_campo_de_lista_que_es_referencia_declara_su_clase_y_solo_una_referencia_la_tiene()
    {
        const string extras = "\"extras\": { \"lista\": { \"pieza\": { \"tipo\": \"ref\", \"kind\": \"prueba.pieza\" }, \"cantidad\": \"entero\" } }";
        var conLista = Renta
            .Replace("\"entrada\": [\"inquilino\", \"equipo\", \"ventana\", \"unidades\", \"garantia\"]",
                "\"entrada\": [\"inquilino\", \"equipo\", \"ventana\", \"unidades\", \"garantia\", \"extras\"]", StringComparison.Ordinal)
            .Replace("\"garantia\": \"dinero\"", $"\"garantia\": \"dinero\", {extras}", StringComparison.Ordinal);
        Assert.Contains("\"extras\"]", conLista, StringComparison.Ordinal);
        Assert.Contains(extras, conLista, StringComparison.Ordinal);
        using (var control = new Banco(conLista)) Assert.Empty(control.Errores());

        using (var sinClase = new Banco(conLista.Replace("{ \"tipo\": \"ref\", \"kind\": \"prueba.pieza\" }", "\"ref\"", StringComparison.Ordinal)))
        {
            Assert.Contains(sinClase.Errores(), e => e.Contains("«extras.pieza» es una referencia que viene del cuerpo y no declara su clase", StringComparison.Ordinal));
        }

        var unidadesConClase = Renta.Replace("\"unidades\": \"entero\"", "\"unidades\": { \"tipo\": \"entero\", \"kind\": \"prueba.unidad\" }", StringComparison.Ordinal);
        Assert.NotEqual(Renta, unidadesConClase);
        using var deMas = new Banco(unidadesConClase);
        Assert.Contains(deMas.Errores(), e => e.Contains("«unidades» declara la clase «prueba.unidad» y es «entero»: sólo una referencia tiene clase", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cotizar_la_renta_anticipa_hasta_antes_de_apartar_sin_reservar_nada()
    {
        using var banco = new Banco(Renta);

        var cotizada = await banco.Fachada().AnticiparAsync(EntradaDeRenta(3, 300_000m), CancellationToken.None);

        Assert.True(cotizada.IsOk, cotizada.Rejection?.ToString());
        Assert.Equal(Money.Of(30_000m, "COP"), cotizada.Value.Get<Money>("total"));
        Assert.DoesNotContain(banco.Puertos.Llamadas, l => l.StartsWith("apartar", StringComparison.Ordinal) || l.StartsWith("autorizar", StringComparison.Ordinal));
    }
}
