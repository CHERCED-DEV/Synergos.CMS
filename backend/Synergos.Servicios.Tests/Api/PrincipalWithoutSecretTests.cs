using Microsoft.Extensions.Options;
using Synergos.Api.Identity.Domain;
using Synergos.Api.Identity.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// Un principal puede existir SIN credencial (HU #14, rebanada 4).
/// </summary>
/// <remarks>
/// <para><b>Por qué hacía falta.</b> Un token se emite para un principal que exista, y quien
/// actúa desde el CMS ya entró por otra puerta: su sesión de Umbraco. Exigirle una contraseña
/// obligaba a quien lo da de alta a inventarse una por persona y a custodiarla — o sea a
/// fabricar credenciales que nadie usa. Una credencial que no se usa es sólo superficie de
/// ataque.</para>
///
/// <para><b>Y el detalle que no es obvio:</b> intentar autenticar a uno de estos NO cuenta como
/// intento fallido. Si contara, cualquiera podría bloquear a una persona que ni siquiera entra
/// por ahí mandando cinco peticiones con cualquier contraseña — una negación de servicio por una
/// puerta que esa persona no usa.</para>
/// </remarks>
public sealed class PrincipalWithoutSecretTests
{
    private static readonly Ref Funcionario = Ref.Create("gov.funcionario", "ana@entidad.gov.co");

    private static IdentityService Capacidad()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "id-sin-secreto-" + Guid.NewGuid().ToString("n"));
        var opciones = Options.Create(new IdentityStorageOptions { Root = raiz });
        return new IdentityService(
            new FileSystemPrincipalStore(opciones), new FileIdempotencyLedger(raiz), TimeProvider.System);
    }

    private static IdentityService ConFuncionario(out Principal principal)
    {
        var svc = Capacidad();
        var r = svc.Register(Funcionario, null, new[] { "funcionario" }, IdempotencyKey.Of("alta-1"));
        Assert.True(r.IsOk, $"El alta sin credencial se rechazó: {r.Rejection}");
        principal = r.Value;
        return svc;
    }

    /// <summary>Se da de alta sin credencial, y queda sin credencial — no con una vacía.</summary>
    /// <remarks>
    /// La diferencia importa: una derivación de la cadena vacía sería una contraseña que alguien
    /// puede acertar escribiendo nada.
    /// </remarks>
    [Fact]
    public void Se_registra_sin_credencial()
    {
        _ = ConFuncionario(out var principal);

        Assert.Null(principal.Secret);
        Assert.Equal(new[] { "funcionario" }, principal.Roles);
    }

    /// <summary>Y con eso ya se le puede emitir un token, que es para lo que existe.</summary>
    [Fact]
    public void Sin_credencial_se_le_emite_token_igual()
    {
        var svc = ConFuncionario(out _);

        var token = svc.IssueToken(Funcionario, 15);

        Assert.True(token.IsOk, $"No se pudo emitir: {token.Rejection}");
        Assert.Equal(new[] { "funcionario" }, token.Value.Roles);
        Assert.Equal(Funcionario, token.Value.Subject);
    }

    /// <summary>
    /// Autenticarlo por credencial se rechaza con su propio código, no con «credenciales inválidas».
    /// </summary>
    /// <remarks>
    /// Decir «inválidas» invita a reintentar algo que no puede funcionar nunca. Esto no es una
    /// contraseña equivocada: es una puerta que este principal no tiene.
    /// </remarks>
    [Fact]
    public void Sin_credencial_no_se_autentica_por_credencial()
    {
        var svc = ConFuncionario(out _);

        var r = svc.Authenticate(Funcionario, "lo-que-sea-largo");

        Assert.False(r.IsOk);
        Assert.Equal("identity.no_credential", r.Rejection!.Code);
    }

    /// <summary>
    /// Y esos intentos NO lo bloquean.
    /// </summary>
    /// <remarks>
    /// <b>Es la mitad que importa de la regla anterior.</b> Con el contador corriendo, cinco
    /// peticiones de cualquiera dejarían al funcionario bloqueado —y con él, sus tokens— por una
    /// puerta que no usa. El ataque no necesitaría ni acertar.
    /// </remarks>
    [Fact]
    public void Los_intentos_contra_uno_sin_credencial_no_lo_bloquean()
    {
        var svc = ConFuncionario(out _);

        for (var i = 0; i < IdentityRules.MaxFailedAttempts + 2; i++)
        {
            svc.Authenticate(Funcionario, $"intento-numero-{i}");
        }

        // Lo que de verdad se comprueba: sigue pudiendo actuar.
        var token = svc.IssueToken(Funcionario, 15);
        Assert.True(token.IsOk, $"Quedó bloqueado por intentos que no debían contar: {token.Rejection}");
    }

    /// <summary>Lo que NO cambia: media credencial se sigue rechazando.</summary>
    /// <remarks>
    /// No traerla es una decisión; traerla débil es un descuido, y dejarla pasar sería peor que
    /// no tenerla porque parece protección.
    /// </remarks>
    [Fact]
    public void Una_credencial_debil_se_sigue_rechazando()
    {
        var svc = Capacidad();

        var r = svc.Register(Funcionario, "corta", new[] { "funcionario" }, IdempotencyKey.Of("alta-2"));

        Assert.False(r.IsOk);
        Assert.Equal("identity.weak_secret", r.Rejection!.Code);
    }
}
