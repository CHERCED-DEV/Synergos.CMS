namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Exige del ENTORNO la contraseña del administrador cuando el arranque instala Umbraco
/// desatendido, y dice de dónde sale (#150).
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> <c>appsettings.Docker.json</c> traía la contraseña escrita junto a
/// <c>InstallUnattended: true</c>, y <c>ASPNETCORE_ENVIRONMENT: Docker</c> es el perfil con el
/// que corre <b>producción</b> (<c>compose.prod.yml</c>). O sea que la cuenta de administrador
/// del backoffice del sitio desplegado se creaba con una contraseña que cualquiera podía leer en
/// un repo público, junto con su correo. El mismo literal estaba en <b>siete</b> ficheros
/// versionados.</para>
///
/// <para><b>Y acá no se repite, a propósito.</b> Escribir el valor viejo dentro de la
/// explicación de por qué no se escriben los valores lo dejaría publicado una octava vez — con
/// mejor prosa alrededor y exactamente el mismo daño. Quien necesite saber cuál era lo saca del
/// historial de git, que es donde de verdad sigue estando: por eso hay que <b>rotarla</b> y no
/// sólo quitarla.</para>
///
/// <para><b>Es el gemelo del #113</b>, que encontró UNA clave que el perfil de Docker traía mal
/// para producción —los catorce endpoints de <c>DevController</c> quedaban alcanzables desde
/// internet— y la apagó en el compose. La pregunta que no se hizo entonces es la que cierra la
/// familia: <i>¿qué MÁS trae ese perfil que no sirve para producción?</i> Y la respuesta incluía
/// la credencial del administrador.</para>
///
/// <para><b>Por qué una guarda nuestra y no la de Umbraco.</b> Sin la clave, el instalador
/// desatendido de Umbraco también falla — pero su excepción habla de su propia validación y no
/// nombra ni la variable de entorno ni el fichero del que sale. Es la misma distinción que el
/// #137 hizo con el certificado de Kestrel: el problema no era que no avisara, era <b>qué</b>
/// avisaba. Acá el mensaje dice qué teclear.</para>
///
/// <para><b>Y por qué no se deja un valor de desarrollo en el árbol.</b> Era la salida corta:
/// dejar una contraseña distinta en <c>appsettings.Development.json</c> y quitar sólo la de
/// Docker. Pero <c>.env.example</c> abre con «NINGÚN SECRETO ENTRA AL REPO. NI UNO», y una
/// contraseña de desarrollo en un repo público es exactamente la que alguien reusa en el primer
/// servidor que levanta a mano. El precio es una variable más en el onboarding, y está escrito
/// en <c>docs/onboarding/</c>.</para>
///
/// <para><b>Lo que esta guarda NO arregla</b>, y va dicho: la contraseña que ya se usó está
/// quemada. <c>.env.example</c> lo dice con todas las letras —«si alguno se pega por error en un
/// commit… SE ROTA, no se borra el mensaje»— y esto no puede rotarla por nadie. Mientras el
/// literal siga siendo válido en algún entorno desplegado, el resto es cosmético.</para>
/// </remarks>
public static class CredencialDelAdministrador
{
    /// <summary>La clave que enciende la instalación desatendida.</summary>
    public const string ClaveDelInterruptor = "Umbraco:CMS:Unattended:InstallUnattended";

    /// <summary>La clave de la contraseña, que NO vive en ningún fichero del repo.</summary>
    public const string ClaveDeLaContrasena = "Umbraco:CMS:Unattended:UnattendedUserPassword";

    /// <summary>La variable de entorno equivalente, que es como se escribe de verdad.</summary>
    /// <remarks>
    /// Se declara acá y no en la prosa porque el mensaje del fallo la nombra, y una variable
    /// escrita a mano en un mensaje se desvía de la clave que el binder lee — que es el defecto
    /// #138 exacto, una clave en la sección equivocada que nadie cruza.
    /// </remarks>
    public static string VariableDeEntorno => ClaveDeLaContrasena.Replace(':', '_').Replace("_", "__", StringComparison.Ordinal);

    /// <summary>
    /// Lanza si el arranque va a instalar desatendido y no hay contraseña.
    /// </summary>
    /// <param name="instalaDesatendido">El valor de <see cref="ClaveDelInterruptor"/>.</param>
    /// <param name="contrasena">El valor de <see cref="ClaveDeLaContrasena"/>.</param>
    /// <param name="entorno">El nombre del entorno, para que el mensaje diga dónde ponerla.</param>
    public static void Exigir(string? instalaDesatendido, string? contrasena, string entorno)
    {
        if (!string.Equals(instalaDesatendido, "true", StringComparison.OrdinalIgnoreCase)) return;
        if (!string.IsNullOrWhiteSpace(contrasena)) return;

        var enProduccion = string.Equals(entorno, "Docker", StringComparison.OrdinalIgnoreCase);

        throw new InvalidOperationException(
            $"`{ClaveDelInterruptor}` está en true y no hay contraseña para el administrador.\n"
            + "\n"
            + "NO se pone en un appsettings: hasta el #150 este repo publicaba la del "
            + "administrador de PRODUCCIÓN en `appsettings.Docker.json`, que es el perfil con el "
            + "que corre el despliegue. Sale del entorno:\n"
            + "\n"
            + $"    {VariableDeEntorno}=…\n"
            + "\n"
            + (enProduccion
                ? "En el servidor la pone `compose.prod.yml` desde `SYNERGOS_ADMIN_PASSWORD` del "
                  + "`.env` — si estás viendo esto en producción, esa variable falta.\n"
                : "En desarrollo va en tu `.env` local o en `dotnet user-secrets`; el paso está "
                  + "en `docs/onboarding/new-developer-setup.md`.\n")
            + "\n"
            + "Y si sólo querés levantar sin instalar nada, apagá el interruptor: "
            + $"`{ClaveDelInterruptor}=false`.");
    }
}
