using System.Text;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// De dónde sale el actor de un asiento de bitácora, y con qué queda respaldado (#72).
/// </summary>
/// <remarks>
/// <para><b>Es la mitad que no tenía tests, tres veces seguidas.</b> Los de
/// <c>AuditServiceTests</c> construyen el <c>Actor</c> a mano y prueban las reglas —que están
/// bien: sin actor se rechaza, sin acción se rechaza—. Nadie miraba <b>de dónde salía el
/// actor</b>, que es lo que decide si el asiento se puede sostener. Misma forma que #42 y que
/// #48: regla bien, fuente del dato mal.</para>
///
/// <para><b>Y acá duele más que en las otras dos.</b> La bitácora se blindó desde el principio
/// contra reescribir el pasado —sin <c>PUT</c> ni <c>DELETE</c>— y quedó abierta a fabricarlo. Un
/// asiento falso no falla: se guarda, para siempre, con aspecto de prueba.</para>
/// </remarks>
public sealed class AuditActorProofTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly Ref Director = Ref.Create("gov.funcionario", "director@entidad.gov.co");
    private static readonly Ref Ventanilla = Ref.Create("gov.funcionario", "ventanilla@entidad.gov.co");

    private const string Prefijo = "audit";

    private static IdentityTokens Emisor()
        => new(new Dictionary<string, byte[]> { ["k1"] = Encoding.UTF8.GetBytes("llave-de-firma-uno") }, "k1");

    private static IdentityTokenGate ConLlave() => new(Emisor());
    private static IdentityTokenGate SinLlave() => new(null);

    private static string Token(Ref sujeto, params string[] roles)
        => Emisor().Issue(new IdentityClaims(sujeto, roles, Ahora, Ahora.AddMinutes(15), Ahora));

    private static Result<(Actor Actor, IdentityAssertion Assertion)> Resolver(
        IdentityTokenGate gate, string? token, Ref principal,
        IReadOnlyList<string>? rolesDeclarados = null,
        IdentityAssertion? declarada = IdentityAssertion.CmsSession,
        DateTimeOffset? cuando = null)
        => IdentityAssertions.ResolveActor(
            gate, token, principal, rolesDeclarados, declarada, cuando ?? Ahora, Prefijo);

    // ── El defecto que cierra ────────────────────────────────────────────────

    /// <summary>
    /// El token de OTRO no sirve para asentar a nombre de éste.
    /// </summary>
    /// <remarks>
    /// Es el caso que justifica la HU entera: sin comprobar que el sujeto del token es el actor
    /// de la petición, la bitácora seguiría creyendo el actor que le mandan y el token sería
    /// decoración.
    /// </remarks>
    [Fact]
    public void El_token_de_otro_sujeto_no_sirve_para_asentar_a_su_nombre()
    {
        var r = Resolver(ConLlave(), Token(Ventanilla, "funcionario"), Director);

        Assert.False(r.IsOk);
        Assert.Equal("identity.token_subject_mismatch", r.Rejection!.Code);
    }

    /// <summary>Con token, los roles salen del token y lo declarado se IGNORA.</summary>
    /// <remarks>
    /// Es el defecto #48 en esta capacidad: creerle al cuerpo teniendo el token dejaría que
    /// alguien se asentara como <c>admin</c> presentando un token honesto de ventanilla.
    /// </remarks>
    [Fact]
    public void Con_token_mandan_los_roles_del_token_y_no_los_declarados()
    {
        var r = Resolver(ConLlave(), Token(Director, "ventanilla"), Director,
            rolesDeclarados: new[] { "admin", "auditor" });

        Assert.True(r.IsOk);
        Assert.Equal(IdentityAssertion.IdentityToken, r.Value.Assertion);
        Assert.True(r.Value.Actor.HasAnyRole("ventanilla"));
        Assert.False(r.Value.Actor.HasAnyRole("admin"));
        Assert.False(r.Value.Actor.HasAnyRole("auditor"));
    }

    /// <summary>Declarar la afirmación fuerte sin presentarla se rechaza.</summary>
    /// <remarks>
    /// Es el defecto #42 exacto: si se aceptara, quedaría un asiento diciendo que se comprobó un
    /// token que nunca existió — y en una bitácora eso no se puede corregir después.
    /// </remarks>
    [Fact]
    public void Declarar_IdentityToken_sin_presentarlo_se_rechaza()
    {
        var r = Resolver(ConLlave(), token: null, Director, declarada: IdentityAssertion.IdentityToken);

        Assert.False(r.IsOk);
        Assert.Equal("identity.assertion_not_proven", r.Rejection!.Code);
    }

    // ── Lo que sigue funcionando igual ───────────────────────────────────────

    /// <summary>Sin token se sigue asentando, y el asiento dice que fue con la palabra de quien llamó.</summary>
    /// <remarks>
    /// <b>Parar la bitácora cuando falla la identidad sería peor que un asiento débil</b>: dejaría
    /// un hueco en el registro justo cuando algo va mal, que es cuando el registro importa.
    /// </remarks>
    [Fact]
    public void Sin_token_se_asienta_igual_y_queda_como_CmsSession()
    {
        var r = Resolver(ConLlave(), token: null, Director, rolesDeclarados: new[] { "funcionario" });

        Assert.True(r.IsOk);
        Assert.Equal(IdentityAssertion.CmsSession, r.Value.Assertion);
        Assert.True(r.Value.Actor.HasAnyRole("funcionario"));
    }

    [Fact] // presentarlo donde nadie puede comprobarlo se RECHAZA, no se ignora.
    public void Un_token_presentado_sin_llave_se_rechaza()
    {
        var r = Resolver(SinLlave(), Token(Director, "funcionario"), Director);

        Assert.False(r.IsOk);
        Assert.Equal("identity.token_not_verifiable", r.Rejection!.Code);
    }

    [Fact] // vencido no vale, y no degrada a «declarado».
    public void Un_token_vencido_se_rechaza_y_NO_degrada()
    {
        var r = Resolver(ConLlave(), Token(Director, "funcionario"), Director, cuando: Ahora.AddMinutes(20));

        Assert.False(r.IsOk);
        Assert.Equal("identity.token_expired", r.Rejection!.Code);
    }

    // ── Lo que el asiento guarda ─────────────────────────────────────────────

    /// <summary>Un asiento anterior a #72 dice «no consta», y eso es la verdad.</summary>
    /// <remarks>
    /// Rellenarlo con <c>CmsSession</c> sería inventar una afirmación que nadie hizo —#42 con otro
    /// disfraz— y además exigiría reescribir un registro append-only, que es lo único que lo
    /// inutiliza del todo.
    /// </remarks>
    [Fact]
    public void Un_asiento_sin_afirmacion_dice_que_no_consta()
    {
        var viejo = new Synergos.Api.Audit.Domain.AuditEntry(
            "e1", Actor.Of(Director, "funcionario"), "expediente.aprobado",
            Ref.Create("gov.expediente", "SG-2026-000001"), Ahora,
            new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Null(viejo.ActedWith);
    }

    [Fact]
    public void Un_asiento_nuevo_guarda_con_que_se_afirmo()
    {
        var nuevo = new Synergos.Api.Audit.Domain.AuditEntry(
            "e2", Actor.Of(Director, "funcionario"), "expediente.aprobado",
            Ref.Create("gov.expediente", "SG-2026-000001"), Ahora,
            new Dictionary<string, string>(StringComparer.Ordinal),
            IdentityAssertion.IdentityToken);

        Assert.Equal(IdentityAssertion.IdentityToken, nuevo.ActedWith);
    }
}
