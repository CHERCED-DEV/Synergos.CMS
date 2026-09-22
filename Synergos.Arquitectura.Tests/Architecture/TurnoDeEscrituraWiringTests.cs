namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Que el turno de escritura esté ENCHUFADO en las capacidades que lo necesitan (#112).
/// </summary>
/// <remarks>
/// <para><b>Se mide contra el disco y no contra una lista.</b> Es la tercera vez que este repo
/// paga una lista escrita a mano —«los seis de <c>Synergos.Shared</c>», «faltan las otras 16», los
/// tres endpoints de <c>Api.Consent</c>—: quién necesita el turno se deduce de quién usa
/// <c>JsonCollectionStore</c>, así que una capacidad nueva entra sola.</para>
///
/// <para><b>Y mide que esté enchufado, no que exista</b>, que es la lección de
/// <c>WebhookGateTests</c>: <c>Api.Notifications</c> quitó la llamada de su lambda y no falló ni
/// un test, porque los suyos probaban el verificador.</para>
/// </remarks>
public sealed class TurnoDeEscrituraWiringTests
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

    private static IEnumerable<(string Nombre, string Programa, string Codigo)> Capacidades()
        => Proyectos.Directorios()
            .Where(d => Path.GetFileName(d).StartsWith("Synergos.Api.", StringComparison.Ordinal))
            .Select(d => (Nombre: Path.GetFileName(d), Dir: d))
            .Where(x => File.Exists(Path.Combine(x.Dir, "Program.cs")))
            .Select(x => (x.Nombre, Path.Combine(x.Dir, "Program.cs"), Fuente(x.Dir)))
            .ToList();

    /// <summary>Todo el C# de una capacidad, para ver si toca el almacén compartido.</summary>
    private static string Fuente(string dir)
        => string.Join('\n', Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Select(File.ReadAllText));

    /// <summary>
    /// Quien guarda por <c>JsonCollectionStore</c> necesita turno; quien no, no.
    /// </summary>
    /// <remarks>
    /// <c>Api.Sessions</c> es la única que no lo usa, y no es un olvido: su almacén AÑADE líneas a
    /// un fichero por día y nunca lee-modifica-escribe, que es el único patrón que ya era seguro
    /// entre réplicas. Darle turno serializaría un registro de búsquedas para no arreglar nada.
    /// </remarks>
    /// <summary>
    /// Las capacidades que ENCHUFAN el turno, por nombre de proyecto.
    /// </summary>
    /// <remarks>
    /// <para>Vive acá y es <c>internal</c> porque <c>ComposeStackTests</c> necesita lo mismo para
    /// decidir quién puede escalar (#152), y **dos gates con el mismo criterio es
    /// <c>feedback_the_same_algorithm_is_not_the_same_thing</c>**: el día que uno se afine, el
    /// otro miente. Ya casi pasa — la copia que escribí primero barría TODO el C# de la capacidad
    /// en vez de su <c>Program.cs</c>, y habría dado por cableada a la que sólo lo menciona.</para>
    ///
    /// <para>Se mira la LLAMADA —<c>UseStoreWriteGate(</c> con paréntesis— y en el
    /// <c>Program.cs</c>. Contar la mención daría «20 de 20»: <c>Api.Inventory</c> llegó a tener
    /// el comentario que lo explica y no la llamada, porque una sesión anterior la quitó para ver
    /// el gate en rojo y se cortó antes de restaurarla.</para>
    /// </remarks>
    internal static IReadOnlySet<string> ConTurno()
        => Capacidades()
            .Where(c => File.ReadAllText(c.Programa).Contains("UseStoreWriteGate(", StringComparison.Ordinal))
            .Select(c => c.Nombre)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Las que guardan por <c>JsonCollectionStore</c>, o sea las que comparten grano.</summary>
    internal static IReadOnlySet<string> UsanElAlmacenCompartido()
        => Capacidades()
            .Where(c => c.Codigo.Contains("JsonCollectionStore<", StringComparison.Ordinal))
            .Select(c => c.Nombre)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Toda_capacidad_con_almacen_compartido_cablea_el_turno()
    {
        var conTurno = ConTurno();
        var sinCablear = UsanElAlmacenCompartido()
            .Where(n => !conTurno.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(sinCablear.Count == 0,
            "Estas capacidades guardan por JsonCollectionStore y no cablean UseStoreWriteGate: "
            + string.Join(", ", sinCablear)
            + ". Sin él, dos réplicas que leen-deciden-escriben el MISMO documento pierden una de "
            + "las dos escrituras — el defecto #30 vuelto a aparecer entre procesos.");
    }

    /// <summary>
    /// El turno va DESPUÉS de la llave compartida.
    /// </summary>
    /// <remarks>
    /// Al revés, cualquiera que supiera la URL dejaría a la capacidad sin turnos sin haberse
    /// identificado: no hace falta escribir nada para hacer cola.
    /// </remarks>
    [Fact]
    public void El_turno_va_detras_de_la_llave_compartida()
    {
        var malPuestas = Capacidades()
            .Select(c => (c.Nombre, Codigo: File.ReadAllText(c.Programa)))
            .Where(c => c.Codigo.Contains("UseStoreWriteGate(", StringComparison.Ordinal))
            .Where(c => c.Codigo.IndexOf("UseStoreWriteGate(", StringComparison.Ordinal)
                      < c.Codigo.IndexOf("UseSharedKeyAuth(", StringComparison.Ordinal))
            .Select(c => c.Nombre)
            .ToList();

        Assert.True(malPuestas.Count == 0,
            "Estas capacidades ponen el turno de escritura ANTES de la llave compartida: "
            + string.Join(", ", malPuestas));
    }

    /// <summary>
    /// Ningún <c>GET</c> de las veinte escribe.
    /// </summary>
    /// <remarks>
    /// <b>Es la premisa sobre la que el turno vive en el borde</b>, y por eso se vigila en vez de
    /// confiarse: el turno sólo mira lo que muta, así que una lectura que empezara a guardar se
    /// quedaría fuera de la exclusión sin que nada avisara. Se comprueba sobre la fuente SIN
    /// comentarios, que es la lección de <c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>.
    /// </remarks>
    [Fact]
    public void Ningun_GET_escribe_en_el_almacen()
    {
        var sospechosos = new List<string>();

        foreach (var (nombre, _, _) in Capacidades())
        {
            var dir = Proyectos.Dir(nombre, "Endpoints");
            if (!Directory.Exists(dir)) continue;

            foreach (var fichero in Directory.EnumerateFiles(dir, "*.cs"))
            {
                var codigo = SinComentarios(File.ReadAllText(fichero));
                foreach (var bloque in codigo.Split("app.Map").Skip(1))
                {
                    if (!bloque.StartsWith("Get(", StringComparison.Ordinal)) continue;
                    var metodos = System.Text.RegularExpressions.Regex
                        .Matches(bloque, @"\bsvc\.(\w+)\(")
                        .Select(m => m.Groups[1].Value);

                    foreach (var metodo in metodos)
                    {
                        if (EscribeEn(nombre, metodo)) sospechosos.Add($"{nombre}.{metodo}");
                    }
                }
            }
        }

        Assert.True(sospechosos.Count == 0,
            "Estos métodos se sirven desde un GET y escriben en el almacén: "
            + string.Join(", ", sospechosos)
            + ". El turno de escritura sólo cubre lo que muta, así que una escritura por GET "
            + "queda fuera de la exclusión entre réplicas.");
    }

    /// <summary>Si el cuerpo del método nombrado guarda algo.</summary>
    private static bool EscribeEn(string capacidad, string metodo)
    {
        var dir = Path.Combine(RepoRoot(), capacidad, "Domain");
        if (!Directory.Exists(dir)) return false;

        foreach (var fichero in Directory.EnumerateFiles(dir, "*.cs"))
        {
            var codigo = SinComentarios(File.ReadAllText(fichero));
            var i = codigo.IndexOf(" " + metodo + "(", StringComparison.Ordinal);
            if (i < 0) continue;

            // Hasta el siguiente miembro público, que es donde acaba este cuerpo.
            var cuerpo = codigo[i..];
            var fin = cuerpo.IndexOf("\n    public ", 1, StringComparison.Ordinal);
            if (fin > 0) cuerpo = cuerpo[..fin];
            if (cuerpo.Contains(".Put(", StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static string SinComentarios(string codigo)
        => System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(codigo, @"/\*.*?\*/", "",
                System.Text.RegularExpressions.RegexOptions.Singleline),
            @"//[^\n]*", "");
}
