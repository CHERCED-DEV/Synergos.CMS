using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Synergos.Core;

namespace Synergos.Shared;

/// <summary>
/// Cablea el emisor/verificador de tokens de identidad en un servicio.
/// </summary>
/// <remarks>
/// <para><b>Vive acá porque ya hay dos consumidores</b> (<c>CLAUDE.md</c> §17): <c>Api.Identity</c>,
/// que emite, y la primera capacidad que verifica. Y lo que se comparte no es solo el código: es
/// que <b>la sección de configuración se llame igual en todos</b>. Con un nombre por servicio,
/// configurar la llave en uno y olvidarla en otro produce un token válido que una capacidad
/// rechaza — de los peores síntomas de diagnosticar, porque todo «parece bien».</para>
///
/// <para><b>Sin llave el servicio NO arranca cuando la exige.</b> Un servicio que dice verificar
/// identidades y no puede es peor que uno caído: parece que funciona y deja pasar todo.</para>
/// </remarks>
public static class IdentityTokenSetup
{
    /// <summary>El nombre de la sección, idéntico en los 22 servicios.</summary>
    public const string Section = "IdentityTokens";

    /// <param name="builder">El host.</param>
    /// <param name="required">
    /// <c>true</c> en quien emite: sin llave, el arranque falla. <c>false</c> en quien solo
    /// verifica y todavía puede operar sin tokens — un clon limpio arranca sin configurar nada.
    /// </param>
    /// <remarks>
    /// <para><b>Con <paramref name="required"/> se registra además <see cref="IdentityTokens"/> a
    /// secas</b>, para que quien lo exige lo inyecte sin comprobar nulos. Quien tolera su
    /// ausencia inyecta <see cref="IdentityTokenGate"/> y pregunta. Son dos formas porque son
    /// dos situaciones distintas, y colapsarlas obligaría al emisor a defenderse de un caso que
    /// su propio arranque ya hizo imposible.</para>
    ///
    /// <para><b>La llave se lee AQUÍ y no dentro de una fábrica</b>, y no es cosmético. Con
    /// <c>AddSingleton(sp =&gt; ...)</c> el fallo es perezoso: en una API mínima nadie resuelve el
    /// servicio hasta la primera petición que lo inyecta, así que un despliegue sin llave
    /// arrancaba <b>verde</b>, contestaba <c>/health</c>, pasaba la prueba de humo y solo
    /// reventaba cuando una persona de verdad intentaba entrar. Es justo el «parece que funciona»
    /// que este método dice impedir. Lo destapó levantar el proceso, no un test.</para>
    /// </remarks>
    public static IHostApplicationBuilder AddIdentityTokens(this IHostApplicationBuilder builder, bool required)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var seccion = builder.Configuration.GetSection(Section);
        builder.Services.Configure<IdentityTokenOptions>(seccion);

        var o = seccion.Get<IdentityTokenOptions>() ?? new IdentityTokenOptions();
        var llaves = o.Keys
            .Where(k => !string.IsNullOrWhiteSpace(k.Value))
            .ToDictionary(k => k.Key, k => System.Text.Encoding.UTF8.GetBytes(k.Value), StringComparer.Ordinal);

        if (llaves.Count == 0 && required)
        {
            throw new InvalidOperationException(
                $"{Section}:Keys está vacío. Sin llave de firma no se pueden emitir ni verificar "
                + "tokens de identidad. NO es la misma que la llave compartida — ver IdentityTokenOptions.");
        }

        var gate = new IdentityTokenGate(
            llaves.Count == 0 ? null : new IdentityTokens(llaves, o.ActiveKeyId));

        builder.Services.AddSingleton(gate);

        if (required)
        {
            builder.Services.AddSingleton(gate.Tokens!);
        }

        return builder;
    }
}

/// <summary>
/// El verificador de tokens, que <b>puede no estar configurado</b>.
/// </summary>
/// <param name="Tokens">El verificador, o <c>null</c> si no hay llave.</param>
/// <remarks>
/// <para><b>Existe para que «no hay llave» sea un caso que se mira y no un nulo que se olvida.</b>
/// Una capacidad que todavía no exige token tiene que poder arrancar en un clon limpio — pero el
/// día que alguien mande un token, tiene que quedar claro si se está verificando de verdad o si
/// se está aceptando sin mirar.</para>
///
/// <para><b>Y ésa es la diferencia que importa:</b> sin llave, un token presentado se RECHAZA. No
/// se ignora. Ignorarlo dejaría que alguien mandara cualquier cosa y consiguiera que el registro
/// dijera lo que quiso — que es exactamente lo que la HU #14 existe para impedir.</para>
/// </remarks>
public sealed record IdentityTokenGate(IdentityTokens? Tokens)
{
    /// <summary>Si este servicio puede verificar tokens.</summary>
    public bool Configured => Tokens is not null;
}

/// <summary>
/// Qué significa presentar —o no presentar— un token, para TODAS las capacidades.
/// </summary>
/// <remarks>
/// <para><b>Vive junto al token y no en cada capacidad a propósito.</b> Es el contrato: si cada
/// una interpretara por su cuenta qué vale un token presentado, el mismo acceso quedaría anotado
/// con distinta fuerza según a quién se le pidiera — y el campo dejaría de servir para comparar.
/// </para>
///
/// <para><b>La afirmación la decide la CAPACIDAD, nunca el llamador.</b> Ése es el cambio entero
/// de la HU #14: antes, quien llamaba declaraba <c>assertion</c> y se le creía. Ahora se le cree
/// solo lo que se puede comprobar.</para>
/// </remarks>
public static class IdentityAssertions
{
    /// <summary>
    /// Resuelve con qué fuerza se afirmó la identidad, o por qué no se puede afirmar nada.
    /// </summary>
    /// <param name="gate">El verificador, que puede no estar configurado.</param>
    /// <param name="rawToken">Lo que vino en la cabecera, si vino algo.</param>
    /// <param name="who">Quién dice la petición que está actuando.</param>
    /// <param name="declared">Lo que el llamador declaró. Solo se le acepta lo indemostrable-a-la-baja.</param>
    /// <param name="now">Ahora.</param>
    /// <param name="codePrefix">El prefijo de rechazo de la capacidad que pregunta.</param>
    public static (IdentityAssertion? Assertion, Rejection? Rejection) Resolve(
        IdentityTokenGate gate, string? rawToken, Ref who,
        IdentityAssertion? declared, DateTimeOffset now, string codePrefix)
    {
        ArgumentNullException.ThrowIfNull(gate);

        if (!string.IsNullOrWhiteSpace(rawToken))
        {
            // Presentaron token y este servicio no puede comprobarlo. Se RECHAZA, no se ignora:
            // ignorarlo dejaría que alguien mandara cualquier cosa y siguiera adelante como si
            // no hubiera mandado nada, que es peor que no aceptar tokens.
            if (!gate.Configured)
            {
                return (null, Rejection.Invalid($"{IdentityTokens.CodePrefix}.token_not_verifiable",
                    "Se presentó un token de identidad y este servicio no tiene llave para comprobarlo."));
            }

            var (claims, motivo) = gate.Tokens!.Verify(rawToken, now);
            if (claims is null) return (null, IdentityTokens.ToRejection(motivo!.Value));

            // El que da sentido a toda la HU: el token dice una persona y la petición actúa como
            // otra. Sin esto, el token sería decoración y la capacidad seguiría creyendo el
            // `who` que le mandan.
            if (claims.Subject != who)
            {
                return (null, IdentityTokens.SubjectMismatch(claims.Subject, who));
            }

            return (IdentityAssertion.IdentityToken, null);
        }

        if (declared is null || !Enum.IsDefined(declared.Value))
        {
            return (null, Rejection.Invalid($"{codePrefix}.access_requires_identity",
                "Hace falta decir cómo se afirmó la identidad de quien accede: sin eso el registro no certifica nada."));
        }

        // Declarar una afirmación fuerte SIN presentarla es exactamente la mentira que el campo
        // existe para impedir (defecto #42). Lo único que se acepta sin prueba es la afirmación
        // más débil, que es honesta: «nos fiamos de quien llama».
        if (declared.Value != IdentityAssertion.CmsSession)
        {
            return (null, Rejection.Invalid($"{IdentityTokens.CodePrefix}.assertion_not_proven",
                $"No se puede afirmar '{declared.Value}' sin presentar la prueba correspondiente."));
        }

        return (IdentityAssertion.CmsSession, null);
    }

    /// <summary>
    /// Resuelve QUIÉN actúa —con sus roles— y con qué fuerza se afirmó, de una sola verificación.
    /// </summary>
    /// <param name="gate">El verificador, que puede no estar configurado.</param>
    /// <param name="rawToken">Lo que vino en la cabecera, si vino algo.</param>
    /// <param name="principal">Quién dice la petición que está actuando.</param>
    /// <param name="declaredRoles">Los roles que declara el cuerpo. Se usan SOLO si no hay token.</param>
    /// <param name="declared">La afirmación que declara el cuerpo, si la hay.</param>
    /// <param name="now">Ahora.</param>
    /// <param name="codePrefix">El prefijo de rechazo de la capacidad que pregunta.</param>
    /// <remarks>
    /// <para><b>Por qué existe además de <see cref="Resolve"/>.</b> Aquél contesta «con qué
    /// fuerza», que es lo que necesita quien sólo va a <i>anotar</i> el acceso. Esto contesta
    /// además «con qué roles», que es lo que necesita quien va a <b>decidir</b> con ellos — y
    /// esos roles están dentro del token, así que sacarlos exige las <c>claims</c> y no sólo el
    /// veredicto. Encadenar los dos verificaría el mismo token dos veces.</para>
    ///
    /// <para><b>Vive acá desde su SEGUNDO consumidor, no antes</b> (<c>CLAUDE.md</c> §0.B.17).
    /// Nació dentro de <c>Api.Workflow</c> con el defecto #48 —los roles venían en el cuerpo, así
    /// que cualquiera con la llave compartida se ascendía a funcionario— y apareció igual en
    /// <c>Api.Audit</c> (#72). Con una tercera copia, la comprobación del token derivaría entre
    /// capacidades y el mismo token valdría distinto según a quién se le presente; y una
    /// capacidad no puede referenciar a otra (§0.B.11), así que <c>Shared</c> es el único sitio
    /// válido.</para>
    ///
    /// <para><b>El token gana sobre lo declarado, y no es preferencia:</b> los roles declarados
    /// son la palabra de quien llama y los del token vienen firmados. Cuando hay prueba, lo
    /// declarado no se mezcla — se descarta.</para>
    ///
    /// <para><b>Sin token se sigue adelante declarando</b>, que es lo que se hacía antes de la
    /// HU #14. Rechazar ahí convertiría cada despliegue sin <c>Api.Identity</c> en uno que no
    /// puede operar, y la identidad es justo la pieza que no debe ser punto único de fallo.</para>
    /// </remarks>
    public static Result<(Actor Actor, IdentityAssertion Assertion)> ResolveActor(
        IdentityTokenGate gate, string? rawToken, Ref principal,
        IReadOnlyList<string>? declaredRoles, IdentityAssertion? declared,
        DateTimeOffset now, string codePrefix)
    {
        ArgumentNullException.ThrowIfNull(gate);

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            // Sin prueba, la afirmación se resuelve con la MISMA regla que el resto de la
            // plataforma: lo único que se acepta declarado es lo más débil.
            var (afirmacion, motivo) = Resolve(gate, rawToken, principal, declared, now, codePrefix);
            if (afirmacion is null) return Result.Rejected<(Actor, IdentityAssertion)>(motivo!);

            var declarados = (declaredRoles ?? Array.Empty<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToArray();

            return Result.Ok((Actor.Of(principal, declarados), afirmacion.Value));
        }

        if (!gate.Configured)
        {
            return Result.Rejected<(Actor, IdentityAssertion)>(
                Rejection.Invalid($"{IdentityTokens.CodePrefix}.token_not_verifiable",
                    "Se presentó un token de identidad y este servicio no tiene llave para comprobarlo."));
        }

        var (claims, falla) = gate.Tokens!.Verify(rawToken, now);
        if (claims is null) return Result.Rejected<(Actor, IdentityAssertion)>(IdentityTokens.ToRejection(falla!.Value));

        // El token dice una persona y la petición actúa como otra: sin esto el token sería
        // decoración y la capacidad seguiría creyendo el `who` que le mandan.
        if (claims.Subject != principal)
        {
            return Result.Rejected<(Actor, IdentityAssertion)>(
                IdentityTokens.SubjectMismatch(claims.Subject, principal));
        }

        return Result.Ok((Actor.Of(principal, claims.Roles.ToArray()), IdentityAssertion.IdentityToken));
    }
}
