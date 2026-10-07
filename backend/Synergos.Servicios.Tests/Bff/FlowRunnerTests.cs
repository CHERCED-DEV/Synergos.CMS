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
/// Eventos no ejerce: una reserva sin <c>despues</c>, una fase que se corta antes de reservar.</para>
///
/// <para><b>Todo lo observable va a una sola bitácora</b> —cada paso, cada escritura de la saga y
/// cada deshacer, en el orden en que pasaron—, porque lo que estas reglas fijan es el ORDEN: la
/// compensación anotada en el acto y no al final, el carácter cambiado dentro del bucle y no
/// después.</para>
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
              { "para_cada": "items", "como": "it", "pasos": ["apartar"] },
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
            "apartar": {
              "tipo": "t.apartar", "lee": ["it.sku"], "llave_base": "ap", "motivo": "no se pudo apartar",
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

    // ── El banco: una saga mínima con sus dos ranuras, y pasos guionados ─────

    private sealed record SagaDePrueba(
        string Id,
        SagaStatus Status,
        IReadOnlyList<Compensation> Compensations,
        DateTimeOffset StartedAtUtc,
        IReadOnlyList<HoldLeg> Apartados,
        string? Cargo,
        string? Falla,
        DateTimeOffset? AlertedAtUtc = null,
        int AlertsSent = 0) : ISaga<SagaDePrueba>, IHoldLedger, IChargeLedger
    {
        public SagaDePrueba WithStatus(SagaStatus status) => this with { Status = status };
        public SagaDePrueba WithCompensations(IReadOnlyList<Compensation> compensations) => this with { Compensations = compensations };
        public SagaDePrueba WithAlert(DateTimeOffset? alertedAtUtc, int alertsSent) => this with { AlertedAtUtc = alertedAtUtc, AlertsSent = alertsSent };
        public IReadOnlyList<HoldLeg> Legs => Apartados;
        public string? ChargeRef => Cargo;
    }

    private sealed class BindingDePrueba : IFlowBinding<SagaDePrueba>
    {
        public SagaDePrueba Crear(string sagaId, FlowContext ctx, DateTimeOffset ahora)
            => new(sagaId, SagaStatus.Running, Array.Empty<Compensation>(), ahora, Array.Empty<HoldLeg>(), null, null);

        public FlowContext Leer(SagaDePrueba saga) => new FlowContext().Set("cobro", saga.Cargo);

        public SagaDePrueba ConApartado(SagaDePrueba saga, HoldLeg apartado, FlowContext item)
            => saga with { Apartados = saga.Apartados.Append(apartado).ToList() };

        public SagaDePrueba ConCargo(SagaDePrueba saga, string cargo) => saga with { Cargo = cargo };

        public SagaDePrueba ConError(SagaDePrueba saga, string? falla) => saga with { Falla = falla };
    }

    private sealed class Banco
    {
        public Banco(string definicion = Definicion)
        {
            Sagas = new Almacen(Bitacora);
            var vocabulario = new SagaVocabulary("prueba", "la prueba");
            Motor = new SagaEngine<SagaDePrueba>(
                Sagas,
                new Compensator<SagaDePrueba>(new Deshacer(Bitacora), Reloj, NullLogger<Compensator<SagaDePrueba>>.Instance),
                new CompensationAlert(new SinRed(), vocabulario, Options.Create(new AlertOptions())),
                ArriendoDePrueba.Nuevo(), vocabulario, Reloj, NullLogger<SagaEngine<SagaDePrueba>>.Instance);
            Flujo = FlujoDef.Leer(definicion);
            Registro = new RegistroDePasos(new IPaso[]
            {
                Paso("t.revisar", 1, 0, _ => SalidaDePaso.Sigue()),
                Paso("t.cotizar", 1, 1, e => SalidaDePaso.Sigue(e.Lee<decimal>(0) * 2)),
                Paso("t.apartar", 1, 0, e =>
                {
                    var sku = e.Lee<string>(0);
                    Bitacora.Add($"  llave {e.LlavePara(sku).Value}");
                    return SalidaDePaso.Reserva(new Reservado($"h-{sku}", CierreId: $"pozo-{sku}"));
                }, e => e.Lee<string>(0)),
                Paso("t.autorizar", 1, 1, e =>
                {
                    Bitacora.Add($"  llave {e.Llave().Value}");
                    return SalidaDePaso.Reserva(new Reservado("cobro-1"), "cobro-1");
                }),
                Paso("t.capturar", 1, 0, _ => SalidaDePaso.Sigue(), e => e.Lee<string>(0)),
                Paso("t.consumir", 1, 0, _ => SalidaDePaso.Sigue(), e => e.Lee<string>(0)),
            });
            Runner = new FlowRunner<SagaDePrueba>(Motor, Registro, Flujo, new BindingDePrueba(), Reloj, NullLogger.Instance);
        }

        public List<string> Bitacora { get; } = new();

        /// <summary>Qué pasos fallan: el tipo, o <c>tipo@dato</c> para uno solo de un bloque.</summary>
        public HashSet<string> Fallan { get; } = new(StringComparer.Ordinal);

        /// <summary>Qué pasos cortan la fase en vez de seguir.</summary>
        public HashSet<string> Saltan { get; } = new(StringComparer.Ordinal);

        public RelojFijo Reloj { get; } = new();
        public Almacen Sagas { get; }
        public SagaEngine<SagaDePrueba> Motor { get; }
        public FlujoDef Flujo { get; }
        public RegistroDePasos Registro { get; }
        public FlowRunner<SagaDePrueba> Runner { get; }

        private IPaso Paso(
            string tipo, int lee, int escribe, Func<EntradaDePaso, SalidaDePaso> hace,
            Func<EntradaDePaso, string>? dato = null)
            => new Guionado(tipo, lee, escribe, e =>
            {
                var d = dato?.Invoke(e);
                Bitacora.Add(d is null ? $"paso {tipo}" : $"paso {tipo} {d}");
                if (Fallan.Contains(tipo) || (d is not null && Fallan.Contains($"{tipo}@{d}")))
                {
                    return SalidaDePaso.Rechaza(Rejection.Conflict($"{tipo}.no", "guionado"));
                }
                return Saltan.Contains(tipo) ? SalidaDePaso.SaltaFase(new object?[escribe]) : hace(e);
            });

        public Task<Result<SagaDePrueba>> Abrir(params string[] skus)
            => Runner.EjecutarFaseAsync("abrir", "s1", new FlowContext()
                .Set("items", skus.Select(s => new FlowContext().Set("sku", s)).ToList())
                .Set("monto", 10m), CancellationToken.None);

        public Task<Result<SagaDePrueba>> Cerrar()
            => Runner.EjecutarFaseAsync("cerrar", "s1", new FlowContext(), CancellationToken.None);

        public IEnumerable<string> Escrituras => Bitacora.Where(l => l.StartsWith("put", StringComparison.Ordinal));
        public IEnumerable<string> Deshechos => Bitacora.Where(l => l.StartsWith("undo", StringComparison.Ordinal));
    }

    private sealed class Guionado(string tipo, int lee, int escribe, Func<EntradaDePaso, SalidaDePaso> hace) : IPaso
    {
        public string Tipo => tipo;
        public int Lecturas => lee;
        public int Escrituras => escribe;
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
            "paso t.apartar a",
            "  llave s1|ap:a",
            "put Running [Soltar:h-a]",
            "paso t.apartar b",
            "  llave s1|ap:b",
            "put Running [Soltar:h-a, Soltar:h-b]",
            "paso t.autorizar",
            "  llave s1|auth",
            "put Running [Soltar:h-a, Soltar:h-b, Anular:cobro-1]",
        }, banco.Bitacora);

        // Lo apartado se guarda con su objetivo de DESPUÉS de consumir, que es el pozo.
        Assert.Equal(new[] { new HoldLeg("h-a", "pozo-a"), new HoldLeg("h-b", "pozo-b") }, r.Value.Apartados);
        Assert.Equal("cobro-1", r.Value.Cargo);
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
        banco.Sagas.Dejar(new SagaDePrueba("s1", SagaStatus.Running,
            new[] { Compensation.For("Soltar", "h-a", "sin cerrar") }, banco.Reloj.GetUtcNow(),
            new[] { new HoldLeg("h-a", "pozo-a") }, null, null));

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
    public void Una_definicion_que_no_valida_no_construye_el_interprete()
    {
        var banco = new Banco();
        var rota = FlujoDef.Leer(Definicion.Replace("\"t.consumir\"", "\"t.entregar\"", StringComparison.Ordinal));

        var ex = Assert.Throws<InvalidOperationException>(() => new FlowRunner<SagaDePrueba>(
            banco.Motor, banco.Registro, rota, new BindingDePrueba(), banco.Reloj, NullLogger.Instance));

        Assert.Contains("«t.entregar»", ex.Message, StringComparison.Ordinal);
    }
}
