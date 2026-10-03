using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El motivo de un rechazo que ven los nueve controladores de vertical: sin el «(Parameter 'x')»
/// de .NET (#195, y los 50 sitios que devolvían <c>ex.Message</c> tal cual).
/// </summary>
public sealed class MotivoDelRechazoTests
{
    [Fact]
    public void Un_argumento_rechazado_lleva_su_mensaje_sin_el_nombre_del_parametro()
    {
        var ex = new ArgumentException("Aforo insuficiente para el tier 'VIP'.", "items");

        Assert.Contains("items", ex.Message);
        Assert.Equal("Aforo insuficiente para el tier 'VIP'.", ex.Motivo());
    }

    [Fact]
    public void Un_argumento_nulo_tambien_pierde_el_sufijo()
    {
        var ex = new ArgumentNullException("cedula", "Falta la cédula.");

        Assert.Equal("Falta la cédula.", ex.Motivo());
    }

    [Fact]
    public void Una_excepcion_sin_parametro_lleva_su_mensaje_tal_cual()
    {
        Assert.Equal("La cita ya fue tomada.", new InvalidOperationException("La cita ya fue tomada.").Motivo());
        Assert.Equal("Sin parámetro.", new ArgumentException("Sin parámetro.").Motivo());
    }
}
