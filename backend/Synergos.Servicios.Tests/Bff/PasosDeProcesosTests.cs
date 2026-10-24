using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Bff.Pasos.Http;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Los pasos comunes de la capacidad de procesos (núcleo, lote 7; 009-P2 = A): arrancar, disparar y leer, contra
/// <c>Api.Workflow</c> REAL en proceso, con el adaptador común que censa su contrato.
/// </summary>
/// <remarks>
/// <para><b>El proceso de la muestra</b> tiene la forma del trámite de 009 (tomar antes de decidir, subsanar vuelve a
/// revisión, un rol por transición) con nombres propios: estas pruebas no dependen de la definición de un vertical. Y
/// hay otro proceso con una transición del mismo nombre desde el mismo estado, que es lo que haría equivocarse a un
/// paso que no mira de qué proceso es la instancia.</para>
/// </remarks>
public sealed class PasosDeProcesosTests : IAsyncLifetime
{
    private const string Proceso = "muestra.tramite";
    private const string Otro = "muestra.otro";
    private static readonly Ref Funcionaria = Ref.Create("muestra.funcionario", "f-1");
    private static readonly Ref Ciudadana = Ref.Create("muestra.ciudadano", "c-1");

    private readonly ArnesDeCapacidades _capacidades = new ArnesDeCapacidades()
        .Levanta<Synergos.Api.Workflow.Contracts.InstanceResponse>("workflow");

    private readonly PuertosHttp _puertos;

    public PasosDeProcesosTests() => _puertos = new PuertosHttp(_capacidades);

    public async Task InitializeAsync()
    {
        await Definir(new
        {
            key = Proceso, initialState = "radicado", finalStates = new[] { "aprobado", "rechazado" },
            transitions = new object[]
            {
                new { name = "tomar", from = "radicado", to = "en-revision", requiredRoles = new[] { "funcionario" } },
                new { name = "aprobar", from = "en-revision", to = "aprobado", requiredRoles = new[] { "funcionario" } },
                new { name = "rechazar", from = "en-revision", to = "rechazado", requiredRoles = new[] { "funcionario" } },
                new { name = "pedir-informacion", from = "en-revision", to = "informacion-pedida", requiredRoles = new[] { "funcionario" } },
                new { name = "subsanar", from = "informacion-pedida", to = "en-revision", requiredRoles = new[] { "ciudadano" } },
            },
        }, "def-tramite");
        await Definir(new
        {
            key = Otro, initialState = "radicado", finalStates = new[] { "cerrado" },
            transitions = new object[] { new { name = "tomar", from = "radicado", to = "cerrado", requiredRoles = new[] { "funcionario" } } },
        }, "def-otro");
    }

    public Task DisposeAsync()
    {
        _capacidades.Dispose();
        return Task.CompletedTask;
    }

    private async Task Definir(object definicion, string llave)
    {
        using var http = _capacidades.CreateClient("workflow");
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/definitions") { Content = JsonContent.Create(definicion) };
        peticion.Headers.Add("Idempotency-Key", llave);
        using var r = await http.SendAsync(peticion);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }

    private async Task<JsonElement> Instancia(string id)
    {
        using var http = _capacidades.CreateClient("workflow");
        return await http.GetFromJsonAsync<JsonElement>($"v1/instances/{id}");
    }

    /// <summary>La entrada de un paso como la arma el intérprete, con lo que ya tiene el contexto.</summary>
    private static EntradaDePaso Entrada(string saga, PasoDef paso, params (string Nombre, object? Valor)[] contexto)
    {
        var ctx = new FlowContext();
        foreach (var (nombre, valor) in contexto) ctx.Set(nombre, valor);
        return new EntradaDePaso(saga, Ref.Create("muestra.tramite", saga), paso, ctx, null, null);
    }

    private static PasoDef Arrancar(string proceso = Proceso)
        => new("arrancar", "workflow.arrancar", [], ["expediente", "estado"], "start", null, null, null, null, Proceso: proceso);

    private static PasoDef Disparar(string transicion, string rol = "funcionario", bool conNota = true, string proceso = Proceso)
        => new("disparar", "workflow.disparar", conNota ? ["expediente", "actor", "nota"] : ["expediente", "actor"], ["estado"],
            "fire", null, null, null, null, Proceso: proceso, Transicion: transicion, Rol: rol);

    private static PasoDef Elegir(params string[] transiciones)
        => new("decidir", "workflow.disparar-elegida", ["expediente", "actor", "decision", "nota"], ["estado"],
            "fire", null, null, null, null, Proceso: Proceso, Transiciones: transiciones, Rol: "funcionario");

    private static PasoDef Leer(string proceso = Proceso)
        => new("leer", "workflow.leer", ["expediente"], ["estado"], null, null, null, null, null, Proceso: proceso);

    private async Task<string> Radicar(string saga, string proceso = Proceso)
    {
        var salida = await new PasoProcesoArrancar(_puertos).EjecutarAsync(Entrada(saga, Arrancar(proceso)), CancellationToken.None);
        Assert.True(salida.Control == PasoResultado.Continuar, salida.Rechazo?.Code);
        return (string)salida.Valores[0]!;
    }

    [Fact]
    public async Task Arrancar_abre_una_instancia_del_proceso_para_la_saga_y_repetirla_devuelve_la_misma()
    {
        var paso = new PasoProcesoArrancar(_puertos);

        var primera = await paso.EjecutarAsync(Entrada("saga-1", Arrancar()), CancellationToken.None);
        var repetida = await paso.EjecutarAsync(Entrada("saga-1", Arrancar()), CancellationToken.None);
        var otra = await paso.EjecutarAsync(Entrada("saga-2", Arrancar()), CancellationToken.None);

        Assert.Equal(PasoResultado.Continuar, primera.Control);
        Assert.Equal("radicado", primera.Valores[1]);
        var id = (string)primera.Valores[0]!;
        Assert.Equal(id, repetida.Valores[0]);
        Assert.NotEqual(id, otra.Valores[0]);

        var guardada = await Instancia(id);
        Assert.Equal(Proceso, guardada.GetProperty("definitionKey").GetString());
        Assert.Equal("muestra.tramite", guardada.GetProperty("subjectKind").GetString());
        Assert.Equal("saga-1", guardada.GetProperty("subjectId").GetString());
    }

    [Fact]
    public async Task Disparar_lleva_la_instancia_por_su_transicion_con_el_rol_del_paso_y_repetir_no_dispara_otra_vez()
    {
        var id = await Radicar("saga-1");
        var paso = new PasoProcesoDisparar(_puertos);
        var tomar = Entrada("acto-1", Disparar("tomar"), ("expediente", id), ("actor", Funcionaria), ("nota", "La reviso yo."));

        var tomada = await paso.EjecutarAsync(tomar, CancellationToken.None);
        var repetida = await paso.EjecutarAsync(tomar, CancellationToken.None);

        Assert.Equal("en-revision", Assert.Single(tomada.Valores));
        Assert.Equal("en-revision", Assert.Single(repetida.Valores));
        var historia = (await Instancia(id)).GetProperty("history").EnumerateArray().ToList();
        var unica = Assert.Single(historia);
        Assert.Equal("tomar", unica.GetProperty("transition").GetString());
        Assert.Equal("f-1", unica.GetProperty("byId").GetString());
        Assert.Equal("La reviso yo.", unica.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Sin_nota_dispara_igual_y_la_historia_no_inventa_una()
    {
        var id = await Radicar("saga-1");

        var tomada = await new PasoProcesoDisparar(_puertos).EjecutarAsync(
            Entrada("acto-1", Disparar("tomar", conNota: false), ("expediente", id), ("actor", Funcionaria)), CancellationToken.None);

        Assert.Equal("en-revision", Assert.Single(tomada.Valores));
        Assert.Equal(JsonValueKind.Null, Assert.Single((await Instancia(id)).GetProperty("history").EnumerateArray()).GetProperty("note").ValueKind);
    }

    [Fact]
    public async Task El_rechazo_de_la_capacidad_sube_tal_cual()
    {
        var id = await Radicar("saga-1");
        var paso = new PasoProcesoDisparar(_puertos);

        var antesDeTomar = await paso.EjecutarAsync(
            Entrada("acto-1", Disparar("aprobar"), ("expediente", id), ("actor", Funcionaria), ("nota", null)), CancellationToken.None);
        var sinElRol = await paso.EjecutarAsync(
            Entrada("acto-2", Disparar("tomar", rol: "ciudadano"), ("expediente", id), ("actor", Ciudadana), ("nota", null)), CancellationToken.None);

        Assert.Equal(PasoResultado.Abortar, antesDeTomar.Control);
        Assert.Equal("workflow.transition_not_allowed", antesDeTomar.Rechazo!.Code);
        Assert.Equal(PasoResultado.Abortar, sinElRol.Control);
        Assert.Equal("workflow.role_required", sinElRol.Rechazo!.Code);
        Assert.Equal("radicado", (await Instancia(id)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task Una_instancia_de_otro_proceso_ni_se_dispara_ni_se_lee()
    {
        var ajena = await Radicar("saga-1", Otro);

        var disparo = await new PasoProcesoDisparar(_puertos).EjecutarAsync(
            Entrada("acto-1", Disparar("tomar"), ("expediente", ajena), ("actor", Funcionaria), ("nota", null)), CancellationToken.None);
        var lectura = await new PasoProcesoLeer(_puertos).EjecutarAsync(Entrada("acto-2", Leer(), ("expediente", ajena)), CancellationToken.None);

        Assert.Equal(PasosDeProcesos.ProcesoAjeno, disparo.Rechazo?.Code);
        Assert.Equal(PasosDeProcesos.ProcesoAjeno, lectura.Rechazo?.Code);
        Assert.Equal("radicado", (await Instancia(ajena)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task La_elegida_dispara_si_esta_en_la_lista_y_si_no_ni_llega_a_la_capacidad()
    {
        var id = await Radicar("saga-1");
        await new PasoProcesoDisparar(_puertos).EjecutarAsync(
            Entrada("acto-1", Disparar("tomar"), ("expediente", id), ("actor", Funcionaria), ("nota", null)), CancellationToken.None);
        var paso = new PasoProcesoDispararElegida(_puertos);

        var fuera = await paso.EjecutarAsync(Entrada("acto-2", Elegir("aprobar", "rechazar"),
            ("expediente", id), ("actor", Funcionaria), ("decision", "pedir-informacion"), ("nota", null)), CancellationToken.None);
        Assert.Equal(PasosDeProcesos.TransicionNoAdmitida, fuera.Rechazo?.Code);
        Assert.Equal("en-revision", (await Instancia(id)).GetProperty("state").GetString());

        var dentro = await paso.EjecutarAsync(Entrada("acto-3", Elegir("aprobar", "rechazar"),
            ("expediente", id), ("actor", Funcionaria), ("decision", "rechazar"), ("nota", "No cumple.")), CancellationToken.None);
        Assert.Equal("rechazado", Assert.Single(dentro.Valores));
    }

    [Fact]
    public async Task Leer_escribe_el_estado_en_que_esta()
    {
        var id = await Radicar("saga-1");

        var leida = await new PasoProcesoLeer(_puertos).EjecutarAsync(Entrada("acto-1", Leer(), ("expediente", id)), CancellationToken.None);
        var noExiste = await new PasoProcesoLeer(_puertos).EjecutarAsync(Entrada("acto-2", Leer(), ("expediente", "no-existe")), CancellationToken.None);

        Assert.Equal("radicado", Assert.Single(leida.Valores));
        Assert.Equal("workflow.instance_not_found", noExiste.Rechazo?.Code);
    }
}

/// <summary>
/// Lo que el validador exige de un paso de procesos al arrancar (núcleo, lote 7): sus datos (009-P3 = A), la nota que se
/// puede no nombrar y la regla de forma de lo que no tiene vuelta (ADR 0140).
/// </summary>
public sealed class PasosDeProcesosEnLaDefinicionTests
{
    private sealed class SinRed : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Validar no llama a nadie.");
    }

    /// <summary>Radicar: se autoriza el cobro, y al cerrar, con el cobro capturado, nace el expediente. Lo reversible, antes.</summary>
    private const string Radicar = """
        {
          "clave": "muestra.tramite",
          "entrada": ["ciudadano", "total"],
          "tipos": { "ciudadano": { "de": "sujeto" }, "total": "dinero" },
          "salida": ["expediente"],
          "fases": { "abrir": ["autorizar"], "cerrar": ["capturar", "arrancar"] },
          "pasos": {
            "autorizar": {
              "tipo": "payments.autorizar", "lee": ["ciudadano", "total"], "escribe": ["cobroId"], "llave": "authorize",
              "reserva": { "consumado_por": "capturar", "antes": "payments.anular", "despues": "payments.devolver" }
            },
            "capturar": { "tipo": "payments.capturar", "lee": ["cobroId"], "llave": "capture", "cierra_reserva": "autorizar" },
            "arrancar": { "tipo": "workflow.arrancar", "escribe": ["expediente", "estado"], "llave": "start", "proceso": "muestra.tramite" }
          }
        }
        """;

    /// <summary>Decidir: quien atiende elige entre aprobar y rechazar, con su nota.</summary>
    private const string Decidir = """
        {
          "clave": "muestra.decision",
          "entrada": ["funcionario", "expediente", "decision", "nota"],
          "tipos": { "funcionario": { "de": "sujeto" }, "expediente": "texto", "decision": "texto", "nota": "texto?" },
          "salida": ["estado"],
          "fases": { "decidir": ["decidir"] },
          "pasos": {
            "decidir": {
              "tipo": "workflow.disparar-elegida", "lee": ["expediente", "funcionario", "decision", "nota"], "escribe": ["estado"],
              "llave": "fire", "proceso": "muestra.tramite", "transiciones": ["aprobar", "rechazar"], "rol": "funcionario"
            }
          }
        }
        """;

    private static IReadOnlyList<string> Errores(string json)
    {
        var flujo = FlujoDef.Leer(json);
        var puertos = new PuertosHttp(new SinRed());
        var registro = PasosComunes.Registro(puertos);
        var binding = new BindingDeFlujo(flujo, TiposDeValor.Con(new TipoContacto(), new TipoDireccion(), new TipoFranjas()),
            registro, new DeshacerComun(puertos, puertos, puertos, puertos, []).Tipos);
        return FlowValidator.Validar(flujo, registro, ContratoDelFlujo.De(binding, flujo.Fases.Select(f => f.Nombre).ToList()));
    }

    private static string Mutar(string json, string antes, string despues)
    {
        Assert.Contains(antes, json, StringComparison.Ordinal);
        return json.Replace(antes, despues, StringComparison.Ordinal);
    }

    private static void Dice(string json, string mensaje)
        => Assert.Contains(Errores(json), e => e.Contains(mensaje, StringComparison.Ordinal));

    private static void Arranca(string json)
    {
        var errores = Errores(json);
        Assert.True(errores.Count == 0, string.Join(Environment.NewLine, errores));
    }

    [Fact]
    public void Los_dos_flujos_de_la_muestra_arrancan()
    {
        Arranca(Radicar);
        Arranca(Decidir);
    }

    [Fact]
    public void Cada_dato_del_proceso_se_cruza_con_el_paso_en_los_dos_sentidos()
    {
        Dice(Mutar(Radicar, Q(", 'proceso': 'muestra.tramite' }"), " }"),
            "«arrancar» es de tipo «workflow.arrancar», que necesita «proceso», y no lo declara.");
        Dice(Mutar(Radicar, Q("'cierra_reserva': 'autorizar' }"), Q("'cierra_reserva': 'autorizar', 'proceso': 'muestra.tramite' }")),
            "«capturar» declara «proceso» y «payments.capturar» no lo usa");
        Dice(Mutar(Decidir, Q(", 'rol': 'funcionario'"), ""),
            "«decidir» es de tipo «workflow.disparar-elegida», que necesita «rol», y no lo declara.");

        var conLaFija = Mutar(Decidir, Q("'transiciones': ['aprobar', 'rechazar']"), Q("'transicion': 'aprobar'"));
        Dice(conLaFija, "«decidir» es de tipo «workflow.disparar-elegida», que necesita «transiciones», y no lo declara.");
        Dice(conLaFija, "«decidir» declara «transicion» y «workflow.disparar-elegida» no lo usa");
    }

    [Fact]
    public void La_lista_cerrada_no_va_vacia_ni_con_un_nombre_dos_veces()
    {
        Dice(Mutar(Decidir, Q("['aprobar', 'rechazar']"), "[]"), "«decidir» declara «transiciones» vacía");
        Dice(Mutar(Decidir, Q("['aprobar', 'rechazar']"), Q("['aprobar', 'aprobar']")), "«decidir» nombra «aprobar» dos veces en «transiciones».");
    }

    [Fact]
    public void La_nota_se_puede_no_nombrar_y_lo_demas_no()
    {
        var sinNota = Mutar(Mutar(Mutar(Decidir, Q("'decision', 'nota'], 'escribe'"), Q("'decision'], 'escribe'")),
            Q("'entrada': ['funcionario', 'expediente', 'decision', 'nota']"), Q("'entrada': ['funcionario', 'expediente', 'decision']")),
            Q(", 'nota': 'texto?' }"), " }");
        Arranca(sinNota);
        Dice(Mutar(Decidir, Q("'funcionario', 'decision', 'nota'], 'escribe'"), Q("'funcionario'], 'escribe'")),
            "«decidir» declara 2 lectura(s) y «workflow.disparar-elegida» lee 4, de las que las últimas 1 se pueden no nombrar.");
    }

    [Fact]
    public void Lo_que_no_tiene_vuelta_va_detras_del_ultimo_cierre_y_detras_de_el_solo_lo_que_sigue_si_falla()
    {
        Dice(Mutar(Radicar, Q("['capturar', 'arrancar']"), Q("['arrancar', 'capturar']")),
            "«arrancar» (workflow.arrancar) no tiene vuelta y va antes del último cierre de «cerrar»");

        var conUnaLectura = Mutar(Mutar(Radicar, Q("'capturar', 'arrancar']"), Q("'capturar', 'arrancar', 'leer']")),
            Q("'proceso': 'muestra.tramite' }"),
            Q("'proceso': 'muestra.tramite' }, 'leer': { 'tipo': 'workflow.leer', 'lee': ['expediente'], 'escribe': ['otro'], 'proceso': 'muestra.tramite' }"));
        Dice(conUnaLectura, "«arrancar» (workflow.arrancar) no tiene vuelta, y detrás van pasos que no siguen si fallan («leer»)");

        var enLaPrimera = Mutar(Radicar, Q("{ 'abrir': ['autorizar'], 'cerrar': ['capturar', 'arrancar'] }"),
            Q("{ 'abrir': ['autorizar', 'arrancar'], 'cerrar': ['capturar'] }"));
        Dice(enLaPrimera, "«arrancar» (workflow.arrancar) no tiene vuelta y no está en la última fase");
    }

    /// <summary>Un trozo de JSON escrito con comillas simples, para no escapar las dobles.</summary>
    private static string Q(string conSimples) => conSimples.Replace('\'', '"');
}
