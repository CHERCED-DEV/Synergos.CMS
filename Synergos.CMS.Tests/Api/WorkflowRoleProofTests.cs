using System.Text;
using Synergos.Api.Workflow.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// De dónde salen los roles de quien dispara una transición (defecto #48).
/// </summary>
/// <remarks>
/// <para><b>Es la mitad que no tenía tests, y por eso el defecto vivió.</b> Los que había
/// construían el <c>Actor</c> a mano y probaban la regla —que era correcta: <c>HasAnyRole</c>
/// funciona—. Nadie miraba <b>de dónde salían los roles</b>, que es lo que decide si la guarda
/// vale algo. Misma forma que el defecto #42: regla bien, fuente del dato mal.</para>
/// </remarks>
public sealed class WorkflowRoleProofTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly Ref Funcionario = Ref.Create("gov.funcionario", "f-1");
    private static readonly Ref Ciudadano = Ref.Create("gov.ciudadano", "c-1");

    private static IdentityTokens Emisor()
        => new(new Dictionary<string, byte[]> { ["k1"] = Encoding.UTF8.GetBytes("llave-de-firma-uno") }, "k1");

    private static IdentityTokenGate ConLlave() => new(Emisor());
    private static IdentityTokenGate SinLlave() => new(null);

    private static string Token(Ref sujeto, params string[] roles)
        => Emisor().Issue(new IdentityClaims(sujeto, roles, Ahora, Ahora.AddMinutes(15), Ahora));

    // ── De dónde salen los roles ────────────────────────────────────────────

    /// <summary>
    /// Con token, los roles salen del token y lo declarado se IGNORA.
    /// </summary>
    /// <remarks>
    /// Creerle al cuerpo teniendo el token dejaría que un llamador se ascendiera presentando uno
    /// honesto y pidiendo además el rol que le falta — el token sería un adorno que da permiso.
    /// </remarks>
    [Fact]
    public void Con_token_mandan_los_roles_del_token_y_no_los_declarados()
    {
        var r = WorkflowRules.ResolveActor(
            ConLlave(), Token(Funcionario, "ventanilla"), Funcionario,
            new[] { "funcionario", "auditor" }, Ahora);

        Assert.True(r.IsOk);
        Assert.True(r.Value.Verified);
        Assert.True(r.Value.Actor.HasAnyRole("ventanilla"));
        Assert.False(r.Value.Actor.HasAnyRole("funcionario"));
        Assert.False(r.Value.Actor.HasAnyRole("auditor"));
    }

    [Fact] // sin token se aceptan los declarados, pero NO cuentan como probados.
    public void Sin_token_los_roles_declarados_valen_pero_no_estan_verificados()
    {
        var r = WorkflowRules.ResolveActor(
            ConLlave(), null, Funcionario, new[] { "funcionario" }, Ahora);

        Assert.True(r.IsOk);
        Assert.False(r.Value.Verified);
        Assert.True(r.Value.Actor.HasAnyRole("funcionario"));
    }

    /// <summary>El token de OTRO no sirve para actuar como éste.</summary>
    /// <remarks>
    /// Sin esta comprobación el token sería decoración y la capacidad seguiría creyendo el actor
    /// que le mandan — la lección de la HU #14 rebanada 3.
    /// </remarks>
    [Fact]
    public void El_token_de_otro_sujeto_se_rechaza()
    {
        var r = WorkflowRules.ResolveActor(
            ConLlave(), Token(Ciudadano, "funcionario"), Funcionario, null, Ahora);

        Assert.False(r.IsOk);
        Assert.Equal("identity.token_subject_mismatch", r.Rejection!.Code);
    }

    [Fact] // vencido no vale, y no degrada a «declarado».
    public void Un_token_vencido_se_rechaza_y_NO_degrada()
    {
        var r = WorkflowRules.ResolveActor(
            ConLlave(), Token(Funcionario, "funcionario"), Funcionario, null, Ahora.AddMinutes(20));

        Assert.False(r.IsOk);
        Assert.Equal("identity.token_expired", r.Rejection!.Code);
    }

    [Fact] // presentarlo donde nadie puede comprobarlo se RECHAZA, no se ignora.
    public void Un_token_presentado_sin_llave_se_rechaza()
    {
        var r = WorkflowRules.ResolveActor(
            SinLlave(), Token(Funcionario, "funcionario"), Funcionario, null, Ahora);

        Assert.False(r.IsOk);
        Assert.Equal("identity.token_not_verifiable", r.Rejection!.Code);
    }

    // ── Qué hace la guarda con esa prueba ───────────────────────────────────

    private static WorkflowDefinition Definicion() => new(
        "d1", "gov.tramite", "submitted", new[] { "approved" },
        new[]
        {
            new TransitionRule("approve", "submitted", "approved", new[] { "funcionario" }),
            new TransitionRule("comentar", "submitted", "submitted", Array.Empty<string>()),
        });

    private static WorkflowInstance Instancia() => new(
        "i1", "gov.tramite", Ref.Create("gov.expediente", "e-1"), "submitted",
        Array.Empty<HistoryEntry>(), Ahora);

    [Fact] // happy: rol verificado pasa la guarda incluso con la postura estricta.
    public void Un_rol_verificado_pasa_la_guarda_estricta()
    {
        var actor = Actor.Of(Funcionario, "funcionario");

        var r = WorkflowRules.Resolve(Definicion(), Instancia(), "approve", actor,
            verified: true, requireVerified: true);

        Assert.True(r.IsOk);
    }

    /// <summary>
    /// Con la postura estricta, un rol DECLARADO no alcanza.
    /// </summary>
    /// <remarks>
    /// Es el interruptor que hace que este arreglo no sea teórico: un despliegue que ya tenga
    /// identidad cierra el agujero, sin romper al que todavía no la tiene.
    /// </remarks>
    [Fact]
    public void Con_la_postura_estricta_un_rol_declarado_NO_alcanza()
    {
        var actor = Actor.Of(Funcionario, "funcionario");

        var r = WorkflowRules.Resolve(Definicion(), Instancia(), "approve", actor,
            verified: false, requireVerified: true);

        Assert.False(r.IsOk);
        Assert.Equal("workflow.roles_not_verified", r.Rejection!.Code);
    }

    [Fact] // el default no rompe a nadie: sin postura estricta, lo declarado sigue valiendo.
    public void Sin_postura_estricta_lo_declarado_sigue_valiendo()
    {
        var actor = Actor.Of(Funcionario, "funcionario");

        var r = WorkflowRules.Resolve(Definicion(), Instancia(), "approve", actor);

        Assert.True(r.IsOk);
    }

    /// <summary>
    /// La postura estricta sólo muerde donde hay rol que exigir.
    /// </summary>
    /// <remarks>
    /// Si tumbara también las transiciones sin guarda, encenderla pararía el sistema entero por
    /// una razón que no tiene nada que ver con quién avanza qué.
    /// </remarks>
    [Fact]
    public void La_postura_estricta_no_toca_las_transiciones_sin_rol()
    {
        var actor = Actor.Of(Ciudadano);

        var r = WorkflowRules.Resolve(Definicion(), Instancia(), "comentar", actor,
            verified: false, requireVerified: true);

        Assert.True(r.IsOk);
    }

    [Fact] // y sin el rol correcto se rechaza como siempre, verificado o no.
    public void Sin_el_rol_correcto_se_rechaza_igual()
    {
        foreach (var verificado in new[] { true, false })
        {
            var r = WorkflowRules.Resolve(Definicion(), Instancia(), "approve",
                Actor.Of(Ciudadano, "ciudadano"), verificado, requireVerified: false);

            Assert.False(r.IsOk);
            Assert.Equal("workflow.role_required", r.Rejection!.Code);
        }
    }
}
