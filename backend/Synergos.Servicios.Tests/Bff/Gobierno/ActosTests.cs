using System.Net;
using System.Text.Json;
using Synergos.Bff.Core;
using Synergos.CMS.Tests.Bff.Reservas;

namespace Synergos.CMS.Tests.Bff.Gobierno;

/// <summary>
/// Historia 4 de 009-gobierno-tramite (T035): tomar y decidir por la puerta. Cada acto es una operación posterior del
/// personal sobre la radicación completada (núcleo, P6 y P8 del lote 6) que dispara la transición en la capacidad de
/// procesos con los pasos comunes (lote 7). Contra procesos, precios, pagos y avisos reales.
/// </summary>
/// <remarks>
/// La puerta del CMS es la guarda del rol (T037); aquí llega el sujeto que ella pone, el del funcionario, y el
/// orquestador no le aplica la regla de dueño de la radicación.
/// </remarks>
public sealed class ActosTests
{
    private static string Id(JsonElement r) => r.GetProperty("id").GetString()!;

    private static string Estado(JsonElement r) => r.GetProperty("datos").GetProperty("estado").GetString()!;

    private static string Codigo(JsonElement r) => r.GetProperty("code").GetString()!;

    private static string EstadoDe(JsonElement instancia) => instancia.GetProperty("state").GetString()!;

    private static List<JsonElement> Historia(JsonElement instancia) => instancia.GetProperty("history").EnumerateArray().ToList();

    [Fact]
    public async Task Tomar_pasa_el_expediente_a_revision_y_la_historia_nombra_al_funcionario_y_no_al_ciudadano()
    {
        using var r = await new RadicacionReal().ConOferta();
        var (radicacion, expediente) = await r.Radicada();

        var (estado, toma) = await r.Tomar(radicacion, nota: "Lo reviso yo.");

        Assert.True(estado == HttpStatusCode.Created, toma.ToString());
        Assert.Equal("Completed", toma.GetProperty("estado").GetString());
        Assert.Equal("in-review", Estado(toma));
        Assert.Equal(radicacion, toma.GetProperty("datos").GetProperty("radicacion").GetString());
        var instancia = await r.Instancia(expediente);
        Assert.Equal("in-review", EstadoDe(instancia));
        var paso = Assert.Single(Historia(instancia));
        Assert.Equal("take", paso.GetProperty("transition").GetString());
        Assert.Equal("submitted", paso.GetProperty("from").GetString());
        Assert.Equal("gobierno.funcionario", paso.GetProperty("byKind").GetString());
        Assert.Equal("f-1", paso.GetProperty("byId").GetString());
        Assert.Equal("Lo reviso yo.", paso.GetProperty("note").GetString());
    }

    [Theory]
    [InlineData("approve", "approved")]
    [InlineData("reject", "rejected")]
    [InlineData("request-info", "info-requested")]
    public async Task Decidir_lleva_el_expediente_al_estado_del_proceso_con_su_nota_y_su_autor(string decision, string queda)
    {
        using var r = await new RadicacionReal().ConOferta();
        var (radicacion, expediente) = await r.Radicada();
        await r.Tomar(radicacion);

        var (estado, decidida) = await r.Decidir(radicacion, decision, nota: "Revisados los soportes.", funcionario: "f-2");

        Assert.True(estado == HttpStatusCode.Created, decidida.ToString());
        Assert.Equal(queda, Estado(decidida));
        Assert.Equal(decision, decidida.GetProperty("datos").GetProperty("decision").GetString());
        var instancia = await r.Instancia(expediente);
        Assert.Equal(queda, EstadoDe(instancia));
        var ultimo = Historia(instancia)[^1];
        Assert.Equal(decision, ultimo.GetProperty("transition").GetString());
        Assert.Equal("gobierno.funcionario", ultimo.GetProperty("byKind").GetString());
        Assert.Equal("f-2", ultimo.GetProperty("byId").GetString());
        Assert.Equal("Revisados los soportes.", ultimo.GetProperty("note").GetString());
    }

    /// <summary>
    /// La decisión deja elegir aprobar, rechazar o pedir información, y nada más: tomar y subsanar son otros actos, con
    /// su acceso. Una elegida fuera de la lista no llega a la capacidad, aunque el proceso la tenga.
    /// </summary>
    [Theory]
    [InlineData("take")]
    [InlineData("resubmit")]
    public async Task La_decision_solo_deja_elegir_las_de_su_lista_cerrada(string otra)
    {
        using var r = await new RadicacionReal().ConOferta();
        var (radicacion, expediente) = await r.Radicada();
        var antes = r.Disparos;

        var (estado, cuerpo) = await r.Decidir(radicacion, otra);

        Assert.True(estado == HttpStatusCode.BadRequest, $"{(int)estado} {cuerpo}");
        Assert.Equal("flow.transicion_no_admitida", Codigo(cuerpo));
        Assert.Equal("submitted", EstadoDe(await r.Instancia(expediente)));
        Assert.Equal(antes, r.Disparos);
    }

    /// <summary>
    /// US1-2: aprobar, rechazar o pedir información va siempre con una nota, y la definición la declara obligatoria. Una
    /// nota en blanco todavía pasa: el tipo <c>texto</c> del núcleo la admite (NG-15), como la ruta directa.
    /// </summary>
    [Fact]
    public async Task Decidir_sin_la_nota_no_se_admite_y_el_expediente_no_cambia()
    {
        using var r = await new RadicacionReal().ConOferta();
        var (radicacion, expediente) = await r.Radicada();
        await r.Tomar(radicacion);
        var antes = r.Disparos;

        var (estado, cuerpo) = await r.Actuar("gobierno.decision", new { radicacion, decision = "approve" }, "decision-sin-nota");

        Assert.True(estado == HttpStatusCode.BadRequest, $"{(int)estado} {cuerpo}");
        Assert.Equal("in-review", EstadoDe(await r.Instancia(expediente)));
        Assert.Equal(antes, r.Disparos);
    }

    /// <summary>
    /// US4-3: dos funcionarios toman a la vez el mismo expediente. Sólo uno lo cambia; el otro recibe que el expediente
    /// ya cambió, y ninguna de las dos sagas queda abierta.
    /// </summary>
    [Fact]
    public async Task Dos_funcionarios_a_la_vez_una_sola_transicion_y_el_otro_recibe_que_el_expediente_ya_cambio()
    {
        using var r = await new RadicacionReal().ConOferta();
        var (radicacion, expediente) = await r.Radicada();

        var aLaVez = await Task.WhenAll(
            r.Tomar(radicacion, llave: "toma-f1", funcionario: "f-1"),
            r.Tomar(radicacion, llave: "toma-f2", funcionario: "f-2"));

        Assert.Single(aLaVez, x => x.Estado == HttpStatusCode.Created);
        var otro = Assert.Single(aLaVez, x => x.Estado != HttpStatusCode.Created);
        Assert.True(otro.Estado == HttpStatusCode.Conflict, otro.Cuerpo.ToString());
        Assert.Equal("workflow.transition_not_allowed", Codigo(otro.Cuerpo));
        var instancia = await r.Instancia(expediente);
        Assert.Equal("in-review", EstadoDe(instancia));
        var ganador = aLaVez[0].Estado == HttpStatusCode.Created ? "f-1" : "f-2";
        Assert.Equal(ganador, Assert.Single(Historia(instancia)).GetProperty("byId").GetString());
        Assert.Empty(r.Sagas.StartedBefore(DateTimeOffset.UtcNow.AddDays(1)));
    }

    /// <summary>US1-5: un expediente aprobado o rechazado no admite otra decisión, y nada cambia.</summary>
    [Fact]
    public async Task Sobre_un_expediente_cerrado_otra_decision_dice_que_esta_cerrado()
    {
        using var r = await new RadicacionReal().ConOferta();
        var (radicacion, expediente) = await r.Radicada();
        await r.Tomar(radicacion);
        await r.Decidir(radicacion, "approve", llave: "decision-1");

        var (estado, cuerpo) = await r.Decidir(radicacion, "reject", llave: "decision-2");

        Assert.True(estado == HttpStatusCode.Conflict, cuerpo.ToString());
        Assert.Equal("workflow.instance_closed", Codigo(cuerpo));
        var instancia = await r.Instancia(expediente);
        Assert.Equal("approved", EstadoDe(instancia));
        Assert.Equal(["take", "approve"], Historia(instancia).Select(h => h.GetProperty("transition").GetString()));
    }

    /// <summary>
    /// US1-6: el doble clic y los reintentos con la misma llave caen en el mismo acto, y la transición ocurre una vez.
    /// Se afirma lo que quedó en la capacidad, no cuántas llamadas hubo.
    /// </summary>
    [Fact]
    public async Task El_doble_clic_y_los_reintentos_con_la_misma_llave_son_una_sola_transicion()
    {
        using var r = await new RadicacionReal().ConOferta();
        var (radicacion, expediente) = await r.Radicada();

        var dobles = await Task.WhenAll(r.Tomar(radicacion), r.Tomar(radicacion));
        var id = Assert.Single(dobles.Where(d => d.Estado is HttpStatusCode.Created or HttpStatusCode.OK).Select(d => Id(d.Cuerpo)).Distinct());
        for (var i = 0; i < 5; i++)
        {
            var (estado, otra) = await r.Tomar(radicacion);
            Assert.True(estado is HttpStatusCode.Created or HttpStatusCode.OK, otra.ToString());
            Assert.Equal(id, Id(otra));
            Assert.Equal("in-review", Estado(otra));
        }

        Assert.Equal("take", Assert.Single(Historia(await r.Instancia(expediente))).GetProperty("transition").GetString());
    }

    /// <summary>Sobre una radicación que no existe, o que todavía no se completó, no hay acto: la capacidad ni se entera.</summary>
    [Fact]
    public async Task Sin_una_radicacion_completada_no_hay_acto()
    {
        using var r = await new RadicacionReal().ConOferta();
        var (_, abierta) = await r.Abrir();
        var antes = r.Disparos;

        var (noExiste, sinNada) = await r.Tomar("no-existe", llave: "toma-a");
        var (aMedias, sinCerrar) = await r.Tomar(Id(abierta), llave: "toma-b");

        Assert.True(noExiste == HttpStatusCode.NotFound, sinNada.ToString());
        Assert.Equal("flow.original_no_encontrada", Codigo(sinNada));
        Assert.True(aMedias == HttpStatusCode.Conflict, sinCerrar.ToString());
        Assert.Equal("flow.original_no_completada", Codigo(sinCerrar));
        Assert.Equal(antes, r.Disparos);
    }

    /// <summary>
    /// FR-035 en un acto: la correlación de la puerta llega a la capacidad de procesos, cada llamada queda en el registro
    /// con la saga del acto y su número, y la saga del acto guarda la radicación sobre la que actuó. Que la radicación
    /// vaya también en el ámbito del registro del acto es del núcleo (NG-14).
    /// </summary>
    [Fact]
    public async Task Cada_llamada_del_acto_lleva_su_correlacion_y_su_saga_y_el_acto_guarda_la_radicacion()
    {
        var registro = new RegistroConAmbitos();
        using var r = await new RadicacionReal(registro: registro).ConOferta();
        var (radicacion, _) = await r.Radicada();
        var antes = r.Cable.Llamadas.Count;
        lock (registro.Lineas) registro.Lineas.Clear();

        var (_, toma) = await r.Tomar(radicacion, correlacion: "tomarexpediente01");

        var delActo = r.Cable.Llamadas.Skip(antes).ToList();
        Assert.Equal(["workflow"], delActo.Select(l => l.Capacidad).Distinct());
        Assert.All(delActo, l => Assert.Equal("tomarexpediente01", l.Correlacion));
        var id = Id(toma);
        var numero = toma.GetProperty("numero").GetString()!;
        var llamadas = registro.Lineas
            .Where(l => l.Categoria == RadicacionReal.LlamadasAlArnes && !l.Mensaje.Contains("/v1/deliveries/", StringComparison.Ordinal))
            .ToList();
        Assert.Contains(llamadas, l => l.Mensaje.Contains("/fire", StringComparison.Ordinal));
        Assert.All(llamadas, l =>
        {
            Assert.Equal(id, l.Valores.GetValueOrDefault("Saga"));
            Assert.True(numero.Equals(l.Valores.GetValueOrDefault("NumeroDeSaga")), $"«{l.Mensaje}» no lleva el número {numero}.");
        });
        Assert.Equal(radicacion, r.Dato(id, "radicacion"));
    }

    /// <summary>
    /// SC-005: siete días entre la radicación y la toma, y otros siete después, no compensan nada —ninguna saga espera a
    /// una persona: la radicación se completó al radicar y cada acto es otra saga corta—, y ninguna saga queda abierta
    /// más de 30 minutos.
    /// </summary>
    [Fact]
    public async Task La_espera_de_dias_entre_actos_no_compensa_nada_y_ninguna_saga_queda_abierta()
    {
        var reloj = new RelojFalso(DateTimeOffset.UtcNow);
        using var r = await new RadicacionReal(reloj: reloj).ConOferta();
        var (radicacion, expediente) = await r.Radicada();

        reloj.Avanzar(TimeSpan.FromDays(7));
        await r.Barrer();

        Assert.Equal("Completed", (await r.Consultar(radicacion)).Cuerpo.GetProperty("estado").GetString());
        var cobro = await r.Cobro(radicacion);
        Assert.Equal("Captured", cobro.GetProperty("status").GetString());
        Assert.Equal(0m, cobro.GetProperty("refunded").GetProperty("amount").GetDecimal());
        Assert.Equal("submitted", EstadoDe(await r.Instancia(expediente)));

        var (estado, toma) = await r.Tomar(radicacion);
        Assert.True(estado == HttpStatusCode.Created, toma.ToString());
        reloj.Avanzar(TimeSpan.FromDays(7));
        await r.Barrer();

        Assert.Equal(SagaStatus.Completed, r.Sagas.Find(Id(toma))!.Status);
        Assert.Equal(SagaStatus.Completed, r.Sagas.Find(radicacion)!.Status);
        Assert.Empty(r.Sagas.StartedBefore(reloj.GetUtcNow() - TimeSpan.FromMinutes(30)));
        Assert.Equal("in-review", EstadoDe(await r.Instancia(expediente)));
    }

    private sealed class RelojFalso(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;
        public override DateTimeOffset GetUtcNow() => _ahora;
        public void Avanzar(TimeSpan d) => _ahora += d;
    }
}
