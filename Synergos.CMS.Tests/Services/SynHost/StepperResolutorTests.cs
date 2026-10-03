using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="StepperResolutor"/>: el TEXTO <c>stepsJson</c> llega como la LISTA
/// <c>steps</c> (<c>label</c> → <c>title</c>) y <c>currentStep</c> como número — con los alias el
/// indicador salía vacío (D1).
/// </summary>
public sealed class StepperResolutorTests
{
    private const string Pasos = """[{"label":"Datos"},{"label":"Pago","description":"Con tarjeta o PSE"},{"title":"Confirmación"}]""";

    private readonly ILogger<StepperResolutor> _log = Substitute.For<ILogger<StepperResolutor>>();

    private StepperResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void Los_pasos_viajan_como_lista_y_el_paso_activo_como_numero()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("stepsJson", Pasos),
            ("currentStep", "1"))).Props);

        Assert.Equal(new[] { "steps", "currentStep" }, cable.Keys);
        var steps = (JsonElement)cable["steps"]!;
        Assert.Equal(new[] { "Datos", "Pago", "Confirmación" }, steps.EnumerateArray().Select(s => s.GetProperty("title").GetString()));
        Assert.Equal("Con tarjeta o PSE", steps[1].GetProperty("description").GetString()); // #192, caso 13: ahora viaja
        Assert.Equal(JsonValueKind.Number, ((JsonElement)cable["currentStep"]!).ValueKind);
        Assert.Equal(1, ((JsonElement)cable["currentStep"]!).GetInt32());
    }

    [Fact]
    public void Un_paso_sin_texto_o_un_paso_activo_que_no_es_numero_no_viajan_y_se_anotan()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("stepsJson", """[{"description":"sin título"},{"label":"Pago"}]"""),
            ("currentStep", "segundo"))).Props;

        Assert.Equal(new[] { new StepperItem("Pago") }, props.Steps);
        Assert.Null(props.CurrentStep);
        Assert.Equal(2, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("stepsJson", Pasos), ("currentStep", "2"));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Steps, Resolutor().Resolver(elemento).Props.Steps);
    }

    // #192, caso 13: el elemento pinta la descripción y el id de cada paso; StepperItem sólo traía el título.
    [Fact]
    public void La_descripcion_y_el_id_de_un_paso_viajan()
    {
        var paso = Resolutor().Resolver(ElementoFalso.Con(
            ("stepsJson", """[{"label":"Datos","description":"Tus datos de contacto","id":"datos"}]"""))).Props.Steps!.Single();

        Assert.Equal(new StepperItem("Datos", "Tus datos de contacto", "datos"), paso);
    }
}
