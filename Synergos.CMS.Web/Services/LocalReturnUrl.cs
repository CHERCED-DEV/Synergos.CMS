namespace Synergos.CMS.Web.Services;

/// <summary>
/// Regla ÚNICA de <c>returnUrl</c> seguro para los sinks que hacen
/// <c>Redirect(returnUrl)</c> con un valor que viene del visitante
/// (querystring, form field o cookie).
/// </summary>
/// <remarks>
/// Reemplaza las 3 copias que validaban con
/// <c>Uri.TryCreate(raw, UriKind.Relative, out _)</c> (AccountController,
/// MemberController, FlowController). Esa comprobación era un
/// OPEN REDIRECT: <c>//evil.com/x</c> ES una URI relativa válida, así que
/// pasaba el filtro y el navegador la resolvía como protocol-relative
/// hacia otro origen. No filtra sesión (la cookie no cruza origen); el
/// daño real es el phishing — la víctima aterriza en evil.com viniendo
/// de un login legítimo.
///
/// La regla replica <c>IUrlHelper.IsLocalUrl</c> (que sí rechaza
/// <c>//host</c> y <c>/\host</c>) para poder ser estática y testeable sin
/// montar un IUrlHelper. Es deliberadamente MÁS estricta en dos puntos:
/// no acepta <c>~/</c> (el sink es <c>Redirect()</c> crudo, que no
/// resuelve el tilde) y rechaza caracteres de control.
/// </remarks>
public static class LocalReturnUrl
{
    /// <summary>Fallback cuando el returnUrl no es local o viene vacío.</summary>
    public const string Fallback = "/";

    /// <summary>
    /// Devuelve <paramref name="raw"/> si es un path local seguro, o
    /// <see cref="Fallback"/> en cualquier otro caso.
    /// </summary>
    public static string Sanitize(string? raw) => IsLocal(raw) ? raw! : Fallback;

    /// <summary>
    /// True solo para paths del propio sitio: <c>/</c>, <c>/tramites</c>,
    /// <c>/a?b=c</c>, <c>/a#frag</c>. False para <c>//evil.com</c>,
    /// <c>/\evil.com</c>, <c>https://evil.com</c>, <c>~/x</c> y vacío.
    /// </summary>
    public static bool IsLocal(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        // Los navegadores DESCARTAN CR/LF/TAB de la URL antes de resolverla,
        // así que "/\t/evil.com" pasaría un check ingenuo de los 2 primeros
        // chars y luego navegaría a "//evil.com". Fuera todo control char.
        foreach (var c in url)
        {
            if (char.IsControl(c))
            {
                return false;
            }
        }

        // Debe ser path-absoluto del sitio. Esto ya descarta "https://evil.com"
        // y "~/x" (Redirect() no resuelve el tilde).
        if (url[0] != '/')
        {
            return false;
        }

        // "/" solo es la home.
        if (url.Length == 1)
        {
            return true;
        }

        // "//host" = protocol-relative → otro origen.
        // "/\host" = idem, los navegadores tratan "\" como "/" en el authority.
        return url[1] != '/' && url[1] != '\\';
    }
}
