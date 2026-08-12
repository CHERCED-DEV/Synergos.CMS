namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Quién custodia la llave del diploma, y qué pasa con lo ya emitido (hallazgo #45).
/// </summary>
/// <remarks>
/// <para>Tres cosas que el compilador no vigila y que, rotas, <b>no rompen nada visiblemente</b>:
/// que el firmante local se conserve como verificador de los ids viejos, que una capacidad caída
/// no se traduzca en «doy el diploma por bueno», y que el stub siga siendo el default.</para>
/// </remarks>
public sealed class AcademyWiringTests
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

    private static string Firmante() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Services", "HttpCertificateIdSigner.cs"));

    private static string Composer() => SinComentarios(Path.Combine(
        RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.Academy.cs"));

    /// <summary>
    /// Va a <c>/v1/seals</c> y NUNCA a <c>/v1/signatures</c>.
    /// </summary>
    /// <remarks>
    /// Es el hallazgo entero. Aquel token vence, no es determinista y publica su payload sin
    /// llave; las tres cosas son correctas para lo que hace y ninguna sirve para un diploma.
    /// </remarks>
    [Fact]
    public void El_diploma_se_sella_y_no_se_firma()
    {
        var firmante = Firmante();

        Assert.Contains("v1/seals", firmante, StringComparison.Ordinal);
        Assert.DoesNotContain("v1/signatures", firmante, StringComparison.Ordinal);
        // Y sin vencimiento: un diploma no caduca.
        Assert.DoesNotContain("LifetimeMinutes", firmante, StringComparison.Ordinal);
    }

    /// <summary>
    /// El firmante local se conserva como VERIFICADOR de los ids anteriores.
    /// </summary>
    /// <remarks>
    /// Sin esto, cada diploma ya impreso deja de verificar el día del despliegue — y no falla
    /// ruidosamente: contesta que la credencial no vale, que es lo peor que puede decir.
    /// </remarks>
    [Fact]
    public void Los_ids_anteriores_al_cableado_siguen_teniendo_quien_los_verifique()
    {
        var composer = Composer();

        var rama = composer[composer.IndexOf("\"Synergos:Academy:Mode\"", StringComparison.Ordinal)..];
        var otra = rama.IndexOf("else", StringComparison.Ordinal);
        Assert.True(otra > 0, "Sin rama else no hay camino por defecto.");

        // En la rama del cableado se le pasa el firmante heredado.
        Assert.Contains("LazyCertificateIdSigner(", rama[..otra], StringComparison.Ordinal);
        Assert.Contains("HttpCertificateIdSigner(", rama[..otra], StringComparison.Ordinal);

        // Y el firmante sabe distinguir un id viejo sin salir a la red.
        Assert.Contains("EsHeredado", Firmante(), StringComparison.Ordinal);
    }

    /// <summary>
    /// «No sé» no se convierte en «lo doy por bueno».
    /// </summary>
    /// <remarks>
    /// <c>Matches</c> es lo único que impide que quien consiga escribir en el almacén fabrique un
    /// diploma con el nombre que quiera. Un <c>catch</c> que devuelva <c>true</c> —o que caiga al
    /// firmante local para emitir— vacía esa garantía sin romper nada visible.
    /// </remarks>
    [Fact]
    public void Una_capacidad_caida_no_da_por_buena_una_credencial()
    {
        var firmante = Firmante();

        var bloques = firmante.Split("catch", StringSplitOptions.None).Skip(1);
        foreach (var bloque in bloques)
        {
            var hasta = bloque.IndexOf("\n        }", StringComparison.Ordinal);
            var cuerpo = hasta > 0 ? bloque[..hasta] : bloque;

            // Ningún `catch` DEVUELVE, punto. Buscar `return true` era demasiado literal: una
            // mutación que escribió `return (T)(object)true` pasó en verde. Los dos catch
            // legítimos de este fichero relanzan, así que la regla puede ser total.
            Assert.DoesNotContain("return", cuerpo, StringComparison.Ordinal);
            Assert.DoesNotContain("_heredado", cuerpo, StringComparison.Ordinal);
        }
    }

    /// <summary>El stub sigue siendo el default: un clon limpio emite diplomas sin levantar nada.</summary>
    [Fact]
    public void El_stub_sigue_siendo_el_default()
    {
        var composer = Composer();

        var condicion = composer.IndexOf("\"Synergos:Academy:Mode\"", StringComparison.Ordinal);
        Assert.True(condicion > 0, "El composer ya no decide el modo de Educación.");

        var rama = composer[condicion..];
        var otra = rama.IndexOf("else", StringComparison.Ordinal);

        Assert.Contains("\"Api\"", rama[..otra], StringComparison.Ordinal);
        Assert.Contains("ICertificateIdSigner, LazyCertificateIdSigner", rama[otra..], StringComparison.Ordinal);
    }

    /// <summary>La sección se ENLAZA, o el propósito del sello se queda en su default.</summary>
    /// <remarks>
    /// Sellaría bajo una etiqueta que el despliegue no configuró — y las llaves viven POR
    /// propósito, así que sería sellar contra un juego de llaves que no es el suyo.
    /// </remarks>
    [Fact]
    public void La_seccion_de_Educacion_se_enlaza()
    {
        Assert.Contains("Configure<AcademySettings>(", Composer(), StringComparison.Ordinal);
        Assert.Contains("\"Synergos:Academy\"", Composer(), StringComparison.Ordinal);
    }
}
