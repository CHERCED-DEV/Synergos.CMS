namespace Synergos.Core;

/// <summary>
/// Quién actúa: un principal opaco y sus roles.
/// </summary>
/// <param name="Principal">
/// Referencia al principal, resuelta por <c>Api.Identity</c>. Es un <see cref="Ref"/> y no un
/// <c>string</c> porque quien actúa no siempre es una persona: también un servicio o un trabajo
/// programado, y la auditoría necesita poder distinguirlos.
/// </param>
/// <param name="Roles">Roles vigentes en esta acción.</param>
/// <remarks>
/// <para><b>Para qué existe.</b> Para que autorización y auditoría hablen el mismo vocabulario.
/// Hoy cada sitio arma su idea de "el usuario" —a veces un email, a veces un GUID, a veces un
/// <c>ClaimsPrincipal</c>— y la bitácora termina con tres formas de nombrar a la misma persona.
/// El día que haya que responder "¿quién canceló esto?", esas tres formas cuestan una
/// investigación.</para>
///
/// <para><b>No lleva nombre ni correo.</b> Un <c>Actor</c> viaja a bitácoras y a rechazos; los
/// datos personales no tienen por qué acompañarlo. Quien necesite mostrar un nombre se lo pide a
/// <c>Api.Identity</c>, que es quien puede decidir si tiene derecho a verlo.</para>
/// </remarks>
public sealed record Actor(Ref Principal, IReadOnlySet<string> Roles)
{
    /// <summary>
    /// El actor anónimo: un visitante sin sesión.
    /// </summary>
    /// <remarks>
    /// Existe con nombre propio para que "no hay usuario" sea un valor y no un <c>null</c>. Un
    /// <c>null</c> aquí se propaga hasta que alguien lo desreferencia, y el sitio donde revienta
    /// nunca es el sitio donde faltaba.
    /// </remarks>
    public static readonly Actor Anonymous = new(new Ref("system", "anonymous"), new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Construye normalizando los roles (sin vacíos, sin repetidos, sin sensibilidad a mayúsculas).</summary>
    public static Actor Of(Ref principal, params string[] roles)
        => new(principal, new HashSet<string>(
            roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()),
            StringComparer.OrdinalIgnoreCase));

    /// <summary>Si tiene alguno de los roles pedidos.</summary>
    /// <remarks>
    /// "Alguno" y no "todos": es la semántica que ya usa el CMS (<c>RequireRoles</c>) y tenerla
    /// distinta a los dos lados de la red sería una trampa preparada.
    /// </remarks>
    public bool HasAnyRole(params string[] roles) => roles.Any(Roles.Contains);

    public bool IsAnonymous => Principal == Anonymous.Principal;

    public override string ToString() => $"{Principal} [{string.Join(",", Roles)}]";
}
