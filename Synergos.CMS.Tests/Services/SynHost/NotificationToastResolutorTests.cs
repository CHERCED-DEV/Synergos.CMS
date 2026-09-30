using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="NotificationToastResolutor"/>: <c>message</c>/<c>type</c> llegan como la lista
/// <c>toasts</c> (<c>message</c>/<c>variant</c>) que el elemento lee del <c>config</c>, y
/// <c>durationMs</c> como número — con los alias sueltos el aviso hidrataba vacío (D1).
/// </summary>
public sealed class NotificationToastResolutorTests
{
    private readonly ILogger<NotificationToastResolutor> _log = Substitute.For<ILogger<NotificationToastResolutor>>();

    private NotificationToastResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void El_aviso_viaja_como_lista_de_uno_y_la_duracion_como_numero()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("message", "Pedido confirmado"),
            ("type", "Success"),
            ("durationMs", "8000"))).Props);

        Assert.Equal(new[] { "toasts", "durationMs" }, cable.Keys);
        var toasts = (JsonElement)cable["toasts"]!;
        Assert.Equal(1, toasts.GetArrayLength());
        Assert.Equal("Pedido confirmado", toasts[0].GetProperty("message").GetString());
        Assert.Equal("success", toasts[0].GetProperty("variant").GetString());
        Assert.Equal(8000, ((JsonElement)cable["durationMs"]!).GetInt32());
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    public void El_cero_es_persistente_y_viaja_lo_que_no_es_entero_no(string duracion, int? esperado)
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(("message", "Hola"), ("durationMs", duracion))).Props;

        Assert.Equal(esperado, props.DurationMs);
    }

    [Fact]
    public void Sin_mensaje_no_hay_aviso_aunque_haya_tipo()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(("message", "  "), ("type", "error"))).Props;

        Assert.Null(props.Toasts);
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("message", "Hola"), ("type", "info"), ("durationMs", "3000"));

        Assert.Equal(Resolutor().Resolver(elemento).Props.Toasts, Resolutor().Resolver(elemento).Props.Toasts);
        Assert.Equal(Resolutor().Resolver(elemento).Props.DurationMs, Resolutor().Resolver(elemento).Props.DurationMs);
    }
}
