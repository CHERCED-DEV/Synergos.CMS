using Microsoft.Extensions.Options;
using Synergos.Api.Consent.Domain;
using Synergos.Api.Consent.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Con qué se afirmó la identidad de quien consintió (HU #14, rebanada 5).
/// </summary>
/// <remarks>
/// <para><b>«Fulano consintió» no dice nada sin «y así se supo que era fulano».</b> El día que
/// alguien niegue haber dado un permiso, la diferencia entre un token verificado y la palabra del
/// sitio es la diferencia entre poder sostenerlo y no — y hasta esta rebanada el registro no
/// guardaba ninguna de las dos.</para>
///
/// <para><b>Nulo es «no consta», no un default.</b> Los permisos anteriores a esta rebanada no
/// llevan afirmación, y eso es la verdad sobre ellos: rellenarlos con <c>CmsSession</c> sería
/// inventar una comprobación que nadie hizo — el defecto #42 con otro disfraz.</para>
/// </remarks>
public sealed class ConsentIdentityTests
{
    private sealed class RelojFalso(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _now = inicio;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Avanzar(TimeSpan d) => _now += d;
    }

    private static readonly DateTimeOffset Ahora = new(2026, 8, 3, 9, 0, 0, TimeSpan.Zero);
    private static readonly Ref Paciente = Ref.Create("salud.paciente", "p-1");

    private static (ConsentService Svc, RelojFalso Reloj) Nuevo()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "consent-id-" + Guid.NewGuid().ToString("n"));
        var reloj = new RelojFalso(Ahora);
        var svc = new ConsentService(
            new FileSystemConsentStore(Options.Create(new ConsentStorageOptions { Root = raiz })),
            new FileIdempotencyLedger(raiz),
            reloj);
        return (svc, reloj);
    }

    private static Result<ConsentGrant> Otorgar(
        ConsentService svc, IdentityAssertion assertion, string llave = "g1")
        => svc.Grant(Paciente, "salud.agenda", "v1", null, IdempotencyKey.Of(llave), assertion);

    // ── happy ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(IdentityAssertion.CmsSession)]
    [InlineData(IdentityAssertion.IdentityToken)]
    [InlineData(IdentityAssertion.GovFederation)]
    public void Otorgar_guarda_con_que_se_afirmo(IdentityAssertion afirmacion)
    {
        var (svc, _) = Nuevo();

        var r = Otorgar(svc, afirmacion);

        Assert.True(r.IsOk);
        Assert.Equal(afirmacion, r.Value.GrantedWith);
        // Y no se toca lo de revocar: nadie lo ha retirado.
        Assert.Null(r.Value.RevokedWith);
    }

    /// <summary>
    /// Revocar guarda LA SUYA, y puede ser distinta de la de otorgar.
    /// </summary>
    /// <remarks>
    /// Un consentimiento dado en ventanilla y retirado desde el portal son dos actos, y el
    /// registro tiene que poder decir cómo se identificó cada uno. Pisar el campo de otorgar
    /// borraría con qué se dio, que es lo que hay que sostener si alguien reclama por lo que se
    /// hizo <b>mientras el permiso estaba vigente</b>.
    /// </remarks>
    [Fact]
    public void Revocar_guarda_la_suya_sin_pisar_la_de_otorgar()
    {
        var (svc, reloj) = Nuevo();
        Assert.True(Otorgar(svc, IdentityAssertion.CmsSession).IsOk);
        reloj.Avanzar(TimeSpan.FromDays(30));

        var r = svc.Revoke(Paciente, "salud.agenda", IdentityAssertion.IdentityToken);

        Assert.True(r.IsOk);
        Assert.Equal(IdentityAssertion.IdentityToken, r.Value.RevokedWith);
        Assert.Equal(IdentityAssertion.CmsSession, r.Value.GrantedWith);
        Assert.Equal(Ahora.AddDays(30), r.Value.RevokedAtUtc);
    }

    // ── idempotent ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reintentar con la misma llave devuelve el de antes, con SU afirmación.
    /// </summary>
    /// <remarks>
    /// Es la trampa fina: si el reintento reescribiera la afirmación, bastaría con repetir la
    /// llave declarando algo más fuerte para «mejorar» a posteriori un permiso ya dado. La llave
    /// se resuelve ANTES que cualquier regla de estado (<c>CLAUDE.md</c> §16), así que ni se
    /// llega a mirar lo que trae el segundo intento.
    /// </remarks>
    [Fact]
    public void Reintentar_con_la_misma_llave_no_reescribe_la_afirmacion()
    {
        var (svc, _) = Nuevo();
        var primero = Otorgar(svc, IdentityAssertion.CmsSession);
        Assert.True(primero.IsOk);

        var segundo = Otorgar(svc, IdentityAssertion.GovFederation);

        Assert.True(segundo.IsOk);
        Assert.Equal(primero.Value.Id, segundo.Value.Id);
        Assert.Equal(IdentityAssertion.CmsSession, segundo.Value.GrantedWith);
    }

    /// <summary>Revocar lo ya revocado no reescribe con qué se revocó.</summary>
    /// <remarks>
    /// «Ya estaba retirado» es el estado que el llamador quería, así que no es un error — pero el
    /// acto que cuenta es el PRIMERO, igual que el primer acceso a un acto notificado (#62).
    /// </remarks>
    [Fact]
    public void Revocar_dos_veces_conserva_la_primera_afirmacion()
    {
        var (svc, reloj) = Nuevo();
        Assert.True(Otorgar(svc, IdentityAssertion.CmsSession).IsOk);
        var primera = svc.Revoke(Paciente, "salud.agenda", IdentityAssertion.CmsSession);
        Assert.True(primera.IsOk);

        reloj.Avanzar(TimeSpan.FromDays(1));
        var segunda = svc.Revoke(Paciente, "salud.agenda", IdentityAssertion.GovFederation);

        Assert.True(segunda.IsOk);
        Assert.Equal(IdentityAssertion.CmsSession, segunda.Value.RevokedWith);
        Assert.Equal(primera.Value.RevokedAtUtc, segunda.Value.RevokedAtUtc);
    }

    // ── lo que sostiene el archivo ──────────────────────────────────────────

    /// <summary>
    /// Un permiso ANTERIOR a esta rebanada dice «no consta», y sigue siendo válido.
    /// </summary>
    /// <remarks>
    /// Los registros viejos no mienten sobre su propia fuerza: no dicen <c>CmsSession</c>, dicen
    /// nada. Y no se invalidan por eso — el permiso se dio, sólo que no consta cómo se identificó
    /// a quien lo dio.
    /// </remarks>
    [Fact]
    public void Un_permiso_anterior_dice_no_consta_y_sigue_valiendo()
    {
        var viejo = new ConsentGrant("g-viejo", Paciente, "salud.agenda", "v1", Ahora.AddYears(-1));

        Assert.Null(viejo.GrantedWith);
        Assert.Null(viejo.RevokedWith);
        Assert.True(viejo.IsActive(Ahora));
    }

    /// <summary>
    /// Lo guardado SOBREVIVE al reinicio.
    /// </summary>
    /// <remarks>
    /// Es lo único para lo que sirve: sostener, meses después, cómo se identificó a quien
    /// consintió. Un registro que se pierde al reiniciar contesta «no consta» sobre algo que sí
    /// constaba.
    /// </remarks>
    [Fact]
    public void La_afirmacion_sobrevive_al_reinicio()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "consent-id-" + Guid.NewGuid().ToString("n"));
        var opciones = Options.Create(new ConsentStorageOptions { Root = raiz });
        var reloj = new RelojFalso(Ahora);

        var antes = new ConsentService(new FileSystemConsentStore(opciones), new FileIdempotencyLedger(raiz), reloj);
        var dado = antes.Grant(Paciente, "salud.agenda", "v1", null, IdempotencyKey.Of("g1"), IdentityAssertion.IdentityToken);
        Assert.True(dado.IsOk);

        var despues = new ConsentService(new FileSystemConsentStore(opciones), new FileIdempotencyLedger(raiz), reloj);
        var recargado = despues.Get(dado.Value.Id);

        Assert.True(recargado.IsOk);
        Assert.Equal(IdentityAssertion.IdentityToken, recargado.Value.GrantedWith);
    }

    // ── El derecho al olvido (defecto #83) ──────────────────────────────────

    /// <summary>
    /// Olvidar deja escrito con qué se afirmó quien lo pidió.
    /// </summary>
    /// <remarks>
    /// <para>Es la mitad que le faltaba a la prueba. <c>Forget</c> revoca en vez de borrar
    /// —documentado en su propio fichero— «para poder demostrar que la revocación se atendió, que
    /// es exactamente lo que exige quien la pidió». Esa demostración no decía <b>quién</b> la
    /// pidió ni cómo se supo que era esa persona.</para>
    /// </remarks>
    [Theory]
    [InlineData(IdentityAssertion.CmsSession)]
    [InlineData(IdentityAssertion.IdentityToken)]
    public void Olvidar_guarda_con_que_se_afirmo_quien_lo_pidio(IdentityAssertion afirmacion)
    {
        var (svc, _) = Nuevo();
        Otorgar(svc, IdentityAssertion.CmsSession);

        Assert.Equal(1, svc.Forget(Paciente, afirmacion));

        // Se lee del listado y no de `Check`: `Check` contesta «¿está activo?» y un permiso
        // revocado no lo está, que es justo lo que se acaba de hacer.
        Assert.Equal(afirmacion, Revocado(svc, "salud.agenda").RevokedWith);
    }

    /// <summary>
    /// Olvidar alcanza a TODOS los permisos activos, y cada uno queda con su afirmación.
    /// </summary>
    /// <remarks>
    /// Es lo que lo hace más grave que <c>revoke</c>, y lo que hacía de este endpoint la puerta
    /// de atrás: una sola llamada retira todo lo de una persona.
    /// </remarks>
    [Fact]
    public void Olvidar_alcanza_a_todos_y_todos_quedan_anotados()
    {
        var (svc, _) = Nuevo();
        svc.Grant(Paciente, "salud.agenda", "v1", null, IdempotencyKey.Of("g1"), IdentityAssertion.CmsSession);
        svc.Grant(Paciente, "salud.marketing", "v1", null, IdempotencyKey.Of("g2"), IdentityAssertion.CmsSession);

        Assert.Equal(2, svc.Forget(Paciente, IdentityAssertion.IdentityToken));

        foreach (var proposito in new[] { "salud.agenda", "salud.marketing" })
        {
            Assert.Equal(IdentityAssertion.IdentityToken, Revocado(svc, proposito).RevokedWith);
        }
    }

    /// <summary>
    /// Lo ya revocado no se re-anota: olvidar dos veces no reescribe quién lo pidió la primera.
    /// </summary>
    /// <remarks>
    /// El primer acto es el que consta. Pisar la afirmación con la del segundo intento cambiaría
    /// el registro de una revocación que ya ocurrió — y este registro existe justamente para poder
    /// sostener que ocurrió como dice.
    /// </remarks>
    [Fact]
    public void Olvidar_dos_veces_no_reescribe_la_primera()
    {
        var (svc, _) = Nuevo();
        Otorgar(svc, IdentityAssertion.CmsSession);

        Assert.Equal(1, svc.Forget(Paciente, IdentityAssertion.CmsSession));
        Assert.Equal(0, svc.Forget(Paciente, IdentityAssertion.IdentityToken));

        Assert.Equal(IdentityAssertion.CmsSession, Revocado(svc, "salud.agenda").RevokedWith);
    }

    /// <summary>El permiso de ese propósito, ya revocado.</summary>
    private static ConsentGrant Revocado(ConsentService svc, string proposito)
    {
        var pagina = svc.ListForSubject(Paciente, 0, 50);
        Assert.True(pagina.IsOk);
        var g = pagina.Value.Items.Single(x => x.Purpose == proposito);
        Assert.NotNull(g.RevokedAtUtc);
        return g;
    }
}
