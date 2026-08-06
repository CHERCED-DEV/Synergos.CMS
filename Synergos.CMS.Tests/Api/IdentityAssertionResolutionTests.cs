using System.Text;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Quién decide con qué fuerza se afirmó una identidad — <b>la capacidad, no el llamador</b>
/// (HU #14, rebanada 3).
/// </summary>
/// <remarks>
/// <para><b>Es el cambio entero de la HU.</b> Antes, quien llamaba mandaba <c>assertion</c> en el
/// cuerpo y se le creía: cualquiera con la llave compartida podía anotar un acceso como
/// respaldado por un token que nunca existió — y el propio servicio lo hacía (defecto #42). Ahora
/// se le cree solo lo que se puede comprobar.</para>
///
/// <para><b>Y vive junto al token y no en cada capacidad</b> porque es el contrato: si cada una
/// interpretara por su cuenta qué vale un token presentado, el mismo acceso quedaría anotado con
/// distinta fuerza según a quién se le pidiera, y el campo dejaría de servir para comparar.</para>
/// </remarks>
public sealed class IdentityAssertionResolutionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 6, 9, 0, 0, TimeSpan.Zero);
    private static readonly Ref Ciudadano = Ref.Create("gov.ciudadano", "c-1");
    private static readonly Ref Otro = Ref.Create("gov.ciudadano", "c-99");
    private const string Prefijo = "messaging";

    private static IdentityTokens Emisor()
        => new(new Dictionary<string, byte[]> { ["k1"] = Encoding.UTF8.GetBytes("llave-de-firma-uno") }, "k1");

    private static IdentityTokenGate ConLlave() => new(Emisor());
    private static IdentityTokenGate SinLlave() => new(null);

    private static string Token(Ref sujeto, int vigenciaMin = 15)
        => Emisor().Issue(new IdentityClaims(
            sujeto, new[] { "ciudadano" }, Ahora, Ahora.AddMinutes(vigenciaMin), Ahora));

    // ── Con token ───────────────────────────────────────────────────────────

    [Fact] // happy: token válido del mismo sujeto → la afirmación SUBE, la haya declarado o no.
    public void Un_token_valido_del_mismo_sujeto_vale_IdentityToken()
    {
        var (assertion, motivo) = IdentityAssertions.Resolve(
            ConLlave(), Token(Ciudadano), Ciudadano, IdentityAssertion.CmsSession, Ahora, Prefijo);

        Assert.Null(motivo);
        // Declaró la débil y presentó la fuerte: se anota lo que se pudo comprobar, no lo que dijo.
        Assert.Equal(IdentityAssertion.IdentityToken, assertion);
    }

    /// <summary>
    /// El token de OTRA persona no sirve para actuar como ésta.
    /// </summary>
    /// <remarks>
    /// <b>Es el caso que el camino A no puede detectar</b> y el que justifica toda la HU: sin
    /// esta comprobación, una capacidad sigue creyendo el <c>who</c> que le mandan y el token es
    /// decoración.
    /// </remarks>
    [Fact]
    public void Un_token_de_OTRO_sujeto_se_rechaza()
    {
        var (assertion, motivo) = IdentityAssertions.Resolve(
            ConLlave(), Token(Otro), Ciudadano, IdentityAssertion.CmsSession, Ahora, Prefijo);

        Assert.Null(assertion);
        Assert.Equal("identity.token_subject_mismatch", motivo!.Code);
    }

    [Fact] // vencido no vale, y no degrada a CmsSession: se rechaza.
    public void Un_token_vencido_se_rechaza_y_NO_degrada()
    {
        var (assertion, motivo) = IdentityAssertions.Resolve(
            ConLlave(), Token(Ciudadano), Ciudadano, IdentityAssertion.CmsSession,
            Ahora.AddMinutes(20), Prefijo);

        Assert.Null(assertion);
        Assert.Equal("identity.token_expired", motivo!.Code);
    }

    [Fact] // fabricado por otro no vale.
    public void Un_token_firmado_por_otro_se_rechaza()
    {
        var impostor = new IdentityTokens(
            new Dictionary<string, byte[]> { ["k1"] = Encoding.UTF8.GetBytes("llave-inventada") }, "k1");
        var falso = impostor.Issue(new IdentityClaims(
            Ciudadano, Array.Empty<string>(), Ahora, Ahora.AddMinutes(15), Ahora));

        var (assertion, motivo) = IdentityAssertions.Resolve(
            ConLlave(), falso, Ciudadano, IdentityAssertion.CmsSession, Ahora, Prefijo);

        Assert.Null(assertion);
        Assert.Equal("identity.token_malformed", motivo!.Code);
    }

    /// <summary>
    /// Presentar un token donde nadie puede comprobarlo se RECHAZA, no se ignora.
    /// </summary>
    /// <remarks>
    /// Ignorarlo dejaría que alguien mandara cualquier cosa y siguiera adelante como si no
    /// hubiera mandado nada — peor que no aceptar tokens, porque quien lo manda cree que está
    /// probando algo.
    /// </remarks>
    [Fact]
    public void Un_token_presentado_donde_no_hay_llave_se_RECHAZA()
    {
        var (assertion, motivo) = IdentityAssertions.Resolve(
            SinLlave(), Token(Ciudadano), Ciudadano, IdentityAssertion.CmsSession, Ahora, Prefijo);

        Assert.Null(assertion);
        Assert.Equal("identity.token_not_verifiable", motivo!.Code);
    }

    // ── Sin token ───────────────────────────────────────────────────────────

    [Fact] // sin token, la afirmación más fuerte que se acepta es la honesta.
    public void Sin_token_se_acepta_CmsSession_y_nada_mas()
    {
        var (assertion, motivo) = IdentityAssertions.Resolve(
            ConLlave(), null, Ciudadano, IdentityAssertion.CmsSession, Ahora, Prefijo);

        Assert.Null(motivo);
        Assert.Equal(IdentityAssertion.CmsSession, assertion);
    }

    /// <summary>
    /// Declarar una afirmación fuerte SIN presentarla se rechaza.
    /// </summary>
    /// <remarks>
    /// <para><b>Es exactamente el agujero que el defecto #42 dejó a la vista.</b> Si un llamador
    /// pudiera seguir diciendo <c>IdentityToken</c> sin traer uno, todo lo demás sería
    /// decoración: el archivo volvería a decir que se probó algo que no se probó.</para>
    ///
    /// <para><c>GovFederation</c> cae por lo mismo, y a propósito: nadie puede verificarla
    /// todavía, así que aceptarla sería creerle al llamador la afirmación más fuerte de todas.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(IdentityAssertion.IdentityToken)]
    [InlineData(IdentityAssertion.GovFederation)]
    public void Declarar_lo_fuerte_sin_probarlo_se_rechaza(IdentityAssertion declarada)
    {
        var (assertion, motivo) = IdentityAssertions.Resolve(
            ConLlave(), null, Ciudadano, declarada, Ahora, Prefijo);

        Assert.Null(assertion);
        Assert.Equal("identity.assertion_not_proven", motivo!.Code);
    }

    [Fact] // empty: sin decir nada y sin token no se registra nada, como antes.
    public void Sin_decir_nada_y_sin_token_no_se_registra_nada()
    {
        var (assertion, motivo) = IdentityAssertions.Resolve(
            ConLlave(), null, Ciudadano, null, Ahora, Prefijo);

        Assert.Null(assertion);
        // El código lo pone la capacidad que pregunta: el rechazo es suyo, no del token.
        Assert.Equal("messaging.access_requires_identity", motivo!.Code);
    }

    [Fact] // una cabecera vacía es «no presentó», no «presentó algo raro».
    public void Una_cabecera_vacia_es_no_haber_presentado_nada()
    {
        foreach (var vacia in new[] { "", "   " })
        {
            var (assertion, motivo) = IdentityAssertions.Resolve(
                ConLlave(), vacia, Ciudadano, IdentityAssertion.CmsSession, Ahora, Prefijo);

            Assert.Null(motivo);
            Assert.Equal(IdentityAssertion.CmsSession, assertion);
        }
    }

    /// <summary>
    /// Con llave configurada o sin ella, <b>nunca</b> sale <c>IdentityToken</c> sin token.
    /// </summary>
    /// <remarks>
    /// Es la invariante que resume la rebanada: la afirmación fuerte solo puede salir de algo que
    /// esta capacidad comprobó. Si algún camino la produjera sin token, la HU no habría cambiado
    /// nada.
    /// </remarks>
    [Fact]
    public void Ninguna_combinacion_SIN_token_produce_IdentityToken()
    {
        foreach (var gate in new[] { ConLlave(), SinLlave() })
        {
            foreach (var declarada in new IdentityAssertion?[]
                     {
                         null, IdentityAssertion.CmsSession,
                         IdentityAssertion.IdentityToken, IdentityAssertion.GovFederation,
                     })
            {
                var (assertion, _) = IdentityAssertions.Resolve(
                    gate, null, Ciudadano, declarada, Ahora, Prefijo);

                Assert.NotEqual(IdentityAssertion.IdentityToken, assertion);
            }
        }
    }
}
