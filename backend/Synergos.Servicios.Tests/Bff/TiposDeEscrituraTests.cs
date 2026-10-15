using System.Runtime.CompilerServices;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Pasos;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// Cada paso común declara el tipo de lo que escribe (núcleo N1): es lo que deja al validador saber, al
/// arrancar, si un valor que una saga genérica tiene que guardar se puede guardar.
/// </summary>
/// <remarks>
/// <para><b>Un censo y no una lista</b>: se recorren por reflexión todos los <see cref="IPaso"/> públicos
/// de <c>Bff.Pasos</c>, así que un paso común nuevo que no declare sus tipos es rojo aquí y no el día
/// que un flujo genérico guarde lo que escribe.</para>
///
/// <para>Los pasos de un dominio (los de Eventos) pueden no declararlos: su saga es tipada y no los
/// necesita. Si un flujo genérico los usa y guarda lo que escriben, el validador no lo deja arrancar.</para>
/// </remarks>
public sealed class TiposDeEscrituraTests
{
    /// <summary>Los pasos comunes, sin construirlos: sus declaraciones no dependen de sus puertos.</summary>
    public static TheoryData<string> PasosComunes()
    {
        var datos = new TheoryData<string>();
        foreach (var t in Comunes()) datos.Add(t.Name);
        return datos;
    }

    private static IEnumerable<Type> Comunes()
        => typeof(PasoCotizacion).Assembly.GetTypes()
            .Where(t => t is { IsPublic: true, IsAbstract: false, IsClass: true } && typeof(IPaso).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal);

    private static IPaso Paso(string nombre)
        => (IPaso)RuntimeHelpers.GetUninitializedObject(Comunes().Single(t => t.Name == nombre));

    [Fact]
    public void El_censo_ve_los_pasos_comunes()
    {
        // Red de seguridad: un censo que dejó de ver pasa en verde sin mirar nada.
        Assert.True(Comunes().Count() >= 7, $"Se descubrieron {Comunes().Count()} pasos comunes y hay al menos siete.");
    }

    [Theory]
    [MemberData(nameof(PasosComunes))]
    public void Cada_paso_comun_declara_un_tipo_por_cada_valor_que_escribe(string nombre)
    {
        var paso = Paso(nombre);

        Assert.True(paso.TiposDeEscritura is not null, $"«{paso.Tipo}» no declara los tipos de lo que escribe.");
        Assert.Equal(paso.Escrituras, paso.TiposDeEscritura!.Count);
    }

    [Fact]
    public void Lo_que_escriben_los_pasos_de_existencias_y_de_cobro_son_identificadores()
    {
        Assert.Equal(new[] { typeof(string) }, Paso(nameof(PasoInventarioApartar)).TiposDeEscritura);
        Assert.Equal(new[] { typeof(string) }, Paso(nameof(PasoPagosAutorizar)).TiposDeEscritura);
        Assert.Equal(new[] { typeof(QuoteView) }, Paso(nameof(PasoCotizacion)).TiposDeEscritura);
    }
}
