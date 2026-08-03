using Synergos.Api.Notifications.Domain;

namespace Synergos.CMS.Tests.Api;

/// <summary>Cubre <see cref="NotificationRules"/> — lo que los avisos rechazan solos.</summary>
public sealed class NotificationRulesTests
{
    [Theory]
    [InlineData(Channel.Email, "ana@ejemplo.co", true)]
    [InlineData(Channel.Email, "3001234567", false)]
    [InlineData(Channel.Sms, "3001234567", true)]
    [InlineData(Channel.Sms, "ana@ejemplo.co", false)]
    public void Una_direccion_del_canal_EQUIVOCADO_se_rechaza(Channel canal, string direccion, bool ok)
    {
        // Es el error real y frecuente: mandar un teléfono al canal de correo. Si no se ve acá,
        // se ve como un envío "entregado" que nadie recibió.
        Assert.Equal(ok, NotificationRules.CheckAddress(canal, direccion) is null);
    }

    [Fact]
    public void Sin_direccion_no_hay_envio()
    {
        Assert.Equal("notifications.address_required", NotificationRules.CheckAddress(Channel.Email, "  ")?.Code);
    }

    [Fact]
    public void El_tope_de_frecuencia_corta_en_el_limite()
    {
        // Sin tope, un lazo con un fallo manda mil correos a la misma persona. El costo no es la
        // factura: es la dirección del remitente marcada como spam, que deja sin avisos a todos.
        Assert.Null(NotificationRules.CheckRate(NotificationRules.MaxPerRecipient - 1));
        Assert.Equal("notifications.rate_limited", NotificationRules.CheckRate(NotificationRules.MaxPerRecipient)?.Code);
    }

    [Fact]
    public void Un_marcador_SIN_valor_rechaza_en_vez_de_mandar_algo_raro()
    {
        // Dejarlo crudo mandaría "Hola {nombre}"; sustituirlo por vacío mandaría "Hola ,". Las
        // dos son peores que no mandar y decir por qué.
        var r = NotificationRules.Fill("Hola {nombre}, tu cita es el {fecha}.",
            new Dictionary<string, string> { ["nombre"] = "Ana" });

        Assert.False(r.IsOk);
        Assert.Equal("notifications.missing_placeholder", r.Rejection!.Code);
        Assert.Contains("fecha", r.Rejection.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Con_todos_los_valores_se_rellena()
    {
        var r = NotificationRules.Fill("Hola {nombre}, el {fecha}.",
            new Dictionary<string, string> { ["nombre"] = "Ana", ["fecha"] = "5 de marzo" });

        Assert.True(r.IsOk);
        Assert.Equal("Hola Ana, el 5 de marzo.", r.Value);
    }

    [Fact]
    public void Una_plantilla_sin_marcadores_pasa_tal_cual()
    {
        var r = NotificationRules.Fill("Aviso general.", new Dictionary<string, string>());

        Assert.True(r.IsOk);
        Assert.Equal("Aviso general.", r.Value);
    }
}
