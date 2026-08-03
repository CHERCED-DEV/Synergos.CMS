using Synergos.Api.Identity.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Api;

/// <summary>Cubre <see cref="IdentityRules"/> — credenciales, bloqueo y lo que NO se filtra.</summary>
public sealed class IdentityRulesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);

    private static Principal Con(string secreto, int fallos = 0, DateTimeOffset? bloqueadoHasta = null)
        => new("p1", Ref.Create("identity.member", "m-1"), IdentityRules.Derive(secreto),
            new[] { "editor" }, fallos, bloqueadoHasta);

    [Fact]
    public void La_credencial_derivada_NO_contiene_el_secreto()
    {
        var d = IdentityRules.Derive("contrasena-larga");

        Assert.DoesNotContain("contrasena-larga", d.Hash, StringComparison.Ordinal);
        Assert.DoesNotContain("contrasena-larga", d.Salt, StringComparison.Ordinal);
    }

    [Fact]
    public void Dos_derivaciones_del_MISMO_secreto_dan_hashes_distintos()
    {
        // Sal fresca por credencial. Sin ella, dos personas con la misma contraseña tienen el
        // mismo hash, y una tabla precalculada las abre a las dos de una vez.
        var a = IdentityRules.Derive("contrasena-larga");
        var b = IdentityRules.Derive("contrasena-larga");

        Assert.NotEqual(a.Hash, b.Hash);
        Assert.True(IdentityRules.Verify("contrasena-larga", a));
        Assert.True(IdentityRules.Verify("contrasena-larga", b));
    }

    [Fact]
    public void Los_parametros_viajan_CON_la_credencial_y_no_clavados_en_el_codigo()
    {
        // Es lo que permite subir las iteraciones sin invalidar todas las contraseñas a la vez:
        // las viejas se siguen verificando con las suyas mientras se re-derivan de a poco.
        var d = IdentityRules.Derive("contrasena-larga");

        Assert.Equal(IdentityRules.Algorithm, d.Algorithm);
        Assert.Equal(IdentityRules.Iterations, d.Iterations);
        Assert.NotEmpty(d.Salt);
    }

    [Fact]
    public void Una_credencial_corta_no_pasa()
    {
        Assert.Equal("identity.weak_secret", IdentityRules.CheckSecret("corta")?.Code);
        Assert.Null(IdentityRules.CheckSecret(new string('x', IdentityRules.MinSecretLength)));
    }

    [Fact]
    public void Principal_inexistente_y_credencial_mala_dan_el_MISMO_rechazo()
    {
        // Distinguirlos convierte el endpoint en un oráculo que dice qué identidades existen, y
        // eso es la mitad del trabajo de quien va a atacarlas.
        var noExiste = IdentityRules.Authenticate(null, "loquesea", Ahora);
        var malCredencial = IdentityRules.Authenticate(Con("contrasena-larga"), "otra-cosa-larga", Ahora);

        Assert.Equal(noExiste.Rejection!.Code, malCredencial.Rejection!.Code);
        Assert.Equal("identity.bad_credentials", noExiste.Rejection.Code);
    }

    [Fact]
    public void A_los_cinco_fallos_se_BLOQUEA()
    {
        var casi = Con("contrasena-larga", fallos: IdentityRules.MaxFailedAttempts - 1);

        var r = IdentityRules.Authenticate(casi, "mal-mal-mal-mal", Ahora);

        Assert.Equal("identity.locked", r.Rejection!.Code);
        Assert.Equal(Ahora + IdentityRules.LockoutDuration, r.LockedUntilUtc);
    }

    [Fact]
    public void El_bloqueo_SI_se_distingue_y_es_deliberado()
    {
        // A quien de verdad es el dueño hay que decirle por qué no entra, o llamará a soporte
        // convencido de que su contraseña se borró. Quien ataca ya sabe que falló cinco veces.
        var bloqueado = Con("contrasena-larga", fallos: 5, bloqueadoHasta: Ahora.AddMinutes(10));

        var r = IdentityRules.Authenticate(bloqueado, "contrasena-larga", Ahora);

        Assert.Equal("identity.locked", r.Rejection!.Code);
    }

    [Fact]
    public void El_bloqueo_se_LEVANTA_solo_al_vencer()
    {
        var bloqueado = Con("contrasena-larga", fallos: 5, bloqueadoHasta: Ahora);

        var r = IdentityRules.Authenticate(bloqueado, "contrasena-larga", Ahora);

        Assert.Null(r.Rejection);
        Assert.Equal(0, r.FailedAttempts);
    }

    [Fact]
    public void Un_acierto_RESETEA_el_contador()
    {
        // Sin esto, cinco fallos repartidos a lo largo de meses acabarían bloqueando a alguien
        // que entra todos los días.
        var conFallos = Con("contrasena-larga", fallos: 3);

        var r = IdentityRules.Authenticate(conFallos, "contrasena-larga", Ahora);

        Assert.Null(r.Rejection);
        Assert.Equal(0, r.FailedAttempts);
    }

    [Fact]
    public void Verify_rechaza_lo_que_no_tiene_credencial()
    {
        Assert.False(IdentityRules.Verify("contrasena-larga", null));
        Assert.False(IdentityRules.Verify(null, IdentityRules.Derive("contrasena-larga")));
        Assert.False(IdentityRules.Verify("", IdentityRules.Derive("contrasena-larga")));
    }
}
