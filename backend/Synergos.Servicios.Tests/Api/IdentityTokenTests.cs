using System.Text;
using Synergos.Api.Identity.Domain;
using Synergos.Api.Identity.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// El token de identidad: emitir, verificar, y sobre todo <b>no dejarse engañar</b> (HU #14).
/// </summary>
/// <remarks>
/// <para><b>Lo que se prueba acá no es «se emite un token».</b> Es lo único que hace que el token
/// sirva para algo: que una capacidad pueda comprobar por su cuenta quién actúa, <b>sin llamar a
/// nadie</b> — y que no acepte nada que no haya firmado <c>Api.Identity</c>.</para>
///
/// <para><b>Y lo que el token NO es</b>, para que ningún test lo sugiera: no es prueba más fuerte
/// frente a un tercero. Lo emite un servicio nuestro a partir de la palabra del CMS, así que la
/// cadena de confianza toca fondo en el mismo sitio que <c>CmsSession</c>. Lo que compra es
/// integridad interna: el sujeto viene firmado y no se puede reapuntar.</para>
/// </remarks>
public sealed class IdentityTokenTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly Ref Ciudadano = Ref.Create("gov.ciudadano", "c-1");
    private static readonly Ref Otro = Ref.Create("gov.ciudadano", "c-99");

    private static IdentityTokens Emisor(string kid = "k1", params (string Kid, string Secreto)[] extra)
    {
        var llaves = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["k1"] = Encoding.UTF8.GetBytes("llave-de-firma-uno"),
        };
        foreach (var (k, s) in extra) llaves[k] = Encoding.UTF8.GetBytes(s);
        return new IdentityTokens(llaves, kid);
    }

    private static IdentityClaims Afirma(
        Ref? sujeto = null, int vigenciaMin = 15, DateTimeOffset? sesionDesde = null)
        => new(sujeto ?? Ciudadano, new[] { "ciudadano" },
            Ahora, Ahora.AddMinutes(vigenciaMin), sesionDesde ?? Ahora);

    // ── Emitir y verificar ──────────────────────────────────────────────────

    [Fact] // happy: lo que se emite se verifica, y dice lo mismo que se afirmó.
    public void Lo_que_se_emite_se_verifica_y_dice_lo_mismo()
    {
        var emisor = Emisor();

        var (claims, motivo) = emisor.Verify(emisor.Issue(Afirma()), Ahora);

        Assert.Null(motivo);
        Assert.NotNull(claims);
        Assert.Equal(Ciudadano, claims!.Subject);
        Assert.Equal(new[] { "ciudadano" }, claims.Roles);
        Assert.Equal(Ahora.AddMinutes(15), claims.ExpiresAtUtc);
    }

    /// <summary>
    /// Verificar NO llama a nadie: es lo que impide que <c>Api.Identity</c> sea el punto único de
    /// fallo de las veinte capacidades.
    /// </summary>
    /// <remarks>
    /// Se comprueba con un emisor que solo conoce la llave — sin almacén, sin cliente HTTP, sin
    /// reloj del sistema. Si algún día verificar necesitara salir a la red, este test no
    /// compilaría, que es exactamente el aviso que hace falta.
    /// </remarks>
    [Fact]
    public void Verificar_no_necesita_nada_mas_que_la_llave()
    {
        var soloLaLlave = new IdentityTokens(
            new Dictionary<string, byte[]> { ["k1"] = Encoding.UTF8.GetBytes("llave-de-firma-uno") }, "k1");

        var token = Emisor().Issue(Afirma());

        Assert.NotNull(soloLaLlave.Verify(token, Ahora).Claims);
    }

    // ── Lo que NO se acepta ─────────────────────────────────────────────────

    [Fact] // el que da sentido a la HU: un token que no cuadra con quien dice actuar.
    public void Un_token_de_OTRA_persona_no_sirve_para_actuar_como_esta()
    {
        var emisor = Emisor();
        var (claims, _) = emisor.Verify(emisor.Issue(Afirma(Otro)), Ahora);

        // La capacidad compara: el token dice c-99 y la petición actúa como c-1.
        Assert.NotEqual(Ciudadano, claims!.Subject);

        var rechazo = IdentityTokens.SubjectMismatch(claims.Subject, Ciudadano);
        Assert.Equal("identity.token_subject_mismatch", rechazo.Code);
    }

    [Fact] // vencido no vale, y por eso los quince minutos significan algo.
    public void Un_token_vencido_no_vale()
    {
        var emisor = Emisor();
        var token = emisor.Issue(Afirma(vigenciaMin: 15));

        Assert.NotNull(emisor.Verify(token, Ahora.AddMinutes(14)).Claims);

        var (claims, motivo) = emisor.Verify(token, Ahora.AddMinutes(15));
        Assert.Null(claims);
        Assert.Equal(TokenFailure.Expired, motivo);
        Assert.Equal("identity.token_expired", IdentityTokens.ToRejection(motivo!.Value).Code);
    }

    /// <summary>
    /// Un token firmado con OTRA llave no vale, aunque su contenido sea perfecto.
    /// </summary>
    /// <remarks>
    /// Es el caso que separa «un token» de «una afirmación del llamador»: sin esto, cualquiera
    /// fabricaría uno y la HU entera sería decoración.
    /// </remarks>
    [Fact]
    public void Un_token_firmado_por_otro_no_vale()
    {
        var impostor = new IdentityTokens(
            new Dictionary<string, byte[]> { ["k1"] = Encoding.UTF8.GetBytes("llave-inventada") }, "k1");

        var (claims, motivo) = Emisor().Verify(impostor.Issue(Afirma()), Ahora);

        Assert.Null(claims);
        Assert.Equal(TokenFailure.Malformed, motivo);
    }

    [Fact] // manosear el contenido invalida la firma: no se puede reapuntar el sujeto.
    public void Cambiarle_el_sujeto_a_un_token_lo_invalida()
    {
        var emisor = Emisor();
        var token = emisor.Issue(Afirma());
        var partes = token.Split('.');

        // Se le cambia la carga por la de otro sujeto, conservando la firma original.
        var ajeno = emisor.Issue(Afirma(Otro)).Split('.')[2];
        var manoseado = $"{partes[0]}.{partes[1]}.{ajeno}.{partes[3]}";

        Assert.Null(emisor.Verify(manoseado, Ahora).Claims);
    }

    [Theory] // empty / filter: nada de esto es un token.
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-un-token")]
    [InlineData("v1.k1.solo-tres-partes")]
    [InlineData("v2.k1.YWJj.deadbeef")]
    public void Lo_que_no_es_un_token_se_rechaza(string? basura)
    {
        var (claims, motivo) = Emisor().Verify(basura, Ahora);

        Assert.Null(claims);
        Assert.Equal(TokenFailure.Malformed, motivo);
    }

    // ── La rotación ─────────────────────────────────────────────────────────

    /// <summary>
    /// Se verifica con TODAS las llaves y se firma con UNA: es lo que permite rotar.
    /// </summary>
    /// <remarks>
    /// Durante la rotación conviven la vieja —que solo verifica— y la nueva, hasta que vence el
    /// último token firmado con la vieja. Sin esto, rotar invalida todas las sesiones a la vez.
    /// </remarks>
    [Fact]
    public void Rotar_no_invalida_lo_que_ya_esta_en_la_calle()
    {
        var antes = Emisor();
        var enLaCalle = antes.Issue(Afirma());

        // Se añade k2 y se pasa a firmar con ella. k1 sigue conocida.
        var despues = Emisor("k2", ("k2", "llave-de-firma-dos"));

        Assert.NotNull(despues.Verify(enLaCalle, Ahora).Claims);          // el viejo sigue valiendo
        Assert.NotNull(despues.Verify(despues.Issue(Afirma()), Ahora).Claims);   // y el nuevo también
    }

    [Fact] // retirar la llave vieja SÍ invalida lo suyo, y con su propio motivo.
    public void Retirar_una_llave_invalida_lo_que_firmo_y_lo_dice()
    {
        var viejo = Emisor().Issue(Afirma());
        var soloLaNueva = new IdentityTokens(
            new Dictionary<string, byte[]> { ["k2"] = Encoding.UTF8.GetBytes("llave-de-firma-dos") }, "k2");

        var (claims, motivo) = soloLaNueva.Verify(viejo, Ahora);

        Assert.Null(claims);
        // UnknownKey y no Malformed: quien opera necesita distinguir «rotación mal hecha» de
        // «alguien está fabricando tokens».
        Assert.Equal(TokenFailure.UnknownKey, motivo);
        Assert.Equal("identity.token_unknown_key", IdentityTokens.ToRejection(motivo!.Value).Code);
    }

    [Fact] // un emisor sin llaves, o con una activa que no conoce, no arranca.
    public void Sin_llave_no_se_construye_el_emisor()
    {
        Assert.Throws<ArgumentException>(() => new IdentityTokens(new Dictionary<string, byte[]>(), "k1"));
        Assert.Throws<ArgumentException>(() => new IdentityTokens(
            new Dictionary<string, byte[]> { ["k1"] = new byte[] { 1 } }, "k9"));
    }

    // ── Las reglas de EMISIÓN, que no las sostiene el compilador ────────────

    private static (IdentityService Svc, IdentityTokens Tokens) Capacidad()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "id-tok-" + Guid.NewGuid().ToString("n"));
        var opciones = Microsoft.Extensions.Options.Options.Create(
            new IdentityStorageOptions { Root = raiz });
        return (new IdentityService(
            new FileSystemPrincipalStore(opciones), new FileIdempotencyLedger(raiz), TimeProvider.System),
            Emisor());
    }

    /// <summary>
    /// No se emiten tokens para sujetos que no existen.
    /// </summary>
    /// <remarks>
    /// <para><b>Este test existe porque una mutación no se pudo hacer.</b> Al intentar quitar la
    /// guarda, lo que se cayó fue el COMPILADOR —un desreferenciado de nulo— y no ningún test.
    /// O sea que la regla la sostenía la nulabilidad, que desaparece en cuanto alguien escribe
    /// <c>principal!</c>.</para>
    ///
    /// <para>Y destapó lo de fondo: las reglas vivían en el lambda del endpoint, donde no se
    /// pueden probar sin levantar el host. Se movieron a <c>IdentityService</c>
    /// (<c>CLAUDE.md</c> §15: ruteo solo en <c>Endpoints/</c>).</para>
    /// </remarks>
    [Fact]
    public void No_se_emite_token_para_un_sujeto_que_no_existe()
    {
        var (svc, _) = Capacidad();

        var r = svc.IssueToken(Ciudadano, 15);

        Assert.False(r.IsOk);
        Assert.Equal("identity.principal_not_found", r.Rejection!.Code);
    }

    [Fact] // happy: un principal registrado sí recibe token, con SUS roles.
    public void Un_principal_registrado_recibe_token_con_sus_roles()
    {
        var (svc, _) = Capacidad();
        Assert.True(svc.Register(Ciudadano, "secreto-larguisimo-de-prueba",
            new[] { "ciudadano", "declarante" }, IdempotencyKey.Of("p1")).IsOk);

        var r = svc.IssueToken(Ciudadano, 15);

        Assert.True(r.IsOk);
        Assert.Equal(Ciudadano, r.Value.Subject);
        Assert.Equal(new[] { "ciudadano", "declarante" }, r.Value.Roles);
        // La sesión empieza AHORA: es el primer token, no una renovación.
        Assert.Equal(r.Value.IssuedAtUtc, r.Value.SessionStartedAtUtc);
    }

    /// <summary>
    /// Renovar refresca los roles — es lo que acota el costo de llevarlos dentro del token.
    /// </summary>
    [Fact]
    public void Renovar_refresca_los_roles()
    {
        var (svc, tokens) = Capacidad();
        svc.Register(Ciudadano, "secreto-larguisimo-de-prueba", new[] { "ciudadano" }, IdempotencyKey.Of("p1"));
        var primero = tokens.Issue(svc.IssueToken(Ciudadano, 15).Value);

        var principal = svc.FindBySubject(Ciudadano)!;
        Assert.True(svc.GrantRoles(principal.Id, new[] { "auditor" }).IsOk);

        var r = svc.RenewToken(tokens, primero, 15, 480);

        Assert.True(r.IsOk);
        Assert.Contains("auditor", r.Value.Roles);
    }

    /// <summary>
    /// El techo de la sesión: renovar deja de funcionar aunque el token esté vigente.
    /// </summary>
    /// <remarks>
    /// Sin esto, los quince minutos serían un adorno: quien se hiciera con un token lo renovaría
    /// para siempre, de a quince minutos.
    /// </remarks>
    [Fact]
    public void Pasado_el_techo_de_la_sesion_ya_no_se_renueva()
    {
        var (svc, tokens) = Capacidad();
        svc.Register(Ciudadano, "secreto-larguisimo-de-prueba", new[] { "ciudadano" }, IdempotencyKey.Of("p1"));

        // Un token VIGENTE, pero de una sesión que empezó hace nueve horas.
        var viejo = tokens.Issue(new IdentityClaims(
            Ciudadano, new[] { "ciudadano" },
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(15),
            DateTimeOffset.UtcNow.AddHours(-9)));

        Assert.NotNull(tokens.Verify(viejo, DateTimeOffset.UtcNow).Claims);   // vigente de verdad

        var r = svc.RenewToken(tokens, viejo, 15, 480);

        Assert.False(r.IsOk);
        Assert.Equal("identity.session_expired", r.Rejection!.Code);
    }

    /// <summary>
    /// Un principal BLOQUEADO no recibe token, aunque quien lo pida jure que se autenticó.
    /// </summary>
    /// <remarks>
    /// <para><b>Este test faltaba y lo destapó una mutación</b>: quitar la guarda no ponía nada
    /// en rojo. Y es el agujero grande — el bloqueo por intentos fallidos es la defensa propia de
    /// esta capacidad, y si se puede pedir un token para un principal bloqueado, la defensa se
    /// salta entera pidiendo un token.</para>
    ///
    /// <para>El camino (b) hace esto todavía más importante: quien pide el token no trae
    /// credencial, así que el bloqueo es lo único que queda del lado de la capacidad.</para>
    /// </remarks>
    [Fact]
    public void Un_principal_bloqueado_NO_recibe_token()
    {
        var (svc, tokens) = Capacidad();
        svc.Register(Ciudadano, "secreto-larguisimo-de-prueba", new[] { "ciudadano" }, IdempotencyKey.Of("p1"));

        for (var i = 0; i < IdentityRules.MaxFailedAttempts; i++)
        {
            svc.Authenticate(Ciudadano, "esta-no-es");
        }
        Assert.True(svc.FindBySubject(Ciudadano)!.IsLocked(DateTimeOffset.UtcNow));

        var r = svc.IssueToken(Ciudadano, 15);

        Assert.False(r.IsOk);
        Assert.Equal("identity.principal_locked", r.Rejection!.Code);
    }

    [Fact] // y tampoco se le RENUEVA: bloquear a alguien con sesión abierta tiene que morder ya.
    public void A_un_principal_bloqueado_tampoco_se_le_renueva()
    {
        var (svc, tokens) = Capacidad();
        svc.Register(Ciudadano, "secreto-larguisimo-de-prueba", new[] { "ciudadano" }, IdempotencyKey.Of("p1"));
        var vigente = tokens.Issue(svc.IssueToken(Ciudadano, 15).Value);

        for (var i = 0; i < IdentityRules.MaxFailedAttempts; i++)
        {
            svc.Authenticate(Ciudadano, "esta-no-es");
        }

        var r = svc.RenewToken(tokens, vigente, 15, 480);

        Assert.False(r.IsOk);
        Assert.Equal("identity.principal_locked", r.Rejection!.Code);
    }

    [Fact] // un token vencido NO se renueva: si no, la vigencia corta no significaría nada.
    public void Un_token_vencido_no_se_renueva()
    {
        var (svc, tokens) = Capacidad();
        svc.Register(Ciudadano, "secreto-larguisimo-de-prueba", new[] { "ciudadano" }, IdempotencyKey.Of("p1"));

        var vencido = tokens.Issue(new IdentityClaims(
            Ciudadano, new[] { "ciudadano" },
            DateTimeOffset.UtcNow.AddMinutes(-30), DateTimeOffset.UtcNow.AddMinutes(-15),
            DateTimeOffset.UtcNow.AddMinutes(-30)));

        var r = svc.RenewToken(tokens, vencido, 15, 480);

        Assert.False(r.IsOk);
        Assert.Equal("identity.token_expired", r.Rejection!.Code);
    }

    // ── El techo de la sesión ───────────────────────────────────────────────

    /// <summary>
    /// El token lleva CUÁNDO empezó la sesión, no solo cuándo se emitió.
    /// </summary>
    /// <remarks>
    /// Es lo que hace que quince minutos signifiquen algo: sin este dato, renovar sería
    /// indistinguible de empezar de nuevo y un token robado se renovaría para siempre.
    /// </remarks>
    [Fact]
    public void El_token_recuerda_cuando_empezo_la_SESION_y_no_solo_el_token()
    {
        var emisor = Emisor();
        var sesionDesde = Ahora.AddHours(-7);

        // Un token renovado: emitido ahora, pero de una sesión de hace siete horas.
        var renovado = emisor.Issue(Afirma(sesionDesde: sesionDesde));

        var claims = emisor.Verify(renovado, Ahora).Claims!;
        Assert.Equal(sesionDesde, claims.SessionStartedAtUtc);
        Assert.NotEqual(claims.SessionStartedAtUtc, claims.IssuedAtUtc);
    }

    /// <summary>
    /// Los cuatro rechazos son <c>Invalid</c> y no <c>Unauthorized</c>.
    /// </summary>
    /// <remarks>
    /// El 401 está reservado para la llave compartida, que es la única que dice «no sos de acá».
    /// Que un token no sirva es un problema de la petición, no de quién la manda — y confundirlos
    /// haría que un token vencido pareciera un fallo de despliegue.
    /// </remarks>
    [Fact]
    public void Los_rechazos_del_token_son_de_la_PETICION_y_no_de_quien_llama()
    {
        foreach (var motivo in Enum.GetValues<TokenFailure>())
        {
            var r = IdentityTokens.ToRejection(motivo);
            Assert.StartsWith("identity.token_", r.Code, StringComparison.Ordinal);
            Assert.Equal(RejectionKind.Invalid, r.Kind);
        }

        Assert.Equal(RejectionKind.Invalid, IdentityTokens.SubjectMismatch(Otro, Ciudadano).Kind);
    }
}
