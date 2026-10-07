using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Eventos.Clients;
using Synergos.Bff.Eventos.Domain;
using Synergos.CMS.Tests.Architecture;   // Proyectos: dónde vive cada proyecto (#136)

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Un flujo mal declarado no ARRANCA (ADR 0140): ni el orquestador, ni el intérprete.
/// </summary>
/// <remarks>
/// <para><b>Cada mutante parte de la definición de verdad</b>, <c>flujos/eventos.compra.json</c>, y
/// cambia UNA cosa — y antes de juzgar comprueba que el cambio entró: una mutación que no se aplicó
/// deja la definición intacta y el test en verde por la razón equivocada.</para>
///
/// <para><b>Vive acá y no en <c>Synergos.Arquitectura.Tests</c></b>, a propósito: el arranque de
/// verdad necesita referenciar <c>Bff.Eventos</c>, y esa suite está acotada a ver el árbol por la
/// fuente (CLAUDE.md §0.A.9).</para>
/// </remarks>
public sealed class FlowValidatorTests
{
    private sealed class SinRed : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => throw new InvalidOperationException("Validar una definición no llama a ninguna capacidad.");
    }

    private static readonly IRegistroDePasos Registro = EventosPasos.Registro(new EventosCapabilities(new SinRed()));

    private static string Original()
        => File.ReadAllText(Proyectos.Dir("Synergos.Bff.Eventos", "flujos", "eventos.compra.json"));

    private static string Mutar(string antes, string despues)
    {
        var original = Original();
        Assert.True(original.Contains(antes, StringComparison.Ordinal),
            $"La mutación no entra: la definición ya no dice «{antes}». Revisar este test antes que el validador.");
        return original.Replace(antes, despues, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> Errores(string json)
        => FlowValidator.Validar(FlujoDef.Leer(json), Registro, typeof(TicketingSaga));

    [Fact]
    public void La_definicion_de_eventos_compra_valida_contra_sus_pasos_y_su_saga()
    {
        // El control de todos los de abajo: sin él, un validador que rechazara TODO los pondría
        // en verde a la vez.
        Assert.Empty(Errores(Original()));
        Assert.Empty(FlowValidator.Validar(EventosFlujos.Compra, Registro, typeof(TicketingSaga)));
    }

    [Fact]
    public void Un_tipo_de_paso_que_nadie_registro_no_pasa()
    {
        var errores = Errores(Mutar("\"tipo\": \"inventory.consumir\"", "\"tipo\": \"inventory.entregar\""));

        Assert.Contains(errores, e => e.Contains("«inventory.entregar»", StringComparison.Ordinal));
    }

    [Fact]
    public void Una_lectura_sin_escritura_previa_no_pasa()
    {
        var errores = Errores(Mutar("\"lee\": [\"paymentId\"]", "\"lee\": [\"cobroId\"]"));

        Assert.Contains(errores, e => e.Contains("lee «cobroId» y nadie lo escribe antes", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_campo_del_item_que_se_lee_antes_de_escribirse_no_pasa()
    {
        // Apartar antes de hallar el pozo: apartar leería un itemId que todavía no existe.
        var errores = Errores(Mutar(
            "\"pasos\": [\"sujeto-pozo\", \"hallar\", \"apartar\"]",
            "\"pasos\": [\"sujeto-pozo\", \"apartar\", \"hallar\"]"));

        Assert.Contains(errores, e => e.Contains("«apartar» lee «linea.itemId»", StringComparison.Ordinal));
    }

    [Fact]
    public void Una_reserva_sin_quien_la_consuma_no_pasa()
    {
        // Sin consumación, la compensación del cobro quedaría armada para siempre: ni se cambiaría
        // de carácter al capturar ni se marcaría hecha.
        var errores = Errores(Mutar("\"consumado_por\": \"capturar\"", "\"consumado_por\": \"cobrar\""));

        Assert.Contains(errores, e => e.Contains("se consuma con «cobrar», que no está declarado", StringComparison.Ordinal));
    }

    [Fact]
    public void Las_lecturas_de_un_paso_se_cruzan_con_lo_que_su_tipo_lee()
    {
        var errores = Errores(Mutar("\"lee\": [\"comprador\", \"total\"]", "\"lee\": [\"comprador\"]"));

        Assert.Contains(errores, e => e.Contains("declara 1 lectura(s) y «payments.autorizar» lee 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_campo_que_el_interprete_no_conoce_no_se_lee()
    {
        // Quien escribe «al_fallar» cree haber declarado algo; si se ignorara, la primera compra
        // le enseñaría que no.
        var ex = Assert.Throws<FormatException>(() => FlujoDef.Leer(Mutar(
            "\"llave\": \"capture\",", "\"llave\": \"capture\", \"al_fallar\": \"soltar-y-seguir\",")));

        Assert.Contains("«al_fallar»", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_saga_sin_las_ranuras_que_el_flujo_usa_no_pasa()
    {
        var errores = FlowValidator.Validar(EventosFlujos.Compra, Registro, typeof(object));

        Assert.Contains(errores, e => e.Contains(nameof(IHoldLedger), StringComparison.Ordinal));
        Assert.Contains(errores, e => e.Contains(nameof(IChargeLedger), StringComparison.Ordinal));
    }

    [Fact]
    public void Un_catalogo_vacio_no_pasa()
    {
        var resultado = new FlowDefinitionValidator(Registro).Validate(null, new FlowCatalog());

        Assert.True(resultado.Failed);
    }

    // ── El arranque de verdad ───────────────────────────────────────────────

    /// <summary>
    /// Anota si el catálogo se validó y con qué resultado, desde DENTRO del host.
    /// </summary>
    /// <remarks>
    /// <para><b>Por qué un testigo y no la excepción del arranque.</b> Con <i>minimal hosting</i>,
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> arranca el host en el hilo del
    /// <c>Program</c>, y cuando el arranque falla su <c>app.Run()</c> desecha el host. Si eso gana la
    /// carrera al hilo del test, lo que sube es un <see cref="ObjectDisposedException"/> y no la
    /// <see cref="OptionsValidationException"/>: medido, uno de cada ocho en la suite completa.
    /// Un test que juzga por el TIPO de la excepción es un rojo intermitente; el testigo corre
    /// dentro de la validación, antes de la carrera, y lo que anota no depende de quién gane.</para>
    ///
    /// <para><b>Y prueba lo que importa:</b> que se validó AL ARRANCAR. Sin <c>ValidateOnStart</c>,
    /// nadie pide el catálogo hasta la primera compra, y el testigo no ve nada.</para>
    /// </remarks>
    private sealed class Testigo : IValidateOptions<FlowCatalog>
    {
        public ValidateOptionsResult? Visto { get; private set; }

        public ValidateOptionsResult Validate(string? name, FlowCatalog options)
        {
            Visto = new FlowDefinitionValidator(Registro).Validate(name, options);
            return ValidateOptionsResult.Skip;
        }
    }

    private static WebApplicationFactory<TicketingSaga> Orquestador(string raiz, Testigo testigo, FlujoDef? reemplazo = null)
        => new WebApplicationFactory<TicketingSaga>().WithWebHostBuilder(b =>
        {
            b.UseSetting("eventos:Storage:Root", raiz);
            b.UseSetting("Eventos:ApiKey", "llave-de-prueba");
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IValidateOptions<FlowCatalog>>(testigo);
                if (reemplazo is not null) s.PostConfigure<FlowCatalog>(c => c.Registrar(reemplazo));
            });
        });

    [Fact]
    public async Task El_orquestador_de_Eventos_arranca_con_su_definicion_y_la_valida_al_arrancar()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "flujo-arranque-" + Guid.NewGuid().ToString("N"));
        try
        {
            var testigo = new Testigo();
            using var fabrica = Orquestador(raiz, testigo);
            using var cliente = fabrica.CreateClient();

            using var salud = await cliente.GetAsync(new Uri("/health", UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, salud.StatusCode);
            Assert.True(testigo.Visto is { Succeeded: true }, "El catálogo de flujos no se validó al arrancar.");

            // Y la fachada se construye: valida la misma definición contra el mismo registro.
            Assert.NotNull(fabrica.Services.GetRequiredService<TicketingFlow>());
        }
        finally
        {
            if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
        }
    }

    [Fact]
    public void El_orquestador_de_Eventos_NO_arranca_con_una_definicion_rota()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "flujo-arranque-" + Guid.NewGuid().ToString("N"));
        try
        {
            var testigo = new Testigo();
            var rota = FlujoDef.Leer(Mutar("\"consumado_por\": \"capturar\"", "\"consumado_por\": \"cobrar\""));
            using var fabrica = Orquestador(raiz, testigo, rota);

            var ex = Record.Exception(() => fabrica.CreateClient());

            Assert.True(ex is not null, "El orquestador arrancó con un flujo roto: llegaría a la primera compra.");
            Assert.True(testigo.Visto is { Failed: true },
                "El arranque falló, pero no porque se validara el catálogo de flujos al arrancar: "
                + ex.GetType().Name);
            Assert.Contains(testigo.Visto!.Failures!, f => f.Contains("«cobrar»", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
        }
    }
}
