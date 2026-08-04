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
///   añadir.</item>
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
    public void Restaurar_VACIA_el_volumen_antes_de_desempacar()
    {
        // Sin esto, un fichero que existía en el servidor y no en la copia sobrevive, y queda un
        // estado mezclado: ni el de ayer ni el de hoy, y nadie puede razonar sobre él.
        Assert.Contains("rm -rf /destino", Herramienta("restaurar.sh"), StringComparison.Ordinal);
    }

    [Fact]
    public void Las_copias_NO_van_al_repo()
    {
        // `feedback_backups_external_to_repo`. Y además llevan datos personales —direcciones de
        // entrega, nombres de pacientes—: un respaldo commiteado es una filtración con historial.
        var respaldo = Herramienta("respaldo.sh");

        Assert.DoesNotContain("SYNERGOS_BACKUP_DIR:-.", respaldo, StringComparison.Ordinal);
        Assert.Contains("/var/backups/synergos", respaldo, StringComparison.Ordinal);

        // Y en disco no puede haber ninguno ya commiteado.
        var sueltos = Directory
            .EnumerateFiles(RepoRoot(), "synergos-datos-*.tar.gz", SearchOption.AllDirectories)
            .ToList();
        Assert.True(sueltos.Count == 0, $"hay respaldos dentro del repo: {string.Join(", ", sueltos)}");
    }

    [Fact]
    public void El_respaldo_deja_un_MANIFIESTO_de_que_copio()
    {
        // Dentro de seis meses hay un tar.gz y ninguna forma de saber de qué versión es ni si le
        // falta una capacidad. El manifiesto es lo que hace la copia legible sin adivinar.
        Assert.Contains("MANIFIESTO", Herramienta("respaldo.sh"), StringComparison.Ordinal);
        Assert.Contains("MANIFIESTO", Herramienta("restaurar.sh"), StringComparison.Ordinal);
    }
}
