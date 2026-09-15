using Synergos.Shared;

namespace Synergos.CMS.Tests.Api;

/// <summary>
/// El almacén de las veinte capacidades aguanta una segunda réplica (#112).
/// </summary>
/// <remarks>
/// <para><b>DOS almacenes sobre el mismo directorio, y eso ES el fixture</b> — la misma
/// convención que <c>ArriendoDeCompensacionTests</c> y que el gate del #82. Un proceso se simula
/// con su propia instancia: escribir y leer contra la MISMA pasa en verde con el defecto puesto,
/// que es literalmente por lo que el #82 vivió meses sin que nadie lo viera.</para>
///
/// <para><b>El defecto que reproducen.</b> <c>Put</c> escribía el mapa ENTERO desde el caché de
/// su proceso. Dos réplicas no se pisaban «un documento»: la que escribía segunda borraba la
/// colección de la primera. Sin excepción y sin log.</para>
/// </remarks>
public sealed class AlmacenPorDocumentoTests : IDisposable
{
    private readonly string _raiz = Path.Combine(
        Path.GetTempPath(), "syn-almacen-" + Guid.NewGuid().ToString("n"));

    public void Dispose()
    {
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* el temporal no es el sujeto del test */ }
    }

    private sealed record Cosa(string Id, int Valor);

    private JsonCollectionStore<Cosa> Almacen() => new(_raiz, "cosas", c => c.Id);

    // ── Lo que reventaba ─────────────────────────────────────────────────────

    /// <summary>
    /// Dos «réplicas» que guardan documentos distintos: los dos quedan.
    /// </summary>
    /// <remarks>
    /// Es el #112 entero. Con el formato anterior, <c>b</c> escribía su mapa completo —que no
    /// contenía a <c>a</c>, porque su caché se llenó antes— y <c>a</c> desaparecía. Comprobado
    /// mutando: con el almacén de colección este test se pone rojo con «esperaba 2, hubo 1».
    /// </remarks>
    [Fact]
    public void Dos_replicas_que_escriben_distinto_no_se_pisan()
    {
        var a = Almacen();
        var b = Almacen();

        // Los dos toman su foto ANTES de que el otro escriba — que es lo que pasa cuando dos
        // procesos arrancan a la vez. Sin esta lectura previa el defecto no se reproduce.
        Assert.Empty(a.All());
        Assert.Empty(b.All());

        a.Put(new Cosa("a", 1));
        b.Put(new Cosa("b", 2));

        var leidos = Almacen().All().OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "a", "b" }, leidos.Select(c => c.Id));
    }

    /// <summary>
    /// Una réplica ve lo que la otra acaba de escribir, sin que nadie le avise.
    /// </summary>
    /// <remarks>
    /// Sin caché de colección no hay foto que envejezca. Es lo que hacía falta para que el
    /// arriendo de compensaciones (#34) dejara de depender de que alguien se acordara de llamar a
    /// <c>Invalidate</c> antes de decidir.
    /// </remarks>
    [Fact]
    public void Una_replica_lee_lo_que_la_otra_escribio()
    {
        var a = Almacen();
        Assert.Null(a.Find("x"));          // deja a `a` "caliente"

        Almacen().Put(new Cosa("x", 7));

        Assert.Equal(7, a.Find("x")!.Valor);
    }

    /// <summary>Un documento por fichero, que es lo que hace que lo de arriba sea posible.</summary>
    [Fact]
    public void Cada_documento_es_un_fichero()
    {
        Almacen().Put(new Cosa("a", 1));
        Almacen().Put(new Cosa("b", 2));

        Assert.Equal(2, Directory.EnumerateFiles(Path.Combine(_raiz, "cosas"), "*.json").Count());
    }

    /// <summary>Reemplazar por identificador sigue reemplazando, no acumulando.</summary>
    [Fact]
    public void Reemplazar_no_duplica()
    {
        Almacen().Put(new Cosa("a", 1));
        Almacen().Put(new Cosa("a", 2));

        Assert.Equal(2, Almacen().All().Single().Valor);
    }

    /// <summary>
    /// Una clave con separadores de ruta no se sale del directorio.
    /// </summary>
    /// <remarks>
    /// No es teórico: la clave del libro de idempotencia es <c>{ámbito}|{llave del llamador}</c>, y
    /// la llave la escribe quien llama —hasta 128 caracteres cualesquiera—. Por eso el nombre del
    /// fichero es la huella y no la clave, igual que en <c>FileSystemSagaLease</c>.
    /// </remarks>
    [Fact]
    public void Una_clave_con_barras_no_escapa_del_directorio()
    {
        // ⚠️ EL FIXTURE, y costó DOS mutaciones en verde antes de tener dientes. La carga tiene
        // que llevar tantos `..` como componentes haya que deshacer, y contarlos es menos obvio de
        // lo que parece: `stock|../../fuera` no sale de ningún sitio —`stock|..` es UN componente,
        // porque no hay barra entre `stock|` y `..`, así que el `..` que sigue sólo deshace ése—,
        // y `stock|/../escapado` deshace `stock|` y aterriza otra vez dentro. Las dos veces el
        // almacén escribió la clave en crudo y el test pasó en verde. Es
        // `feedback_a_gate_that_parses_source_needs_its_own_mutations` con otro disfraz: el dato
        // de prueba tiene que EXIGIR la regla, y acá eso se comprueba contando.
        var malicia = "stock|/../../escapado";
        Almacen().Put(new Cosa(malicia, 9));

        Assert.Equal(9, Almacen().Find(malicia)!.Valor);

        // Y se comprueba por CONTINENTE y no por un nombre concreto: lo que hay que impedir no es
        // que aparezca "escapado.json" en un sitio, es que algo salga del directorio de la
        // colección — puede salir por cualquier nombre.
        var dir = Path.Combine(_raiz, "cosas");
        var fuera = Directory.EnumerateFileSystemEntries(_raiz, "*", SearchOption.AllDirectories)
            .Where(e => !e.StartsWith(dir, StringComparison.Ordinal))
            .ToList();

        Assert.True(fuera.Count == 0, "Se escribió fuera del directorio de la colección: "
            + string.Join(", ", fuera));
    }

    /// <summary>
    /// Dos claves que un saneo ingenuo confundiría son dos documentos.
    /// </summary>
    /// <remarks>
    /// Cambiar los caracteres prohibidos por un guión —que es lo que hace el almacén del árbol del
    /// CMS— colisionaría estas dos en el mismo fichero: la segunda llave de idempotencia
    /// devolvería el resultado de la primera, que es exactamente lo que la llave existe para
    /// evitar. La huella no colisiona.
    /// </remarks>
    [Fact]
    public void Dos_claves_parecidas_no_colisionan()
    {
        Almacen().Put(new Cosa("stock|a/b", 1));
        Almacen().Put(new Cosa("stock|a-b", 2));

        Assert.Equal(1, Almacen().Find("stock|a/b")!.Valor);
        Assert.Equal(2, Almacen().Find("stock|a-b")!.Valor);
    }

    // ── Lo que ya estaba escrito ─────────────────────────────────────────────

    /// <summary>Lo guardado con el formato anterior se sigue leyendo.</summary>
    /// <remarks>
    /// Una migración que se equivoque acá no deja un error: deja una capacidad <b>vacía</b>, que
    /// es la peor cosa que puede hacer un despliegue. Por eso va con test y no con confianza.
    /// </remarks>
    [Fact]
    public void Lo_escrito_con_el_formato_anterior_se_lee()
    {
        Directory.CreateDirectory(_raiz);
        File.WriteAllText(Path.Combine(_raiz, "cosas.json"),
            """[{"id":"vieja","valor":41},{"id":"otra","valor":42}]""");

        var leidos = Almacen().All().OrderBy(c => c.Id, StringComparer.Ordinal).ToList();

        Assert.Equal(new[] { "otra", "vieja" }, leidos.Select(c => c.Id));
        Assert.Equal(41, Almacen().Find("vieja")!.Valor);
    }

    /// <summary>
    /// El fichero anterior se queda donde está.
    /// </summary>
    /// <remarks>
    /// El despliegue tiene vuelta atrás automática (ADR 0133). Si la migración se llevara el
    /// <c>{nombre}.json</c>, la versión anterior arrancaría sobre un directorio que no conoce y
    /// serviría una capacidad vacía — en silencio.
    /// </remarks>
    [Fact]
    public void La_migracion_no_se_lleva_el_fichero_anterior()
    {
        var legado = Path.Combine(_raiz, "cosas.json");
        Directory.CreateDirectory(_raiz);
        File.WriteAllText(legado, """[{"id":"vieja","valor":41}]""");

        Almacen().All();

        Assert.True(File.Exists(legado));
    }

    /// <summary>
    /// Migrar dos veces no resucita lo viejo encima de lo nuevo.
    /// </summary>
    /// <remarks>
    /// Es el fallo silencioso de toda migración que se repite: el segundo arranque vuelca el array
    /// antiguo sobre documentos ya editados y deja la capacidad <i>coherente y equivocada</i>. La
    /// marca es lo único que lo impide; quitarla pone este test en rojo con «41, esperaba 99».
    /// </remarks>
    [Fact]
    public void Migrar_dos_veces_no_pisa_lo_editado()
    {
        Directory.CreateDirectory(_raiz);
        File.WriteAllText(Path.Combine(_raiz, "cosas.json"), """[{"id":"vieja","valor":41}]""");

        Almacen().All();                                  // migra
        Almacen().Put(new Cosa("vieja", 99));             // alguien la edita

        Assert.Equal(99, Almacen().Find("vieja")!.Valor); // otro arranque NO la devuelve a 41
    }
}
