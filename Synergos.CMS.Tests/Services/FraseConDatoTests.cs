using System.Text.Encodings.Web;
using Synergos.CMS.Web.Services.Diccionario;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// <see cref="FraseConDato"/>: una frase del diccionario con el dato del visitante dentro, sin
/// confiar en ninguno de los dos (#193).
/// </summary>
public sealed class FraseConDatoTests
{
    private static string Pintar(string frase, string? dato)
    {
        using var escritor = new StringWriter();
        FraseConDato.Resaltado(frase, dato).WriteTo(escritor, HtmlEncoder.Default);
        return escritor.ToString();
    }

    [Fact]
    public void El_dato_va_resaltado_en_su_sitio()
    {
        Assert.Equal("Restableciendo para <strong>ana@correo.co</strong>.", Pintar("Restableciendo para {0}.", "ana@correo.co"));
    }

    [Fact]
    public void Un_script_en_la_frase_del_diccionario_sale_como_texto()
    {
        var html = Pintar("Hola {0}<script>alert(1)</script><img src=x onerror=alert(2)>", "ana@correo.co");

        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<strong>ana@correo.co</strong>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Un_dato_con_marcado_sale_como_texto()
    {
        var html = Pintar("Para {0}.", "\"><img src=x onerror=alert(1)>");

        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Para <strong>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Una_llave_suelta_en_la_frase_no_tumba_la_pagina()
    {
        // Con string.Format esto era un FormatException: un 500 en la página de restablecer.
        Assert.Equal("Para {email} y <strong>ana@correo.co</strong>", Pintar("Para {email} y {0}", "ana@correo.co"));
    }

    [Fact]
    public void Sin_dato_el_resaltado_queda_vacio()
    {
        Assert.Equal("Para <strong></strong>.", Pintar("Para {0}.", null));
    }
}
