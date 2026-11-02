using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Tests.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La cara pública de una persona (012-blogs-social, I0, P3 A): su nombre, un alias estable y su llave, el seudónimo.
/// Nunca su correo.
/// </summary>
public sealed class IdentidadPublicaTests
{
    private const string Ana = "ana.perez@correo.test";

    // ── La regla (CaraPublica) ─────────────────────────────────────────

    [Theory]
    [InlineData("Ana Pérez", "ana-perez")]
    [InlineData("  José Ñúñez  ", "jose-nunez")]
    [InlineData("María-José O'Brien", "maria-jose-o-brien")]
    [InlineData("!!!", "miembro")]
    public void El_alias_es_el_nombre_en_minusculas_sin_tildes_y_el_comienzo_de_la_llave(string nombre, string esperado)
    {
        Assert.Equal($"{esperado}-0123ab", CaraPublica.Alias(nombre, "0123abcdef456789"));
    }

    [Fact]
    public void El_alias_corta_un_nombre_largo_sin_dejar_un_guion_colgando()
    {
        var alias = CaraPublica.Alias("Alejandro Fernando de la Santísima Trinidad Ospina", "0123abcdef456789");

        Assert.EndsWith("-0123ab", alias);
        Assert.True(alias.Length <= 32 + 7, alias);
        Assert.DoesNotContain("--", alias);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ana.perez@correo.test")]
    public void Sin_nombre_o_con_un_correo_por_nombre_sale_Miembro(string? nombre)
    {
        var cara = CaraPublica.De("0123abcdef456789", nombre);

        Assert.Equal(CaraPublica.NombreSinNombre, cara.Nombre);
        Assert.Equal("miembro-0123ab", cara.Alias);
    }

    [Theory]
    [InlineData("0123abcdef456789", true)]
    [InlineData("0123ABCDEF456789", false)]
    [InlineData("0123abcdef45678", false)]
    [InlineData("act-elena", false)]
    [InlineData(null, false)]
    public void Una_llave_son_dieciseis_hexadecimales_en_minusculas(string? valor, bool esLlave)
    {
        Assert.Equal(esLlave, CaraPublica.EsLlave(valor));
    }

    // ── La pieza sobre los miembros (IdentidadPublicaDeMiembros) ──────────

    [Fact]
    public void La_cara_de_un_miembro_es_su_nombre_su_alias_y_su_seudonimo_y_no_lleva_su_correo()
    {
        var cara = IdentidadDePrueba.Con((Ana, "Ana Pérez")).De(Ana);

        Assert.Equal(SeudonimoDePersona.De(Ana), cara.Llave);
        Assert.Equal("Ana Pérez", cara.Nombre);
        Assert.Equal($"ana-perez-{cara.Llave[..6]}", cara.Alias);
        Assert.DoesNotContain("@", $"{cara.Llave}{cara.Alias}{cara.Nombre}", StringComparison.Ordinal);
        Assert.DoesNotContain("correo", cara.Alias, StringComparison.Ordinal);
    }

    [Fact]
    public void La_cara_es_estable_entre_lecturas_y_no_depende_de_las_mayusculas_del_correo()
    {
        var identidad = IdentidadDePrueba.Con((Ana, "Ana Pérez"), ("ANA.PEREZ@correo.test", "Ana Pérez"));

        Assert.Equal(identidad.De(Ana), identidad.De(Ana));
        Assert.Equal(identidad.De(Ana).Llave, identidad.De("ANA.PEREZ@correo.test").Llave);
    }

    [Fact]
    public void Un_miembro_sin_ficha_sale_como_Miembro_y_no_con_su_correo()
    {
        var cara = IdentidadDePrueba.Con().De(Ana);

        Assert.Equal(CaraPublica.NombreSinNombre, cara.Nombre);
        Assert.StartsWith("miembro-", cara.Alias, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Lo_anotado_lleva_de_la_llave_y_del_alias_al_correo()
    {
        var identidad = IdentidadDePrueba.Con((Ana, "Ana Pérez"));
        var cara = identidad.De(Ana);

        await identidad.RecordarAsync(Ana);

        Assert.Equal(Ana, await identidad.CorreoDeAsync(cara.Llave));
        Assert.Equal(Ana, await identidad.CorreoDeAsync(cara.Alias));
        Assert.Equal(Ana, await identidad.CorreoDeAsync(cara.Alias.ToUpperInvariant()));
    }

    [Fact]
    public async Task Sin_anotar_no_se_vuelve_al_correo_porque_la_huella_no_se_invierte()
    {
        var identidad = IdentidadDePrueba.Con((Ana, "Ana Pérez"));

        Assert.Null(await identidad.CorreoDeAsync(identidad.De(Ana).Llave));
        Assert.Null(await identidad.CorreoDeAsync(identidad.De(Ana).Alias));
    }

    [Theory]
    [InlineData("ana.perez@correo.test")]
    [InlineData("../identidad-publica/x")]
    [InlineData("")]
    [InlineData("alias con espacios")]
    public async Task Lo_que_no_es_una_llave_ni_un_alias_no_se_busca(string valor)
    {
        var identidad = IdentidadDePrueba.Con((Ana, "Ana Pérez"));
        await identidad.RecordarAsync(Ana);

        Assert.Null(await identidad.CorreoDeAsync(valor));
    }

    [Fact]
    public async Task Un_alias_ya_anotado_por_otra_persona_no_se_pisa_y_a_la_segunda_se_la_encuentra_por_su_llave()
    {
        var store = new InMemoryJsonEntityStore();
        var identidad = IdentidadDePrueba.Con(store, (Ana, "Ana Pérez"));
        var cara = identidad.De(Ana);

        // Otra persona llegó antes con el mismo alias (mismo nombre y los mismos seis primeros de la llave).
        const string otra = "ffffffffffffffff";
        await store.WriteAsync(IdentidadPublicaDeMiembros.PorLlave, otra,
            $$"""{"llave":"{{otra}}","correo":"otra@correo.test","alias":"{{cara.Alias}}"}""");
        await store.WriteAsync(IdentidadPublicaDeMiembros.PorAlias, cara.Alias,
            $$"""{"alias":"{{cara.Alias}}","llave":"{{otra}}"}""");

        await identidad.RecordarAsync(Ana);

        Assert.Equal("otra@correo.test", await identidad.CorreoDeAsync(cara.Alias));
        Assert.Equal(Ana, await identidad.CorreoDeAsync(cara.Llave));
    }
}
