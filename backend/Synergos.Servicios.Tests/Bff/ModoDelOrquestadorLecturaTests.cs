using Microsoft.Extensions.Configuration;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>El interruptor declarado/imperativo de un orquestador (núcleo N1, supuesto P4).</summary>
public sealed class ModoDelOrquestadorLecturaTests
{
    private static readonly SagaVocabulary Salud = new("salud", "la cita");

    private static IConfiguration Con(string? modo) => new ConfigurationBuilder()
        .AddInMemoryCollection(modo is null ? [] : new Dictionary<string, string?> { ["Salud:Flujos:Modo"] = modo })
        .Build();

    [Fact]
    public void Sin_configurar_corre_el_que_el_orquestador_dice_por_defecto()
    {
        Assert.Equal(ModoDelOrquestador.Imperativo, ModosDelOrquestador.Leer(Con(null), Salud, ModoDelOrquestador.Imperativo));
        Assert.Equal(ModoDelOrquestador.Declarado, ModosDelOrquestador.Leer(Con("  "), Salud, ModoDelOrquestador.Declarado));
    }

    [Fact]
    public void Las_dos_palabras_se_leen_sin_mirar_mayusculas_y_otra_no_arranca()
    {
        Assert.Equal(ModoDelOrquestador.Declarado, ModosDelOrquestador.Leer(Con("Declarado"), Salud, ModoDelOrquestador.Imperativo));
        Assert.Equal(ModoDelOrquestador.Imperativo, ModosDelOrquestador.Leer(Con("imperativo"), Salud, ModoDelOrquestador.Declarado));

        var ex = Assert.Throws<InvalidOperationException>(() => ModosDelOrquestador.Leer(Con("declarativo"), Salud, ModoDelOrquestador.Imperativo));
        Assert.Contains("«declarativo»", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Por flujo (núcleo N3, NE-10 de 007): un orquestador con dos flujos porta uno y conserva el otro. Lo del
    /// flujo gana; sin él, el del orquestador; sin los dos, el por defecto.
    /// </summary>
    [Fact]
    public void El_modo_de_un_flujo_gana_al_del_orquestador_y_sin_el_decide_el_orquestador()
    {
        var eventos = new SagaVocabulary("eventos", "la compra");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Eventos:Flujos:Modo"] = "imperativo",
            ["Eventos:Flujos:eventos.oferta:Modo"] = "Declarado",
        }).Build();

        Assert.Equal(ModoDelOrquestador.Declarado,
            ModosDelOrquestador.LeerDelFlujo(config, eventos, "eventos.oferta", ModoDelOrquestador.Imperativo));
        Assert.Equal(ModoDelOrquestador.Imperativo,
            ModosDelOrquestador.LeerDelFlujo(config, eventos, "eventos.compra", ModoDelOrquestador.Declarado));
        Assert.Equal(ModoDelOrquestador.Declarado,
            ModosDelOrquestador.LeerDelFlujo(Con(null), Salud, "salud.cita", ModoDelOrquestador.Declarado));

        var mal = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Eventos:Flujos:eventos.oferta:Modo"] = "a veces",
        }).Build();
        var ex = Assert.Throws<InvalidOperationException>(
            () => ModosDelOrquestador.LeerDelFlujo(mal, eventos, "eventos.oferta", ModoDelOrquestador.Imperativo));
        Assert.Contains("eventos:Flujos:eventos.oferta:Modo vale «a veces»", ex.Message, StringComparison.Ordinal);
    }
}
