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
}
