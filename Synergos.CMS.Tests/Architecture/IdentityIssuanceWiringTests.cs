namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Quien decide se PRESENTA, ya no sólo se declara (HU #14, rebanada 4).
/// </summary>
/// <remarks>
/// <para>Hasta acá el CMS mandaba el nombre y los roles del funcionario en el CUERPO de la
/// petición, y <c>Api.Workflow</c> le creía: cualquiera con la llave compartida se ascendía
/// escribiendo una línea de JSON (defecto #48). Presentar un token firmado por
/// <c>Api.Identity</c> es lo que hace que la guarda por rol guarde algo.</para>
///
/// <para><b>Y lo que este gate protege además es la degradación</b>: sin identidad el trámite
/// tiene que seguir. La forma de romperlo es sutil —hacer que el emisor lance, o dejar de mandar
/// los roles declarados— y ninguna de las dos rompe un test de comportamiento, porque el camino
/// feliz sigue funcionando.</para>
/// </remarks>
public sealed class IdentityIssuanceWiringTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static string SinComentarios(string ruta)
    {
        Assert.True(File.Exists(ruta), $"No existe {ruta}: revisar este gate.");
        return string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)
                || t.StartsWith("///", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));
    }

    private static string Emisor() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Services", "HttpIdentityTokenIssuer.cs"));

    private static string Gobierno() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Services", "HttpCaseWorkflowService.cs"));

    private static string Composer() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.Platform.cs"));

    /// <summary>El expediente presenta la identidad cuando la tiene.</summary>
    [Fact]
    public void El_expediente_presenta_la_identidad()
    {
        var gobierno = Gobierno();

        Assert.Contains("_identidad.IssueAsync", gobierno, StringComparison.Ordinal);
        Assert.Contains("X-Synergos-Identity", gobierno, StringComparison.Ordinal);
    }

    /// <summary>
    /// Y sigue declarando los roles, que es el camino degradado.
    /// </summary>
    /// <remarks>
    /// <b>No es redundancia.</b> Con token la capacidad ignora lo declarado y usa lo firmado
    /// (#48); sin token —clon limpio, o la capacidad caída— lo declarado es el único dato que
    /// hay. Quitarlo dejaría a ventanilla sin poder decidir el día que la identidad no responda,
    /// y el camino feliz seguiría verde: por eso hace falta un gate y no un test.
    /// </remarks>
    [Fact]
    public void Sin_identidad_se_sigue_declarando()
    {
        var gobierno = Gobierno();

        Assert.Contains("OfficerRoles", gobierno, StringComparison.Ordinal);
        Assert.Contains("new FireDto(", gobierno, StringComparison.Ordinal);

        // Y los roles salen de UN solo sitio: con dos listas, un despliegue presentaría un token
        // con un rol y declararía otro, y el mismo funcionario decidiría distinto según qué mire
        // la capacidad.
        //
        // Se cuenta en vez de prohibir: el literal aparece UNA vez, en la declaración, y buscarlo
        // a secas ponía rojo al propio sitio que este gate quiere que exista. Es la misma trampa
        // de declaración-contra-uso que ya costó una mutación en #40.
        var literales = gobierno.Split("new[] { \"funcionario\" }").Length - 1;
        Assert.True(literales == 1,
            $"El rol del funcionario aparece {literales} veces como literal. Va en UN sitio: "
            + "con dos, el token diría un rol y el cuerpo declararía otro.");
    }

    /// <summary>
    /// El emisor NUNCA lanza.
    /// </summary>
    /// <remarks>
    /// Es la propiedad que define la seam: si lanzara, la primera caída de <c>Api.Identity</c>
    /// pararía las decisiones de ventanilla — el punto único de fallo que la HU #14 evitó
    /// verificando los tokens en local. El <c>catch</c> devuelve, no relanza; la única excepción
    /// que sube es la cancelación de quien pidió, que no es un fallo de la identidad.
    /// </remarks>
    [Fact]
    public void El_emisor_no_lanza()
    {
        var emisor = Emisor();

        Assert.Contains("catch (Exception ex)", emisor, StringComparison.Ordinal);
        Assert.Contains("return null;", emisor, StringComparison.Ordinal);

        // Un `throw;` suelto en el catch general devolvería el punto único de fallo. El de la
        // cancelación está en su propio catch, con su guarda, y ése sí sube.
        var general = emisor[emisor.IndexOf("catch (Exception ex)", StringComparison.Ordinal)..];
        Assert.DoesNotContain("throw", general, StringComparison.Ordinal);
    }

    /// <summary>
    /// El default es no emitir, y el interruptor está en la plataforma.
    /// </summary>
    /// <remarks>
    /// Vive en el composer de plataforma y no en el de Gobierno aunque hoy lo use sólo el
    /// expediente: quién actúa no es asunto de un vertical, y el segundo consumidor tiene que
    /// encontrarlo registrado en vez de duplicarlo con otro cliente y otro timeout.
    /// </remarks>
    [Fact]
    public void El_default_no_emite_identidad()
    {
        var composer = Composer();
        var condicion = composer.IndexOf("\"Synergos:Identity:Mode\"", StringComparison.Ordinal);
        Assert.True(condicion > 0, "El interruptor de identidad no está en el composer de plataforma.");

        var elseAt = composer.IndexOf("else", condicion, StringComparison.Ordinal);
        var rama = composer[condicion..elseAt];

        Assert.Contains("HttpIdentityTokenIssuer", rama, StringComparison.Ordinal);
        Assert.DoesNotContain("StubIdentityTokenIssuer", rama, StringComparison.Ordinal);
        Assert.Contains("StubIdentityTokenIssuer", composer[elseAt..], StringComparison.Ordinal);

        Assert.Equal("Stub", new Synergos.CMS.Application.Configuration.IdentitySettings().Mode);
    }

    /// <summary>
    /// El CMS no se inventa una credencial para quien ya entró por otra puerta.
    /// </summary>
    /// <remarks>
    /// Fabricar una contraseña por persona para poder darla de alta obligaría a custodiarla, y
    /// una credencial que nadie usa es sólo superficie de ataque. Por eso <c>Api.Identity</c>
    /// admite principales sin credencial desde esta rebanada.
    /// </remarks>
    [Fact]
    public void El_CMS_no_fabrica_credenciales()
    {
        var emisor = Emisor();

        Assert.Contains("secret = (string?)null", emisor, StringComparison.Ordinal);

        foreach (var prohibido in new[] { "RandomNumberGenerator.GetBytes", "GeneratePassword", "NewGuid().ToString" })
        {
            Assert.DoesNotContain(prohibido, emisor, StringComparison.Ordinal);
        }
    }
}
