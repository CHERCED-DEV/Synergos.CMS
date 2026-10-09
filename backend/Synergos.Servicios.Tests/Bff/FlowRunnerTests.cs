using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// El intérprete de flujos declarados (ADR 0140), con una saga y unos pasos que no son de nadie.
/// </summary>
/// <remarks>
/// <para><b>Por qué además del oráculo.</b> <c>TicketingCompensationTests</c> prueba la compra de
/// entradas a través de la fachada, y eso fija lo que Eventos usa. Esto fija el CONTRATO del
/// intérprete para el próximo flujo que se declare: en qué punto nace la saga, cuándo se anota y
/// cuándo cambia de carácter cada compensación, qué se salta, qué se marca hecho. Y cubre lo que
/// Eventos no ejerce: una reserva sin <c>despues</c>, una fase que se corta antes de reservar o a
/// mitad de un bloque, dos reservas únicas en el mismo flujo, un cierre que produce su propio
/// objetivo.</para>
///
/// <para><b>Todo lo observable va a una sola bitácora</b> —cada paso, cada escritura de la saga y
/// cada deshacer, en el orden en que pasaron—, porque lo que estas reglas fijan es el ORDEN: la
/// compensación anotada en el acto y no al final, el carácter cambiado dentro del bucle y no
/// después.</para>
///
/// <para><b>El bloque escribe en el ítem y lo lee en el paso siguiente</b>, con dos ítems: es de lo
/// que vive la compra de Eventos (<c>linea.itemId</c>, <c>linea.holdId</c>), y una escritura que se
/// escapara al contexto del flujo dejaría a la segunda línea leyendo lo de la primera.</para>
/// </remarks>
public sealed class FlowRunnerTests
{
    private const string Definicion = """
        {
          "clave": "prueba.compra",
          "entrada": ["items", "monto"],
          "fases": {
            "abrir": [
              "revisar",
              "cotizar",
              { "para_cada": "items", "como": "it", "pasos": ["marcar", "apartar"] },
              "autorizar"
            ],
            "cerrar": [
              "capturar",
              { "para_cada": "reservas:apartar", "como": "ap", "pasos": ["consumir"] }
            ]
          },
          "pasos": {
            "revisar": { "tipo": "t.revisar", "lee": ["items"] },
            "cotizar": { "tipo": "t.cotizar", "lee": ["monto"], "escribe": ["precio"], "motivo": "no se pudo cotizar" },
            "marcar": { "tipo": "t.marcar", "lee": ["it.sku"], "escribe": ["it.pozo"] },
            "apartar": {
              "tipo": "t.apartar", "lee": ["it.sku", "it.pozo"], "llave_base": "ap", "motivo": "no se pudo apartar",
              "reserva": { "consumado_por": "consumir", "antes": "Soltar", "despues": "Reponer", "motivo": "sin cerrar" }
            },
            "autorizar": {
              "tipo": "t.autorizar", "lee": ["precio"], "escribe": ["cobro"], "llave": "auth", "motivo": "no autorizó",
              "reserva": { "consumado_por": "capturar", "antes": "Anular", "despues": "Devolver", "motivo": "sin cerrar" }
            },
            "capturar": { "tipo": "t.capturar", "lee": ["cobro"], "llave": "cap", "cierra_reserva": "autorizar", "motivo": "no capturó" },
            "consumir": { "tipo": "t.consumir", "lee": ["ap.holdId"], "cierra_reserva": "apartar", "motivo": "no consumió" }
          }
        }
        """;

    /// <summary>
    /// Dos reservas ÚNICAS en el mismo flujo: el pedido y el cobro, que es la forma de Tienda (y la
    /// de Salud, con la hora en la agenda en vez del pedido).
    /// </summary>
    private const string DosUnicas = """
        {
          "clave": "prueba.pedido",
          "entrada": ["monto"],
          "fases": {
            "abrir": ["pedir", "autorizar"],
            "cerrar": ["capturar", "confirmar"]
          },
          "pasos": {
            "pedir": {
              "tipo": "t.pedir", "escribe": ["pedido"], "llave": "pedido", "motivo": "no se pudo pedir",
              "reserva": { "consumado_por": "confirmar", "antes": "Cancelar", "motivo": "sin cerrar" }
            },
            "autorizar": {
              "tipo": "t.autorizar", "lee": ["monto"], "escribe": ["cobro"], "llave": "auth", "motivo": "no autorizó",
              "reserva": { "consumado_por": "capturar", "antes": "Anular", "despues": "Devolver", "motivo": "sin cerrar" }
            },
            "capturar": { "tipo": "t.capturar", "lee": ["cobro"], "llave": "cap", "cierra_reserva": "autorizar", "motivo": "no capturó" },
            "confirmar": { "tipo": "t.confirmar", "lee": ["pedido"], "cierra_reserva": "pedir", "motivo": "no confirmó" }
          }
        }
        """;

    /// <summary>
    /// La de arriba con un aviso al final de «cerrar», después del último cierre, que puede fallar sin
    /// deshacer nada, y con la dirección como efímera de «cerrar»: la forma de <c>avisar</c> en Eventos.
    /// </summary>
    private static readonly string ConAviso = Definicion
        .Replace("\"entrada\": [\"items\", \"monto\"],",
            "\"entrada\": [\"items\", \"monto\"],\n  \"efimera\": { \"cerrar\": [\"nota\"] },", StringComparison.Ordinal)
        .Replace("{ \"para_cada\": \"reservas:apartar\", \"como\": \"ap\", \"pasos\": [\"consumir\"] }",
            "{ \"para_cada\": \"reservas:apartar\", \"como\": \"ap\", \"pasos\": [\"consumir\"] },\n      \"avisar\"", StringComparison.Ordinal)
        .Replace("\"consumir\": { \"tipo\": \"t.consumir\",",
            "\"avisar\": { \"tipo\": \"t.avisar\", \"lee\": [\"nota\"], \"al_fallar\": \"seguir\" },\n    \"consumir\": { \"tipo\": \"t.consumir\",", StringComparison.Ordinal);

    /// <summary>La de arriba, gratis si la cotización da cero: autorizar no corre, capturar se salta.</summary>
    private static readonly string GratisSiCero = Definicion.Replace(
        "\"llave\": \"auth\", \"motivo\": \"no autorizó\",",
        "\"llave\": \"auth\", \"motivo\": \"no autorizó\", \"omitir_si_cero\": \"precio\",", StringComparison.Ordinal);

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> NotaEnCerrar =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal) { ["cerrar"] = new[] { "nota" } };

    private static readonly string[] Fases = { "abrir", "cerrar" };

    // ── El banco: una saga mínima con su ranura, y pasos guionados ──────────

    private sealed record SagaDePrueba(
        string Id,
        SagaStatus Status,
        IReadOnlyList<Compensation> Compensations,
        DateTimeOffset StartedAtUtc,
        IReadOnlyDictionary<string, IReadOnlyList<HoldLeg>> Reservado,
        string? Falla,
        DateTimeOffset? AlertedAtUtc = null,
        int AlertsSent = 0) : ISaga<SagaDePrueba>, IHoldLedger
    {
        public SagaDePrueba WithStatus(SagaStatus status) => this with { Status = status };
        public SagaDePrueba WithCompensations(IReadOnlyList<Compensation> compensations) => this with { Compensations = compensations };
        public SagaDePrueba WithAlert(DateTimeOffset? alertedAtUtc, int alertsSent) => this with { AlertedAtUtc = alertedAtUtc, AlertsSent = alertsSent };

        // Como TicketingSaga: una reserva que el record no guarda NO es «ninguna», es un error.
        private static readonly HashSet<string> Conocidas = new(StringComparer.Ordinal) { "apartar", "autorizar", "pedir" };

        public IReadOnlyList<HoldLeg> Legs(string paso) => Conocidas.Contains(paso)
            ? Reservado.GetValueOrDefault(paso) ?? Array.Empty<HoldLeg>()
            : throw new InvalidOperationException($"La saga de prueba no guarda reservas de «{paso}».");

        public static SagaDePrueba Nueva(string id, DateTimeOffset ahora, SagaStatus status = SagaStatus.Running)
            => new(id, status, Array.Empty<Compensation>(), ahora,
                new Dictionary<string, IReadOnlyList<HoldLeg>>(StringComparer.Ordinal), null);
    }

    private sealed class BindingDePrueba : IFlowBinding<SagaDePrueba>
    {
        /// <summary>Un nombre que declara reconstruir y NO pone: el binding que se desvió.</summary>
        public string? Olvida { get; init; }

        /// <summary>Un nombre que declara poner al abrir y el banco NO pone.</summary>
        public string? PoneDeMas { get; init; }

        /// <summary>Una reserva que declara y la saga no sabe leer.</summary>
        public string? ReservaDeMas { get; init; }

        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> Efimera { get; init; } = ContratoDelFlujo.SinEfimera;

        public IReadOnlyCollection<string> PoneAlAbrir => PoneDeMas is null
            ? new[] { "items", "monto" }
            : new[] { "items", "monto", PoneDeMas };

        public IReadOnlyCollection<string> Reconstruye { get; } = new[] { "cobro", "pedido" };

        public IReadOnlyDictionary<string, IReadOnlyCollection<string>> CamposDeItem { get; } =
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal) { ["items"] = new[] { "sku" } };

        public IReadOnlyDictionary<string, FormaDeReserva> Reservas
        {
            get
            {
                var reservas = new Dictionary<string, FormaDeReserva>(StringComparer.Ordinal)
                {
                    ["apartar"] = FormaDeReserva.PorItem,
                    ["autorizar"] = FormaDeReserva.Unica,
                    ["pedir"] = FormaDeReserva.Unica,
                };
                if (ReservaDeMas is not null) reservas[ReservaDeMas] = FormaDeReserva.Unica;
                return reservas;
            }
        }

        public SagaDePrueba Crear(string sagaId, FlowContext ctx, DateTimeOffset ahora) => SagaDePrueba.Nueva(sagaId, ahora);

        public FlowContext Leer(SagaDePrueba saga)
        {
            var ctx = new FlowContext();
            foreach (var (nombre, paso) in new[] { ("cobro", "autorizar"), ("pedido", "pedir") })
            {
                if (nombre != Olvida) ctx.Set(nombre, saga.Legs(paso).SingleOrDefault()?.HoldId);
            }
            return ctx;
        }

        public SagaDePrueba ConReserva(SagaDePrueba saga, string paso, HoldLeg reservado, FlowContext? item)
            => saga with
            {
                Reservado = new Dictionary<string, IReadOnlyList<HoldLeg>>(saga.Reservado, StringComparer.Ordinal)
                {
                    [paso] = saga.Legs(paso).Append(reservado).ToList(),
                },
            };

        public SagaDePrueba ConError(SagaDePrueba saga, string? falla) => saga with { Falla = falla };
    }

    private sealed class Banco
    {
        public Banco(string definicion = Definicion, BindingDePrueba? binding = null, ISagaLease? arriendo = null)
        {
            Sagas = new Almacen(Bitacora);
            var vocabulario = new SagaVocabulary("prueba", "la prueba");
            Motor = new SagaEngine<SagaDePrueba>(
                Sagas,
                new Compensator<SagaDePrueba>(new Deshacer(Bitacora), Reloj, NullLogger<Compensator<SagaDePrueba>>.Instance),
                new CompensationAlert(new SinRed(), vocabulario, Options.Create(new AlertOptions())),
                arriendo ?? ArriendoDePrueba.Nuevo(), vocabulario, Reloj, NullLogger<SagaEngine<SagaDePrueba>>.Instance);
            Flujo = FlujoDef.Leer(definicion);
            Binding = binding ?? new BindingDePrueba();
            Registro = new RegistroDePasos(new IPaso[]
            {
                Paso("t.revisar", 1, 0, LlaveRequerida.Ninguna, _ => SalidaDePaso.Sigue()),
                Paso("t.cotizar", 1, 1, LlaveRequerida.Ninguna, e => SalidaDePaso.Sigue(Money.Of(e.Lee<decimal>(0) * 2, "COP"))),
                Paso("t.marcar", 1, 1, LlaveRequerida.Ninguna, e => SalidaDePaso.Sigue($"pozo-{e.Lee<string>(0)}"),
                    e => e.Lee<string>(0)),
                Paso("t.apartar", 2, 0, LlaveRequerida.PorItem, e =>
                {
                    var sku = e.Lee<string>(0);
                    Bitacora.Add($"  llave {e.LlavePara(sku).Value}");
                    return SalidaDePaso.Reserva(new Reservado($"h-{sku}", CierreId: e.Lee<string>(1)));
                }, e => e.Lee<string>(0)),
                Paso("t.autorizar", 1, 1, LlaveRequerida.Fija, e =>
                {
                    // Como Api.Payments: un cobro de cero no es un cobro (payments.zero_amount).
                    if (e.Lee<object>(0) is Money { IsZero: true }) return SalidaDePaso.Rechaza(Rejection.Invalid("t.autorizar.zero_amount", "guionado"));
                    Bitacora.Add($"  llave {e.Llave().Value}");
                    return SalidaDePaso.Reserva(new Reservado("cobro-1"), "cobro-1");
                }),
                Paso("t.capturar", 1, 0, LlaveRequerida.Fija, _ => SalidaDePaso.Sigue(), e => e.Lee<string>(0)),
                Paso("t.consumir", 1, 0, LlaveRequerida.Ninguna, e => ProducenCierre
                    ? SalidaDePaso.Cierra($"res-{e.Lee<string>(0)}")
                    : SalidaDePaso.Sigue(), e => e.Lee<string>(0)),
                Paso("t.pedir", 0, 1, LlaveRequerida.Fija, e =>
                {
                    Bitacora.Add($"  llave {e.Llave().Value}");
                    return SalidaDePaso.Reserva(new Reservado("pedido-1"), "pedido-1");
                }),
                Paso("t.confirmar", 1, 0, LlaveRequerida.Ninguna, _ => SalidaDePaso.Sigue(), e => e.Lee<string>(0)),
                Paso("t.avisar", 1, 0, LlaveRequerida.Ninguna, _ =>
                {
                    AlAvisar?.Invoke();
                    if (AvisarLanza is { } excepcion) throw excepcion;
                    return SalidaDePaso.Sigue();
                }, e => e.Lee<string?>(0) ?? "-"),
            });
            Runner = new FlowRunner<SagaDePrueba>(Motor, Registro, Flujo, Binding, Fases, Reloj, Log);
        }

        /// <summary>Lo que el intérprete escribe en el log, con las excepciones enteras.</summary>
        public LogQueGuarda Log { get; } = new();

        /// <summary>Si el aviso, en vez de rechazar, lanza.</summary>
        public Exception? AvisarLanza { get; set; }

        /// <summary>Lo que pasa MIENTRAS se avisa: la ventana en la que otra operación llega a la misma saga.</summary>
        public Action? AlAvisar { get; set; }

        public List<string> Bitacora { get; } = new();

        /// <summary>Qué pasos fallan: el tipo, o <c>tipo@dato</c> para uno solo de un bloque.</summary>
        public HashSet<string> Fallan { get; } = new(StringComparer.Ordinal);

        /// <summary>Qué pasos cortan la fase en vez de seguir: el tipo, o <c>tipo@dato</c>.</summary>
        public HashSet<string> Saltan { get; } = new(StringComparer.Ordinal);

        /// <summary>Si consumir produce su propio objetivo de después, como confirmar en Viajes.</summary>
        public bool ProducenCierre { get; set; }

        public RelojFijo Reloj { get; } = new();
        public Almacen Sagas { get; }
        public SagaEngine<SagaDePrueba> Motor { get; }
        public FlujoDef Flujo { get; }
        public BindingDePrueba Binding { get; }
        public RegistroDePasos Registro { get; }
        public FlowRunner<SagaDePrueba> Runner { get; }

        private IPaso Paso(
            string tipo, int lee, int escribe, LlaveRequerida llave, Func<EntradaDePaso, SalidaDePaso> hace,
            Func<EntradaDePaso, string>? dato = null)
            => new Guionado(tipo, lee, escribe, llave, e =>
            {
                var d = dato?.Invoke(e);
                Bitacora.Add(d is null ? $"paso {tipo}" : $"paso {tipo} {d}");
                if (Fallan.Contains(tipo) || (d is not null && Fallan.Contains($"{tipo}@{d}")))
                {
                    return SalidaDePaso.Rechaza(Rejection.Conflict($"{tipo}.no", "guionado"));
                }
                return Saltan.Contains(tipo) || (d is not null && Saltan.Contains($"{tipo}@{d}"))
                    ? SalidaDePaso.SaltaFase(new object?[escribe])
                    : hace(e);
            });

        public Task<Result<SagaDePrueba>> Abrir(params string[] skus) => AbrirPor(10m, skus);

        public Task<Result<SagaDePrueba>> AbrirPor(decimal monto, params string[] skus)
            => Runner.EjecutarFaseAsync("abrir", "s1", new FlowContext()
                .Set("items", skus.Select(s => new FlowContext().Set("sku", s)).ToList())
                .Set("monto", monto), CancellationToken.None);

        public Task<Result<SagaDePrueba>> Cerrar()
            => Runner.EjecutarFaseAsync("cerrar", "s1", new FlowContext(), CancellationToken.None);

        public Task<Result<SagaDePrueba>> Cerrar(string? nota, CancellationToken ct = default)
            => Runner.EjecutarFaseAsync("cerrar", "s1", new FlowContext().Set("nota", nota), ct);

        public IEnumerable<string> Escrituras => Bitacora.Where(l => l.StartsWith("put", StringComparison.Ordinal));
        public IEnumerable<string> Deshechos => Bitacora.Where(l => l.StartsWith("undo", StringComparison.Ordinal));
        public IEnumerable<string> Pasos => Bitacora.Where(l => l.StartsWith("paso", StringComparison.Ordinal));
    }

    private sealed class LogQueGuarda : ILogger
    {
        public List<string> Lineas { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Lineas.Add($"{formatter(state, exception)} {exception}");
    }

    private sealed class Guionado(string tipo, int lee, int escribe, LlaveRequerida llave, Func<EntradaDePaso, SalidaDePaso> hace) : IPaso
    {
        public string Tipo => tipo;
        public int Lecturas => lee;
        public int Escrituras => escribe;
        public LlaveRequerida Llave => llave;
        public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct) => Task.FromResult(hace(entrada));
    }

    private sealed class Almacen(List<string> bitacora) : ISagaStore<SagaDePrueba>
    {
        private readonly Dictionary<string, SagaDePrueba> _s = new(StringComparer.Ordinal);

        public SagaDePrueba? Find(string id) => _s.GetValueOrDefault(id);
        public IReadOnlyList<SagaDePrueba> WithPendingCompensations() => _s.Values.Where(x => x.IsUnwinding()).ToList();
        public IReadOnlyList<SagaDePrueba> StartedBefore(DateTimeOffset limite) => Array.Empty<SagaDePrueba>();
        public void Invalidate() { }

        public void Put(SagaDePrueba saga)
        {
            _s[saga.Id] = saga;
            bitacora.Add($"put {saga.Status} [{string.Join(", ", saga.Compensations.Select(c => $"{c.Kind}:{c.TargetId}{(c.IsPending ? "" : "+")}"))}]");
        }

        /// <summary>Deja una saga como la habría dejado otra llamada, sin pasar por la bitácora.</summary>
        public void Dejar(SagaDePrueba saga) => _s[saga.Id] = saga;
    }

    private sealed class Deshacer(List<string> bitacora) : ICompensationExecutor<SagaDePrueba>
    {
        public Task<Rejection?> UndoAsync(SagaDePrueba saga, Compensation pending, CancellationToken ct)
        {
            bitacora.Add($"undo {pending.Kind}:{pending.TargetId} ({pending.Reason})");
            return Task.FromResult<Rejection?>(null);
        }
    }

    private sealed class SinRed : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("Ninguna compensación de estas pruebas se rinde: nadie debería avisar.");
    }

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
    }

    // ── Abrir ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Abrir_da_los_pasos_en_orden_y_anota_cada_reserva_EN_EL_ACTO()
    {
        var banco = new Banco();

        var r = await banco.Abrir("a", "b");

        Assert.True(r.IsOk);
        Assert.Equal(new[]
        {
            "paso t.revisar",
            "paso t.cotizar",
            "paso t.marcar a",
            "paso t.apartar a",
            "  llave s1|ap:a",
            "put Running [Soltar:h-a]",
            "paso t.marcar b",
            "paso t.apartar b",
            "  llave s1|ap:b",
            "put Running [Soltar:h-a, Soltar:h-b]",
            "paso t.autorizar",
            "  llave s1|auth",
            "put Running [Soltar:h-a, Soltar:h-b, Anular:cobro-1]",
        }, banco.Bitacora);

        // Lo apartado se guarda con su objetivo de DESPUÉS de consumir, que es el pozo que el paso
        // anterior del bloque escribió EN ESE ítem: si la escritura se escapara al contexto, la
        // segunda línea leería el de la primera, o ninguno.
        Assert.Equal(new[] { new HoldLeg("h-a", "pozo-a"), new HoldLeg("h-b", "pozo-b") }, r.Value.Legs("apartar"));
        Assert.Equal(new[] { new HoldLeg("cobro-1", "cobro-1") }, r.Value.Legs("autorizar"));
        Assert.All(r.Value.Compensations, c => Assert.Equal("sin cerrar", c.Reason));
    }

    [Fact]
    public async Task Las_llaves_son_las_de_KeyFor_la_fija_y_la_del_item()
    {
        var banco = new Banco();

        var r = await banco.Abrir("a");

        Assert.Contains($"  llave {r.Value.KeyFor("ap:a").Value}", banco.Bitacora);
        Assert.Contains($"  llave {r.Value.KeyFor("auth").Value}", banco.Bitacora);
    }

    [Fact]
    public async Task Lo_que_falla_ANTES_de_reservar_rechaza_sin_saga()
    {
        // La saga nace justo antes del primer tramo que reserva. Un precio que no se pudo cotizar
        // no toca a nadie, y no tiene que dejar una saga deshecha de más.
        var banco = new Banco();
        banco.Fallan.Add("t.cotizar");

        var r = await banco.Abrir("a");

        Assert.False(r.IsOk);
        Assert.Equal("t.cotizar.no", r.Rejection!.Code);
        Assert.Empty(banco.Escrituras);
        Assert.Empty(banco.Deshechos);
        Assert.Null(banco.Sagas.Find("s1"));
    }

    [Fact]
    public async Task Si_la_tercera_reserva_falla_se_deshacen_las_dos_anteriores_con_el_motivo_del_paso()
    {
        var banco = new Banco();
        banco.Fallan.Add("t.apartar@c");

        var r = await banco.Abrir("a", "b", "c");

        Assert.False(r.IsOk);
        Assert.Equal("t.apartar.no", r.Rejection!.Code);   // el rechazo ORIGINAL, no uno propio
        Assert.Equal(new[] { "undo Soltar:h-a (no se pudo apartar)", "undo Soltar:h-b (no se pudo apartar)" }, banco.Deshechos);
        Assert.DoesNotContain("paso t.autorizar", banco.Bitacora);

        var saga = banco.Sagas.Find("s1")!;
        Assert.Equal(SagaStatus.Compensated, saga.Status);
        Assert.Equal(r.Rejection!.ToString(), saga.Falla);
    }

    [Fact]
    public async Task Una_fase_que_se_corta_antes_de_reservar_igual_deja_su_saga()
    {
        var banco = new Banco();
        banco.Saltan.Add("t.revisar");

        var r = await banco.Abrir("a");

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Running, r.Value.Status);
        Assert.DoesNotContain(banco.Bitacora, l => l.StartsWith("paso t.apartar", StringComparison.Ordinal));
        Assert.Equal(new[] { "put Running []" }, banco.Escrituras);
    }

    [Fact]
    public async Task Una_fase_que_se_corta_A_MITAD_de_un_bloque_conserva_lo_reservado_y_no_sigue()
    {
        // «La fase termina acá, bien»: el segundo ítem no se aparta, el paso de después del bloque
        // no corre, y lo que el primero ya reservó queda guardado con su compensación armada.
        var banco = new Banco();
        banco.Saltan.Add("t.marcar@b");

        var r = await banco.Abrir("a", "b", "c");

        Assert.True(r.IsOk);
        Assert.Equal(new[]
        {
            "paso t.revisar",
            "paso t.cotizar",
            "paso t.marcar a",
            "paso t.apartar a",
            "paso t.marcar b",
        }, banco.Pasos);
        Assert.Equal(new[] { "put Running [Soltar:h-a]" }, banco.Escrituras);
        Assert.Equal(SagaStatus.Running, r.Value.Status);
        Assert.Equal(new[] { new HoldLeg("h-a", "pozo-a") }, r.Value.Legs("apartar"));
        Assert.Empty(banco.Deshechos);
    }

    // ── Cerrar ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cerrar_continua_la_saga_GUARDADA_y_termina_Completed_sin_nada_armado()
    {
        var banco = new Banco();
        await banco.Abrir("a", "b");
        banco.Bitacora.Clear();

        var r = await banco.Cerrar();

        Assert.True(r.IsOk);
        Assert.Equal(new[]
        {
            // El cobro lo lee de lo que la saga guardó: el contexto de abrir ya no existe.
            "paso t.capturar cobro-1",
            "put Running [Soltar:h-a, Soltar:h-b, Devolver:cobro-1]",
            "paso t.consumir h-a",
            "put Running [Reponer:pozo-a, Soltar:h-b, Devolver:cobro-1]",
            "paso t.consumir h-b",
            "put Running [Reponer:pozo-a, Reponer:pozo-b, Devolver:cobro-1]",
            "put Completed [Reponer:pozo-a+, Reponer:pozo-b+, Devolver:cobro-1+]",
        }, banco.Bitacora);
        Assert.Null(r.Value.Falla);
    }

    [Fact]
    public async Task Si_el_consumo_falla_a_la_mitad_lo_consumido_se_REPONE_lo_demas_se_SUELTA_y_el_cobro_se_DEVUELVE()
    {
        // El cambio de carácter tiene que haber ocurrido DENTRO del bucle: la primera ya está
        // consumida y soltarla lo rechazaría la capacidad; la segunda y la tercera no.
        var banco = new Banco();
        await banco.Abrir("a", "b", "c");
        banco.Fallan.Add("t.consumir@h-b");

        var r = await banco.Cerrar();

        Assert.False(r.IsOk);
        Assert.Equal(new[]
        {
            "undo Reponer:pozo-a (no consumió)",
            "undo Soltar:h-b (no consumió)",
            "undo Soltar:h-c (no consumió)",
            "undo Devolver:cobro-1 (no consumió)",
        }, banco.Deshechos);
    }

    [Fact]
    public async Task Si_la_captura_falla_el_cobro_se_ANULA_y_nada_se_consumio()
    {
        var banco = new Banco();
        await banco.Abrir("a");
        banco.Fallan.Add("t.capturar");

        var r = await banco.Cerrar();

        Assert.False(r.IsOk);
        Assert.DoesNotContain(banco.Bitacora, l => l.StartsWith("paso t.consumir", StringComparison.Ordinal));
        Assert.Equal(new[] { "undo Soltar:h-a (no capturó)", "undo Anular:cobro-1 (no capturó)" }, banco.Deshechos);
    }

    [Fact]
    public async Task Cerrar_una_reserva_unica_que_nunca_se_hizo_no_llama_a_nadie()
    {
        // Una saga que se cayó entre apartar y autorizar quedó Running sin cobro: confirmarla
        // consume lo apartado y no captura nada, que es el `if (saga.PaymentId is { })` de antes.
        var banco = new Banco();
        banco.Sagas.Dejar(SagaDePrueba.Nueva("s1", banco.Reloj.GetUtcNow()) with
        {
            Compensations = new[] { Compensation.For("Soltar", "h-a", "sin cerrar") },
            Reservado = new Dictionary<string, IReadOnlyList<HoldLeg>> { ["apartar"] = new[] { new HoldLeg("h-a", "pozo-a") } },
        });

        var r = await banco.Cerrar();

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Completed, r.Value.Status);
        Assert.DoesNotContain(banco.Bitacora, l => l.StartsWith("paso t.capturar", StringComparison.Ordinal));
        Assert.Contains("paso t.consumir h-a", banco.Bitacora);
    }

    [Fact]
    public async Task Una_reserva_sin_despues_queda_HECHA_al_consumirse_y_no_se_deshace()
    {
        // Salud aparta una hora y confirmarla es el último paso: no hay nada que deshacer después.
        var banco = new Banco(Definicion.Replace("\"despues\": \"Reponer\", ", string.Empty, StringComparison.Ordinal));
        Assert.Null(banco.Flujo.Pasos["apartar"].Reserva!.Despues);   // la mutación del banco entró

        await banco.Abrir("a", "b");
        banco.Fallan.Add("t.consumir@h-b");

        await banco.Cerrar();

        var saga = banco.Sagas.Find("s1")!;
        var primera = saga.Compensations.Single(c => c.TargetId == "h-a");
        Assert.Equal("Soltar", primera.Kind);
        Assert.False(primera.IsPending);
        Assert.Equal(new[] { "undo Soltar:h-b (no consumió)", "undo Devolver:cobro-1 (no consumió)" }, banco.Deshechos);
    }

    [Fact]
    public async Task Un_cierre_que_produce_su_propio_objetivo_deja_la_compensacion_de_despues_sobre_ESE()
    {
        // Viajes: confirmar un apartado devuelve una reserva, y cancelarla es sobre la reserva, no
        // sobre el recurso que se anotó al apartar.
        var banco = new Banco { ProducenCierre = true };
        await banco.Abrir("a", "b");
        banco.Fallan.Add("t.consumir@h-b");

        await banco.Cerrar();

        Assert.Equal(new[]
        {
            "undo Reponer:res-h-a (no consumió)",
            "undo Soltar:h-b (no consumió)",
            "undo Devolver:cobro-1 (no consumió)",
        }, banco.Deshechos);
    }

    // ── Dos reservas únicas ─────────────────────────────────────────────────

    [Fact]
    public async Task Dos_reservas_unicas_se_anotan_cierran_y_completan_cada_una_por_su_lado()
    {
        var banco = new Banco(DosUnicas);

        var abierta = await banco.Abrir();
        Assert.True(abierta.IsOk);
        Assert.Equal(new[] { new HoldLeg("pedido-1", "pedido-1") }, abierta.Value.Legs("pedir"));
        Assert.Equal(new[] { new HoldLeg("cobro-1", "cobro-1") }, abierta.Value.Legs("autorizar"));
        banco.Bitacora.Clear();

        var cerrada = await banco.Cerrar();

        Assert.True(cerrada.IsOk);
        Assert.Equal(new[]
        {
            // Capturar cambia SÓLO la del cobro; confirmar marca hecha la del pedido, que no tiene
            // «despues». Las dos leen lo suyo de lo que la saga guardó.
            "paso t.capturar cobro-1",
            "put Running [Cancelar:pedido-1, Devolver:cobro-1]",
            "paso t.confirmar pedido-1",
            "put Running [Cancelar:pedido-1+, Devolver:cobro-1]",
            "put Completed [Cancelar:pedido-1+, Devolver:cobro-1+]",
        }, banco.Bitacora);
    }

    [Fact]
    public async Task Dos_reservas_unicas_se_DESHACEN_las_dos_si_la_segunda_falla_al_cerrar()
    {
        var banco = new Banco(DosUnicas);
        await banco.Abrir();
        banco.Fallan.Add("t.confirmar");

        var r = await banco.Cerrar();

        Assert.False(r.IsOk);
        Assert.Equal("t.confirmar.no", r.Rejection!.Code);
        Assert.Equal(new[] { "undo Cancelar:pedido-1 (no confirmó)", "undo Devolver:cobro-1 (no confirmó)" }, banco.Deshechos);
    }

    [Fact]
    public async Task Cerrar_una_reserva_unica_no_toca_otra_que_se_deshaga_igual()
    {
        // Dos únicas con el mismo «antes» —dos autorizaciones— no son la misma reserva: capturar
        // una no puede convertir la otra en devolución, y menos sobre el cobro equivocado.
        var banco = new Banco(DosUnicas.Replace("\"antes\": \"Cancelar\"", "\"antes\": \"Anular\"", StringComparison.Ordinal));
        Assert.Equal("Anular", banco.Flujo.Pasos["pedir"].Reserva!.Antes);   // la mutación del banco entró
        await banco.Abrir();
        banco.Fallan.Add("t.confirmar");

        await banco.Cerrar();

        Assert.Equal(new[] { "undo Anular:pedido-1 (no confirmó)", "undo Devolver:cobro-1 (no confirmó)" }, banco.Deshechos);
    }

    [Fact]
    public async Task Dos_reservas_unicas_si_la_segunda_no_reserva_se_deshace_la_primera()
    {
        var banco = new Banco(DosUnicas);
        banco.Fallan.Add("t.autorizar");

        var r = await banco.Abrir();

        Assert.False(r.IsOk);
        Assert.Equal(new[] { "undo Cancelar:pedido-1 (no autorizó)" }, banco.Deshechos);
        Assert.Equal(SagaStatus.Compensated, banco.Sagas.Find("s1")!.Status);
    }

    // ── Lo que el intérprete no hace ────────────────────────────────────────

    [Theory]
    [InlineData(SagaStatus.Completed)]
    [InlineData(SagaStatus.Compensated)]
    [InlineData(SagaStatus.CompensationFailed)]
    public async Task Solo_continua_una_saga_en_curso(SagaStatus estado)
    {
        // Las guardas de la fachada contestan con los códigos de su dominio; ésta es la red para
        // la que se las olvide. Continuar una saga completada volvería a consumir lo consumido.
        var banco = new Banco();
        await banco.Abrir("a");
        banco.Sagas.Dejar(banco.Sagas.Find("s1")!.WithStatus(estado));
        banco.Bitacora.Clear();

        var r = await banco.Cerrar();

        Assert.False(r.IsOk);
        Assert.Equal(FlowRunner<SagaDePrueba>.NoEnCurso, r.Rejection!.Code);
        Assert.Empty(banco.Bitacora);
    }

    [Fact]
    public async Task Un_binding_que_no_reconstruye_lo_que_declara_falla_ANTES_de_llamar_a_nadie()
    {
        // El validador aceptó las lecturas de «cerrar» contando con lo que el binding dice que
        // reconstruye. Si no lo pone, mejor saberlo antes de capturar que en el paso que lo lea.
        var banco = new Banco(binding: new BindingDePrueba { Olvida = "pedido" });
        await banco.Abrir("a");
        banco.Bitacora.Clear();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(banco.Cerrar);

        Assert.Contains("reconstruye pedido", ex.Message, StringComparison.Ordinal);
        Assert.Empty(banco.Bitacora);
    }

    [Fact]
    public async Task Una_fachada_que_no_pone_lo_que_declara_falla_ANTES_de_llamar_a_nadie()
    {
        // El validador aceptó la «entrada» contando con lo que el binding dice que se pone al abrir.
        // Si la fachada no lo pone, el paso que lo lea lanzaría con lo anterior ya hecho.
        var banco = new Banco(binding: new BindingDePrueba { PoneDeMas = "moneda" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => banco.Abrir("a"));

        Assert.Contains("pone moneda al abrir", ex.Message, StringComparison.Ordinal);
        Assert.Empty(banco.Bitacora);
    }

    [Fact]
    public async Task Una_reserva_que_la_saga_no_sabe_leer_falla_al_NACER_antes_de_reservar()
    {
        // Sin la comprobación al sembrar, la saga no sabría leerla recién al cerrarla: después de
        // capturar, con la compensación sin cambiar de carácter.
        var banco = new Banco(binding: new BindingDePrueba { ReservaDeMas = "fantasma" });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => banco.Abrir("a"));

        Assert.Contains("«fantasma»", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(banco.Pasos, p => p.Contains("apartar", StringComparison.Ordinal));
        Assert.Empty(banco.Escrituras);
    }

    [Fact]
    public void Una_definicion_que_no_valida_no_construye_el_interprete()
    {
        var banco = new Banco();
        var rota = FlujoDef.Leer(Definicion.Replace("\"t.consumir\"", "\"t.entregar\"", StringComparison.Ordinal));

        var ex = Assert.Throws<InvalidOperationException>(() => new FlowRunner<SagaDePrueba>(
            banco.Motor, banco.Registro, rota, new BindingDePrueba(), Fases, banco.Reloj, NullLogger.Instance));

        Assert.Contains("«t.entregar»", ex.Message, StringComparison.Ordinal);
    }

    // ── al_fallar: seguir, y lo efímero (ADR 0140 F3) ──────────────────────

    private static Banco ConAvisoAlFinal() => new(ConAviso, new BindingDePrueba { Efimera = NotaEnCerrar });

    [Fact]
    public async Task Un_paso_con_al_fallar_seguir_que_rechaza_se_anota_y_la_saga_se_completa_sin_deshacer()
    {
        // Es el aviso que no salió: la compra está cobrada y consumida, y deshacerla por eso
        // devolvería la plata de una compra que sí se hizo.
        var banco = ConAvisoAlFinal();
        Assert.True(banco.Flujo.Pasos["avisar"].SigueSiFalla);   // la definición del banco entró
        await banco.Abrir("a");
        banco.Fallan.Add("t.avisar");

        var r = await banco.Cerrar("ana@ejemplo.co");

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Completed, r.Value.Status);
        Assert.Contains("paso t.avisar ana@ejemplo.co", banco.Pasos);
        Assert.Empty(banco.Deshechos);
        Assert.All(r.Value.Compensations, c => Assert.False(c.IsPending));
        Assert.Null(r.Value.Falla);
        Assert.Contains(banco.Log.Lineas, l => l.Contains("t.avisar.no", StringComparison.Ordinal)
                                              && l.Contains("s1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Un_paso_con_al_fallar_seguir_que_LANZA_tampoco_deshace_ni_escribe_lo_efimero_en_el_log()
    {
        var banco = ConAvisoAlFinal();
        await banco.Abrir("a");
        banco.AvisarLanza = new HttpRequestException("no se pudo avisar a ana@ejemplo.co");

        var r = await banco.Cerrar("ana@ejemplo.co");

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Completed, r.Value.Status);
        Assert.Empty(banco.Deshechos);
        Assert.Contains(banco.Log.Lineas, l => l.Contains(nameof(HttpRequestException), StringComparison.Ordinal));
        Assert.DoesNotContain(banco.Log.Lineas, l => l.Contains("ana@ejemplo.co", StringComparison.Ordinal));
    }

    [Fact]
    public async Task La_cancelacion_de_quien_llama_no_se_traga_como_un_fallo_del_paso()
    {
        // Seguir es para el paso que falla, no para la petición que se cancela.
        var banco = ConAvisoAlFinal();
        await banco.Abrir("a");
        banco.AvisarLanza = new OperationCanceledException();
        using var cancelada = new CancellationTokenSource();
        await cancelada.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => banco.Cerrar("ana@ejemplo.co", cancelada.Token));

        // Control: la misma excepción SIN que nadie haya cancelado es un fallo del paso, y se sigue.
        var otro = ConAvisoAlFinal();
        await otro.Abrir("a");
        otro.AvisarLanza = new OperationCanceledException();
        Assert.True((await otro.Cerrar("ana@ejemplo.co")).IsOk);
    }

    [Fact]
    public async Task Lo_efimero_lo_lee_su_fase_y_no_llega_a_la_saga()
    {
        var banco = ConAvisoAlFinal();
        await banco.Abrir("a");

        var r = await banco.Cerrar("dato-efimero-1f3");

        Assert.True(r.IsOk);
        Assert.Contains("paso t.avisar dato-efimero-1f3", banco.Pasos);
        Assert.DoesNotContain("dato-efimero-1f3", JsonSerializer.Serialize(banco.Sagas.Find("s1")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Una_fachada_que_no_pone_lo_efimero_que_declara_falla_ANTES_de_llamar_a_nadie()
    {
        var banco = ConAvisoAlFinal();
        await banco.Abrir("a");
        banco.Bitacora.Clear();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(banco.Cerrar);

        Assert.Contains("pone nota en «cerrar»", ex.Message, StringComparison.Ordinal);
        Assert.Empty(banco.Bitacora);
    }

    // ── omitir_si_cero (ADR 0140 F3) ────────────────────────────────────────

    [Fact]
    public async Task Con_monto_cero_la_reserva_que_lo_omite_no_corre_y_su_cierre_se_salta_solo()
    {
        // Lo gratis: sin esto, Api.Payments rechaza el cobro de cero y la compra se deshace.
        var banco = new Banco(GratisSiCero);
        Assert.Equal("precio", banco.Flujo.Pasos["autorizar"].OmitirSiCero);   // la definición del banco entró

        var abierta = await banco.AbrirPor(0m, "a");

        Assert.True(abierta.IsOk);
        Assert.DoesNotContain(banco.Pasos, p => p.StartsWith("paso t.autorizar", StringComparison.Ordinal));
        Assert.Empty(abierta.Value.Legs("autorizar"));
        Assert.Equal(new[] { "Soltar" }, abierta.Value.Compensations.Select(c => c.Kind));
        banco.Bitacora.Clear();

        var cerrada = await banco.Cerrar();

        Assert.True(cerrada.IsOk);
        Assert.Equal(SagaStatus.Completed, cerrada.Value.Status);
        Assert.DoesNotContain(banco.Pasos, p => p.StartsWith("paso t.capturar", StringComparison.Ordinal));
        Assert.Contains("paso t.consumir h-a", banco.Pasos);
        Assert.Empty(banco.Deshechos);
        Assert.All(cerrada.Value.Compensations, c => Assert.False(c.IsPending));
    }

    [Fact]
    public async Task Con_monto_la_misma_definicion_autoriza_y_captura_como_siempre()
    {
        var banco = new Banco(GratisSiCero);

        await banco.AbrirPor(10m, "a");
        var cerrada = await banco.Cerrar();

        Assert.True(cerrada.IsOk);
        Assert.Contains("paso t.autorizar", banco.Pasos);
        Assert.Contains("paso t.capturar cobro-1", banco.Pasos);
    }

    // ── Una saga, un turno (ADR 0140 F3) ────────────────────────────────────

    [Fact]
    public async Task Una_fase_no_se_intercala_con_otra_fase_ni_con_la_compensacion_de_su_saga()
    {
        // Lo que la verificación de la F3 midió en Eventos, sin dominio: a media fase —ya capturado y
        // consumido— llegan una compensación y otra fase de la misma saga. Sin turno, la compensación
        // devolvía todo y la fase escribía Completed encima.
        var banco = ConAvisoAlFinal();
        await banco.Abrir("a");
        Result<SagaDePrueba>? deshacer = null, otraFase = null;
        banco.AlAvisar = () =>
        {
            deshacer = banco.Motor.CompensateAsync("s1", "cancelada", CancellationToken.None).GetAwaiter().GetResult();
            otraFase = banco.Cerrar("otra").GetAwaiter().GetResult();
        };

        var r = await banco.Cerrar("nota");

        Assert.True(r.IsOk);
        Assert.Equal(SagaStatus.Completed, banco.Sagas.Find("s1")!.Status);
        Assert.Empty(banco.Deshechos);
        Assert.Equal("prueba.compensation_in_flight", deshacer!.Value.Rejection!.Code);
        Assert.True(deshacer.Value.Rejection.IsTransient);
        Assert.Equal(FlowRunner<SagaDePrueba>.Ocupada, otraFase!.Value.Rejection!.Code);
        Assert.True(otraFase.Value.Rejection.IsTransient);
        Assert.Single(banco.Pasos, p => p.StartsWith("paso t.capturar", StringComparison.Ordinal));

        // El turno se suelta al terminar: lo que llega después ya no está «en curso», sino tarde.
        var tarde = await banco.Motor.CompensateAsync("s1", "tarde", CancellationToken.None);
        Assert.Equal("prueba.already_completed", tarde.Rejection!.Code);
    }

    [Fact]
    public async Task Una_fase_que_abre_no_pisa_la_saga_que_otra_llamada_con_la_misma_llave_ya_abrio()
    {
        // Dos «abrir» con la misma llave resolvieron Abrir antes de que ninguno escribiera; el que
        // consigue el turno después encuentra la saga escrita y no la reemplaza: quizá ya está cerrada.
        var banco = new Banco();
        await banco.Abrir("a");
        await banco.Cerrar();
        banco.Bitacora.Clear();

        var r = await banco.Abrir("b");

        Assert.Equal(FlowRunner<SagaDePrueba>.Ocupada, r.Rejection!.Code);
        Assert.Empty(banco.Bitacora);
        Assert.Equal(SagaStatus.Completed, banco.Sagas.Find("s1")!.Status);
    }

    [Fact]
    public async Task Si_el_turno_vence_a_media_fase_y_otro_deshace_la_saga_completar_no_la_pisa()
    {
        // El arriendo dura más que cualquier fase; ésta es la red para la que tarde más: el barrido le
        // roba el turno vencido y deshace, y la fase que vuelve NO escribe Completed sobre lo deshecho.
        var reloj = new RelojQueAvanza();
        var banco = new Banco(ConAviso, new BindingDePrueba { Efimera = NotaEnCerrar },
            ArriendoDePrueba.Sobre(Path.Combine(Path.GetTempPath(), "syn-turno-" + Guid.NewGuid().ToString("n")), reloj, segundos: 30));
        await banco.Abrir("a");
        Result<SagaDePrueba>? deshacer = null;
        banco.AlAvisar = () =>
        {
            reloj.Avanzar(TimeSpan.FromSeconds(31));
            deshacer = banco.Motor.CompensateAsync("s1", "abandonada", CancellationToken.None).GetAwaiter().GetResult();
        };

        var r = await banco.Cerrar("nota");

        Assert.True(deshacer!.Value.IsOk);                      // el turno vencido se lo llevó el barrido
        Assert.Equal(FlowRunner<SagaDePrueba>.NoEnCurso, r.Rejection!.Code);
        Assert.Equal(SagaStatus.Compensated, banco.Sagas.Find("s1")!.Status);
        Assert.Contains(banco.Log.Lineas, l => l.Contains("no se completa", StringComparison.Ordinal));
    }

    private sealed class RelojQueAvanza : TimeProvider
    {
        private DateTimeOffset _ahora = DateTimeOffset.UtcNow;

        public void Avanzar(TimeSpan cuanto) => _ahora += cuanto;

        public override DateTimeOffset GetUtcNow() => _ahora;
    }
}
