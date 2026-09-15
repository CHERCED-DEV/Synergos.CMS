using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La copia de seguridad de los datos de las 22 (HU #31), como invariante ejecutable.
/// </summary>
/// <remarks>
/// <para><b>Lo que se vigila no es que exista un script de respaldo.</b> Es lo que hace que un
/// respaldo sirva el día que haga falta, que es distinto y se erosiona con más facilidad:</para>
///
/// <list type="number">
///   <item><b>Que haya restaurador.</b> Una copia que nadie restauró nunca no es una copia — es
///   una promesa que nadie comprobó. Por eso los dos ficheros se exigen juntos.</item>
///
///   <item><b>Que restaurar cueste una bandera explícita.</b> Pisa los datos vivos, y un comando
///   destructivo que se dispara con un solo argumento se dispara solo alguna vez.</item>
///
///   <item><b>Que se copie en frío.</b> <c>JsonCollectionStore</c> escribe con un <c>lock</c> de
///   proceso: copiar en caliente puede atrapar un JSON a medio escribir, y eso no da error al
///   copiar — lo da meses después, al restaurar, que es cuando no hay margen.</item>
///
///   <item><b>Que la lista de volúmenes se DERIVE del compose.</b> Una lista a mano se
///   desincroniza en la tercera ola, y lo que se pierde es justo el volumen que nadie recordó
///   añadir. <b>Y que el filtro INCLUYA por defecto</b>, que es lo que estaba mal: con
///   <c>-data$</c> se copiaban los certificados de Caddy —que se vuelven a pedir solos— y se
///   quedaban fuera la base del CMS, la biblioteca de medios, <c>App_Data</c> y el llavero de
///   DataProtection. El respaldo del producto no llevaba el producto, y no fallaba.</item>

/// </list>
/// </remarks>
public sealed class RespaldoTests
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

    private static string Herramienta(string nombre)
        => File.ReadAllText(Path.Combine(RepoRoot(), "tools", nombre));

    /// <summary>Los volúmenes declarados en <c>compose.prod.yml</c>.</summary>
    /// <remarks>
    /// Se leen del bloque <c>volumes:</c> de nivel superior — el mismo sitio del que
    /// <c>docker compose config --volumes</c> los saca — para poder cruzar contra él lo que el
    /// respaldo excluye. Sin este cruce, la lista de exclusiones es otra lista a mano.
    /// </remarks>
    private static IReadOnlyList<string> VolumenesDelCompose()
    {
        var lineas = File.ReadAllLines(Path.Combine(RepoRoot(), "compose.prod.yml"));
        var dentro = false;
        var nombres = new List<string>();

        foreach (var linea in lineas)
        {
            if (linea.StartsWith("volumes:", StringComparison.Ordinal)) { dentro = true; continue; }
            if (!dentro) continue;
            if (linea.Length > 0 && !char.IsWhiteSpace(linea[0])) break;

            var recortada = linea.Trim();
            if (recortada.Length == 0 || recortada.StartsWith('#')) continue;
            if (recortada.EndsWith(':')) nombres.Add(recortada[..^1]);
        }

        Assert.NotEmpty(nombres);
        return nombres;
    }

    [Fact]
    public void El_respaldo_viene_CON_su_restaurador()
    {
        // Es la regla de fondo del ticket: «una copia que nadie restauró nunca no es una copia».
        // Si algún día se borrara `restaurar.sh` por «no se usa», lo que quedaría es un tar.gz
        // que nadie sabe si sirve.
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "tools", "respaldo.sh")),
            "falta tools/respaldo.sh");
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "tools", "restaurar.sh")),
            "hay respaldo y no hay restaurador: eso no es una copia, es una promesa.");
    }

    [Fact]
    public void Restaurar_EXIGE_una_bandera_explicita()
    {
        // Restaurar pisa los datos vivos. Con un solo argumento, alguien lo corre «para ver qué
        // trae» y se lleva por delante el día de trabajo de otro.
        var restaurar = Herramienta("restaurar.sh");

        Assert.Contains("--si-estoy-seguro", restaurar, StringComparison.Ordinal);
        Assert.Contains("INSPECCIÓN", restaurar, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_dos_paran_los_servicios_antes_de_tocar_un_volumen()
    {
        // En caliente se puede atrapar un JSON a medio escribir. No falla al copiar: falla al
        // restaurar, meses después.
        foreach (var script in new[] { "respaldo.sh", "restaurar.sh" })
        {
            Assert.Contains("compose stop", Herramienta(script).Replace("$COMPOSE", "compose", StringComparison.Ordinal),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void El_respaldo_arranca_de_vuelta_pase_lo_que_pase()
    {
        // Un respaldo que falla a la mitad y deja el sitio caído es peor que no haber respaldado.
        var respaldo = Herramienta("respaldo.sh");

        Assert.Contains("trap", respaldo, StringComparison.Ordinal);
        Assert.Contains("compose start", respaldo.Replace("$COMPOSE", "compose", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void La_lista_de_volumenes_se_DERIVA_del_compose()
    {
        // El día que aparezca una capacidad nueva, su volumen tiene que entrar solo. Una lista
        // escrita a mano se desincroniza, y el volumen que falta es el que nadie recuerda.
        var respaldo = Herramienta("respaldo.sh");

        Assert.Contains("config --volumes", respaldo, StringComparison.Ordinal);

        // Y que no haya una lista de capacidades a mano escondida al lado.
        Assert.DoesNotContain("api-orders-data api-cart-data", respaldo, StringComparison.Ordinal);
    }

    [Fact]
    public void El_filtro_de_volumenes_INCLUYE_por_defecto_y_excluye_con_nombre()
    {
        // ESTE ES EL DEFECTO QUE EL ENSAYO DESTAPÓ. El criterio era `-data$`, un filtro que
        // INCLUYE: copiaba `caddy-data` —certificados que Caddy vuelve a pedir solos— y dejaba
        // fuera `cms-db` (la base de Umbraco), `cms-media` (la biblioteca), `cms-appdata` y
        // `cms-dpkeys`. Sin ese último, al restaurar no se puede descifrar la llave de firma de
        // los diplomas y el propio código avisa de que «los certificados ya emitidos dejarán de
        // verificar». O sea: el respaldo del producto no llevaba el producto, el tar salía bien,
        // y sólo se notaba restaurando.
        //
        // Lo que se vigila es el SENTIDO del filtro, no la lista: con uno que incluye, el volumen
        // que nadie recordó se pierde en silencio; con uno que excluye, se copia de más.
        var respaldo = Herramienta("respaldo.sh");

        Assert.DoesNotContain("grep -E -- '-data$'", respaldo, StringComparison.Ordinal);
        Assert.Contains("grep -E -v \"$RECONSTRUIBLES\"", respaldo, StringComparison.Ordinal);

        var reconstruibles = Regex.Match(respaldo, @"^RECONSTRUIBLES='\^\((?<lista>[^)]*)\)\$'",
            RegexOptions.Multiline).Groups["lista"].Value.Split('|', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(reconstruibles);

        // Cada exclusión tiene que existir de verdad en el compose. Una que sobra deja de leerse
        // —es el argumento de la lista de `HttpClient` del gate #49— y además esconde un typo:
        // `cms-log` en vez de `cms-logs` no excluye nada y nadie lo nota.
        var delCompose = VolumenesDelCompose();
        foreach (var r in reconstruibles)
        {
            Assert.True(delCompose.Contains(r),
                $"respaldo.sh excluye '{r}' y ese volumen no está en compose.prod.yml.");
        }

        // Y el resto entra. Si mañana aparece un volumen nuevo, entra SOLO: éste es el gate que
        // lo garantiza, y el que obliga a escribir una razón si alguien quiere dejarlo fuera.
        foreach (var v in delCompose.Where(v => !reconstruibles.Contains(v)))
        {
            Assert.DoesNotContain($"|{v}|", $"|{string.Join('|', reconstruibles)}|", StringComparison.Ordinal);
        }

        // Los cuatro que el filtro viejo perdía, nombrados: son la regresión concreta.
        foreach (var imprescindible in new[] { "cms-db", "cms-media", "cms-appdata", "cms-dpkeys" })
        {
            Assert.True(delCompose.Contains(imprescindible) && !reconstruibles.Contains(imprescindible),
                $"'{imprescindible}' tiene que entrar en el respaldo: sin él, restaurar devuelve un CMS vacío.");
        }
    }

}
