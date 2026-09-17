namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La llave privada del certificado de desarrollo no puede entrar al repo (#137).
/// </summary>
/// <remarks>
/// <para><b>Existe porque el arreglo del #137 CREA un riesgo que antes no había.</b> La llave vivía
/// fuera del árbol —en <c>C:\LOCAL_CDN</c>, que era el defecto— y hoy vive en <c>certs/</c>, dentro.
/// Un <c>git add -A</c> después de correr <c>tools/cert-dev.mjs</c> la subiría, y una llave privada
/// publicada no se arregla borrando el commit: <b>se rota</b>.</para>
///
/// <para><b>Y este gate NO repite el de <see cref="RaizDelCdnTests"/>.</b> Aquél ya recorre todas
/// las claves de todos los <c>appsettings</c> con el criterio agnóstico del literal
/// (<c>^[A-Za-z]:[\\/]|^/(home|Users)/|%USERPROFILE%|\$HOME</c>), y escribir un segundo gate con
/// ese mismo criterio habría sido la copia que <c>feedback_the_same_algorithm_is_not_the_same_thing</c>
/// prohíbe — el día que una se afine, la otra miente. Lo que falta ahí es otra pregunta: no «¿la
/// ruta nombra una máquina?» sino <b>«¿lo que esa ruta contiene puede llegar a GitHub?»</b>.</para>
///
/// <para><b>Se comprueba el EFECTO y no la línea.</b> Buscar el texto <c>certs/</c> dentro del
/// <c>.gitignore</c> pasaría en verde con la línea comentada, con un <c>!certs/</c> más abajo que la
/// desactive, con un <c>.gitignore</c> anidado que la contradiga o con el fichero renombrado. Es la
/// diferencia entre medir que la pieza esté ENCHUFADA y medir que se mencione — el addendum #14 de
/// <c>feedback_an_exemption_needs_a_signature_behind_it</c>. Así que se le pregunta a git, que es
/// quien decide.</para>
/// </remarks>
public sealed class LlaveDelCertificadoTests
{
    /// <summary>
    /// Lo que <c>tools/cert-dev.mjs</c> escribe. Si ese script cambia de destino, esto se pone rojo
    /// — que es lo que se quiere: el destino y su protección tienen que moverse juntos.
    /// </summary>
    [Theory]
    [InlineData("certs/synergos-dev.key")]
    [InlineData("certs/synergos-dev.crt")]
    public void Lo_que_escribe_cert_dev_esta_ignorado(string candidata)
    {
        var p = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo("git", $"check-ignore -q -- {candidata}")
            {
                WorkingDirectory = Proyectos.Raiz(),
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            },
        };
        p.Start();
        p.WaitForExit();

        // `git check-ignore -q` sale 0 si ESTÁ ignorada, 1 si no, y 128 si no pudo (no es un repo,
        // no hay git en el PATH). El 128 se distingue a propósito: un gate que tratara «no pude
        // preguntar» como «está ignorada» pasaría en verde justo donde no puede saber nada — el
        // verde sobre el vacío que este repo ya tiene escrito cuatro veces.
        Assert.False(p.ExitCode == 128,
            $"No se pudo preguntar a git si «{candidata}» está ignorada (salida 128). Este gate "
            + "necesita un repo git y el ejecutable en el PATH.");

        Assert.True(p.ExitCode == 0,
            $"«{candidata}» NO está ignorada. `tools/cert-dev.mjs` deja el par ahí, así que un "
            + "`git add -A` subiría la LLAVE PRIVADA al repo — y eso no se arregla borrando el "
            + "commit, se rota. Añadí `certs/` a .gitignore (#137).");
    }

    /// <summary>
    /// Y que el script siga escribiendo donde este gate vigila.
    /// </summary>
    /// <remarks>
    /// Sin esto, cambiar el destino en <c>cert-dev.mjs</c> dejaría el gate de arriba vigilando una
    /// carpeta que ya no se usa —verde, y la llave nueva sin proteger—. Es el cruce en el segundo
    /// sentido, como el del censo de <see cref="RaizDelCdnTests"/>.
    /// </remarks>
    [Fact]
    public void El_script_escribe_donde_este_gate_vigila()
    {
        var fuente = File.ReadAllText(Proyectos.Ruta("tools", "cert-dev.mjs"));

        Assert.Contains("'certs'", fuente, StringComparison.Ordinal);
        Assert.Contains("synergos-dev.crt", fuente, StringComparison.Ordinal);
        Assert.Contains("synergos-dev.key", fuente, StringComparison.Ordinal);
    }
}
