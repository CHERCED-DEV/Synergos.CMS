namespace Synergos.CMS.Web.Services;

/// <summary>
/// El bloque de entrada de miembros (<c>elementMemberLogin</c>, <c>Views/Partials/Elements/Member/Login.cshtml</c>).
/// </summary>
/// <remarks>
/// El destino del formulario sólo puede ser una ruta del propio sitio, con la misma regla que <c>returnUrl</c>
/// (<see cref="LocalReturnUrl"/>); con otro destino, el bloque no se pinta. El campo del usuario se llama como lo
/// espera <c>AccountController.LoginPost</c> (016, D-016-C21).
/// </remarks>
public static class BloqueDeEntrada
{
    /// <summary>El nombre del campo del correo o usuario, el que lee el controlador de entrada.</summary>
    public const string CampoDelUsuario = "emailOrUsername";

    /// <summary>El destino del formulario si es una ruta local; nulo en cualquier otro caso.</summary>
    public static string? Destino(string? configurado) => LocalReturnUrl.IsLocal(configurado) ? configurado : null;
}
