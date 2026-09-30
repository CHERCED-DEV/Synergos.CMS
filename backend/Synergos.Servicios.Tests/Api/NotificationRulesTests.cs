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

    private static Template Plantilla(string asunto, string cuerpo, Channel canal = Channel.Email)
        => new("t-1", "prueba.aviso", canal, asunto, cuerpo);

    [Fact]
    public void Un_marcador_SIN_valor_rechaza_en_vez_de_mandar_algo_raro()
    {
        // Dejarlo crudo mandaría "Hola {nombre}"; sustituirlo por vacío mandaría "Hola ,". Las
        // dos son peores que no mandar y decir por qué.
        var r = NotificationRules.Fill(Plantilla("Aviso", "Hola {nombre}, tu cita es el {fecha}."),
            new Dictionary<string, string> { ["nombre"] = "Ana" });

        Assert.False(r.IsOk);
        Assert.Equal("notifications.missing_placeholder", r.Rejection!.Code);
        Assert.Contains("fecha", r.Rejection.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_marcador_SIN_valor_en_el_ASUNTO_tambien_rechaza()
    {
        var r = NotificationRules.Fill(Plantilla("Tu aviso del {fecha}", "Hola {nombre}."),
            new Dictionary<string, string> { ["nombre"] = "Ana" });

        Assert.Equal("notifications.missing_placeholder", r.Rejection?.Code);
    }

    [Fact]
    public void Con_todos_los_valores_se_rellenan_asunto_y_cuerpo()
    {
        var r = NotificationRules.Fill(Plantilla("Tu aviso del {fecha}", "Hola {nombre}, el {fecha}."),
            new Dictionary<string, string> { ["nombre"] = "Ana", ["fecha"] = "5 de marzo" });

        Assert.True(r.IsOk);
        Assert.Equal("Tu aviso del 5 de marzo", r.Value.Subject);
        Assert.Equal("Hola Ana, el 5 de marzo.", r.Value.Body);
    }

    [Fact]
    public void Una_plantilla_sin_marcadores_pasa_tal_cual()
    {
        var r = NotificationRules.Fill(Plantilla("Aviso", "<p>Aviso general.</p>"), new Dictionary<string, string>());

        Assert.True(r.IsOk);
        Assert.Equal("<p>Aviso general.</p>", r.Value.Body);
    }

    // ── Lo que entra por un valor (#175) ────────────────────────────────────

    private const string ConMarcado = "<b>x</b> & \"y\"";

    [Fact]
    public void Un_valor_con_marcado_entra_CODIFICADO_al_cuerpo_de_un_correo_y_TAL_CUAL_al_asunto()
    {
        // El cuerpo de un correo sale como html: crudo, este valor es marcado dentro de un correo
        // con el remitente del producto, y el primer vertical que mande un nombre o un comentario
        // lo inyecta. El asunto es una cabecera, texto plano: codificarlo le mostraría «&lt;» a
        // quien lo lee. Y el marcado de la PLANTILLA no se toca — ése lo escribe el dominio.
        var r = NotificationRules.Fill(Plantilla("Aviso para {nombre}", "<p>Hola {nombre}</p>"),
            new Dictionary<string, string> { ["nombre"] = ConMarcado });

        Assert.True(r.IsOk);
        Assert.Equal("<p>Hola &lt;b&gt;x&lt;/b&gt; &amp; &quot;y&quot;</p>", r.Value.Body);
        Assert.Equal("Aviso para <b>x</b> & \"y\"", r.Value.Subject);
    }

    [Theory]
    [InlineData(Channel.Sms)]
    [InlineData(Channel.Push)]
    public void En_SMS_y_push_el_cuerpo_es_texto_y_el_valor_NO_se_codifica(Channel canal)
    {
        // Codificar acá no protege nada —no hay HTML que interpretar— y le manda «&amp;» a un
        // teléfono. Es la mitad que se rompe si alguien «simplifica» codificando siempre.
        var r = NotificationRules.Fill(Plantilla("Aviso", "Hola {nombre}", canal),
            new Dictionary<string, string> { ["nombre"] = ConMarcado });

        Assert.Equal("Hola " + ConMarcado, r.Value.Body);
    }

    [Fact]
    public void Un_valor_normal_con_tildes_y_enes_sale_IGUAL_en_el_cuerpo_de_un_correo()
    {
        // Lo que separa al codificador elegido de HtmlEncoder.Default y de WebUtility.HtmlEncode:
        // los tres se ven igual en el correo, pero esos dos vuelven entidad cada tilde, y el rastro
        // guardado de un producto en español pasaría a leerse «rechaz&#xF3;».
        const string normal = "José Núñez: el proveedor rechazó el envío (¿reintento?)";

        var r = NotificationRules.Fill(Plantilla("Aviso", "<p>{detalle}</p>"),
            new Dictionary<string, string> { ["detalle"] = normal });

        Assert.Equal($"<p>{normal}</p>", r.Value.Body);
    }
}
