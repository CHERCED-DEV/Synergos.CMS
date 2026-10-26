using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;
using Synergos.Bff.Pasos.Http;
using Synergos.CMS.Tests.Architecture;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Lo que el arranque exige de la extensión que contesta la confirmación parcial de un flujo genérico (núcleo, NV-13):
/// que el flujo declare <c>soltar</c>, que exista y sea genérico, que lo que arma se sepa deshacer y que lo que lee tenga
/// tipo; y que sin ella el enlace genérico diga que no, como antes.
/// </summary>
public sealed class ExtensionDelFlujoEnLaDefinicionTests
{
    private sealed class SinRed : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Validar no llama a nadie.");
    }

    private sealed class Extension(string flujo = "muestra.viaje", string[]? lee = null, string[]? arma = null) : IExtensionDelFlujo
    {
        public string Flujo => flujo;
        public IReadOnlyCollection<string> Lee => lee ?? ["politica"];
        public IReadOnlyCollection<string> Escribe => ["vista"];
        public IReadOnlyCollection<string> Arma => arma ?? [ParcialEnElHostGenericoTests.DevolverParte.Clase];
        public bool SueltaSiFalla(SagaDeFlujo saga, FlowContext ctx) => true;
        public SagaDeFlujo ConItemSoltado(SagaDeFlujo saga, FlowContext ctx, string reservante, HoldLeg soltada) => saga;
    }

    private static readonly string Viaje = File.ReadAllText(
        Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "ParcialGenerica", "flujos", "muestra.viaje.json"));

    private static readonly PuertosHttp Puertos = new(new SinRed());
    private static readonly TiposDeValor Tipos = TiposDeValor.Con(new TipoContacto(), new TipoDireccion(), new TipoFranjas());
    private static readonly IRegistroDePasos Registro = PasosComunes.Registro(Puertos, new ParcialEnElHostGenericoTests.PasoVista());

    private static BindingDeFlujo Binding(FlujoDef flujo, IExtensionDelFlujo? extension, bool conLoQueArma = true)
        => new(flujo, Tipos, Registro, new DeshacerComun(Puertos, Puertos, Puertos, Puertos,
            conLoQueArma ? [new ParcialEnElHostGenericoTests.DevolverParte(Puertos, Tipos)] : []).Tipos, extension);

    private static IReadOnlyList<string> Errores(string json, IExtensionDelFlujo? extension, bool conLoQueArma = true)
    {
        var flujo = FlujoDef.Leer(json);
        return FlowValidator.Validar(flujo, Registro, ContratoDelFlujo.De(Binding(flujo, extension, conLoQueArma), flujo.Fases.Select(f => f.Nombre).ToList()));
    }

    private static void Dice(IReadOnlyList<string> errores, string mensaje)
        => Assert.True(errores.Any(e => e.Contains(mensaje, StringComparison.Ordinal)),
            $"Se esperaba «{mensaje}» y salió:{Environment.NewLine}{string.Join(Environment.NewLine, errores)}");

    private static string SinSoltar() => Viaje.Replace("\"al_fallar\": \"soltar\",", string.Empty, StringComparison.Ordinal);

    [Fact]
    public void Con_soltar_y_su_extension_arranca()
    {
        var errores = Errores(Viaje, new Extension());

        Assert.True(errores.Count == 0, string.Join(Environment.NewLine, errores));
    }

    [Fact]
    public void Soltar_sin_extension_no_arranca()
        => Dice(Errores(Viaje, extension: null), "«confirmar» declara «al_fallar: soltar» y ningún vertical extiende el flujo");

    [Fact]
    public void Una_extension_sin_soltar_no_arranca()
    {
        Assert.NotEqual(Viaje, SinSoltar());

        Dice(Errores(SinSoltar(), new Extension()), "un vertical lo extiende (IExtensionDelFlujo) y ningún paso declara «al_fallar: soltar»");
    }

    [Fact]
    public void Lo_que_arma_la_extension_y_nadie_sabe_deshacer_no_arranca()
        => Dice(Errores(Viaje, new Extension(), conLoQueArma: false), "su extensión arma «muestra.devolver-parte» al soltar un ítem, y nadie sabe deshacer ese tipo");

    [Fact]
    public void Lo_que_lee_la_extension_sin_tipo_no_arranca()
        => Dice(Errores(Viaje, new Extension(lee: ["politicaa"])), "«politicaa» se guarda en la saga");

    [Fact]
    public void Lo_que_lee_y_reescribe_la_extension_se_guarda_en_la_saga()
    {
        var flujo = FlujoDef.Leer(Viaje);

        var con = Binding(flujo, new Extension());
        var sin = Binding(FlujoDef.Leer(SinSoltar()), extension: null);

        Assert.Contains("politica", con.Reconstruye);
        Assert.Contains("politica", con.Guarda);
        Assert.Contains("vista", con.Guarda);
        Assert.DoesNotContain("politica", sin.Guarda);
    }

    [Fact]
    public void Sin_extension_el_enlace_generico_no_suelta_y_no_toca_la_saga()
    {
        var binding = Binding(FlujoDef.Leer(SinSoltar()), extension: null);
        var saga = new SagaDeFlujo("s-1", "muestra.viaje", Ref.Create("muestra.viajero", "u-1"), SagaStatus.Running,
            new Dictionary<string, ValorDeFlujo>(), new Dictionary<string, IReadOnlyList<ReservaDeFlujo>> { ["apartar"] = [] }, [], null, DateTimeOffset.UtcNow);
        var ctx = new FlowContext().Set("politica", "parcial");

        Assert.False(binding.SueltaSiFalla(saga, ctx));
        Assert.Same(saga, binding.ConItemSoltado(saga, "apartar", new HoldLeg("h-1", "h-1"), ctx));
        Assert.Null(binding.ArmaLaExtension);
    }

    [Fact]
    public void Una_extension_de_un_flujo_que_no_existe_o_no_es_generico_no_arranca()
    {
        var flujo = FlujoDef.Leer(Viaje);
        var generico = new FlujoRegistrado(flujo, ContratoDelFlujo.De(Binding(flujo, new Extension()), flujo.Fases.Select(f => f.Nombre).ToList()));
        var tipado = FlujoDef.Leer(Viaje.Replace("\"clave\": \"muestra.viaje\"", "\"clave\": \"muestra.tipado\"", StringComparison.Ordinal));
        var catalogo = new FlowCatalog()
            .Registrar(generico.Flujo, generico.Contrato)
            .Registrar(tipado, generico.Contrato with { Tipos = null, Saga = typeof(SagaDeFlujo) });

        var resultado = new FlowDefinitionValidator(Registro, null, [new Extension(), new Extension("muestra.otro"), new Extension("muestra.tipado")])
            .Validate(null, catalogo);

        Assert.True(resultado.Failed);
        Dice(resultado.Failures!.ToList(), "Extension extiende el flujo «muestra.otro», y el orquestador no tiene ese flujo.");
        Dice(resultado.Failures!.ToList(), "Extension extiende el flujo «muestra.tipado», que no es genérico");
        Assert.DoesNotContain(resultado.Failures!, f => f.Contains("«muestra.viaje», y el orquestador", StringComparison.Ordinal));
    }
}
