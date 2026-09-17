namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Dónde vive cada proyecto, resuelto DEL DISCO y no escrito.
/// </summary>
/// <remarks>
/// <para><b>Por qué existe, y es la lección entera del #136.</b> Los gates leen la FUENTE del
/// árbol —es lo que les permite vigilar un Razor, un <c>.mjs</c>, un compose o un
/// <c>appsettings</c>, ninguno de los cuales tiene tipos— y para eso construían la ruta
/// literal: <c>Path.Combine(RepoRoot(), "Synergos.Api.Booking", "Domain")</c>. Con el árbol
/// plano eso era correcto. Al mover el backend a <c>backend/capacidades/</c> pasó a ser
/// <b>treinta y ocho rutas equivocadas</b>, y arreglarlas a mano habría escrito la disposición
/// nueva en treinta y ocho sitios — la misma deuda con otro valor.</para>
///
/// <para><b>Lo que rompe el ciclo es invertir la dependencia.</b> Un gate no necesita saber
/// DÓNDE está un proyecto: necesita su fuente. Así que pregunta por NOMBRE y esta clase lo
/// encuentra buscando su <c>.csproj</c>. La próxima reorganización —y va a haber otra, porque
/// faltan cuatro orquestadores y un quinto árbol de verticales— cuesta <b>cero</b> ediciones en
/// los gates.</para>
///
/// <para><b>Y falla a gritos, no en silencio</b>, que es la mitad que importa: un nombre que no
/// existe lanza nombrando lo que sí hay. La alternativa —devolver una ruta inventada— dejaría a
/// un gate leyendo un directorio vacío y <b>pasando en verde sin mirar nada</b>, que es
/// exactamente el fallo que este repo ya tiene escrito tres veces (#118, #133, el molde).</para>
///
/// <para><b>El caché no es optimización.</b> Los 394 gates de este ensamblado preguntan cientos
/// de veces; recorrer el árbol entero en cada llamada convertiría la suite de tres segundos en
/// una de minutos. Se recorre <b>una vez</b> por proceso.</para>
/// </remarks>
public static class Proyectos
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> _mapa = new(Descubrir);

    /// <summary>
    /// La raíz del repo: se sube hasta el fichero que sólo existe ahí.
    /// </summary>
    /// <remarks>
    /// Se ancla en <c>Synergos.CMS.sln</c> —la integradora— y no en una carpeta: una carpeta se
    /// renombra y el ancla se pierde sin que nada falle hasta la primera lectura.
    /// </remarks>
    public static string Raiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IReadOnlyDictionary<string, string> Descubrir()
    {
        var mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var csproj in Directory.EnumerateFiles(Raiz(), "*.csproj", SearchOption.AllDirectories))
        {
            // bin/ y obj/ guardan copias de los proyectos al restaurar, y `_archive/` es el
            // legado (§6). Contarlos haría que esto midiera el estado del build, no el del árbol.
            if (csproj.Split(Path.DirectorySeparatorChar).Any(
                    s => s.Equals("bin", StringComparison.OrdinalIgnoreCase)
                      || s.Equals("obj", StringComparison.OrdinalIgnoreCase)
                      || s.Equals("_archive", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            mapa[Path.GetFileNameWithoutExtension(csproj)] = Path.GetDirectoryName(csproj)!;
        }

        return mapa;
    }

    /// <summary>
    /// El directorio de <paramref name="proyecto"/>, y opcionalmente algo dentro.
    /// </summary>
    /// <example><c>Proyectos.Dir("Synergos.Api.Booking", "Domain")</c></example>
    public static string Dir(string proyecto, params string[] dentro)
    {
        if (!_mapa.Value.TryGetValue(proyecto, out var dir))
        {
            throw new DirectoryNotFoundException(
                $"No existe el proyecto «{proyecto}». Los que hay: "
                + string.Join(", ", _mapa.Value.Keys.OrderBy(k => k, StringComparer.Ordinal))
                + ". Si se renombró, este gate tiene que seguir el nombre nuevo — y si se movió, "
                + "no hay nada que tocar: esta clase lo encuentra donde esté (#136).");
        }

        return dentro.Length == 0 ? dir : Path.Combine([dir, .. dentro]);
    }

    /// <summary>
    /// Como <see cref="Dir(string, string[])"/>, pero tolera que la primera parte NO sea un
    /// proyecto: entonces la resuelve contra la raíz del repo.
    /// </summary>
    /// <remarks>
    /// <para>Existe para los helpers que cada gate tiene —<c>Fuente(params string[])</c>,
    /// <c>Dir(params string[])</c>— y que reciben indistintamente
    /// <c>("Synergos.Api.Cart", "Domain", "CartService.cs")</c> y
    /// <c>("docs", "product", "11-mapa-del-cableado.md")</c>. Sin esto, cada uno tendría que
    /// ramificar por su cuenta y habría veinte formas de decidir lo mismo.</para>
    ///
    /// <para><b>El desempate es por EXISTENCIA, no por prefijo.</b> Filtrar por
    /// <c>"Synergos."</c> parece más barato y es peor: <c>Synergos.CMS.Web</c> es un proyecto Y
    /// una carpeta de la raíz, así que las dos lecturas dan lo mismo y nadie nota el criterio —
    /// hasta el día que un proyecto no esté donde su nombre dice. Preguntar al mapa primero da la
    /// respuesta correcta en los dos casos.</para>
    /// </remarks>
    public static string Ruta(params string[] partes)
    {
        if (partes.Length == 0) return Raiz();

        var cabeza = _mapa.Value.TryGetValue(partes[0], out var dir) ? dir : Path.Combine(Raiz(), partes[0]);
        return partes.Length == 1 ? cabeza : Path.Combine([cabeza, .. partes[1..]]);
    }

    /// <summary>
    /// Los directorios de todos los proyectos cuyo nombre empieza por
    /// <paramref name="prefijo"/>, ordenados por nombre.
    /// </summary>
    /// <example><c>Proyectos.Todos("Synergos.Api.")</c> → las veinte capacidades.</example>
    /// <remarks>
    /// Reemplaza a <c>Directory.EnumerateDirectories(raiz, "Synergos.Api.*")</c>, que además de
    /// cablear la disposición <b>dependía de que estuvieran todas al mismo nivel</b> — cierto
    /// mientras el árbol fue plano y falso desde el #136.
    /// </remarks>
    public static IReadOnlyList<string> Todos(string prefijo)
        => _mapa.Value
            .Where(e => e.Key.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => e.Value)
            .ToList();

    /// <summary>
    /// TODOS los directorios de proyecto del repo, ordenados por nombre.
    /// </summary>
    /// <remarks>
    /// Reemplaza a <c>Directory.EnumerateDirectories(RepoRoot())</c>, que era correcto mientras
    /// el árbol fue plano: los gates enumeraban la raíz y filtraban por prefijo. Desde el #136 el
    /// backend vive en <c>backend/{nucleo,capacidades,orquestadores}/</c>, así que enumerar la
    /// raíz devuelve <b>una</b> carpeta —<c>backend</c>— y los filtros de prefijo se quedan sin
    /// nada que filtrar: <b>el gate pasa en verde sin mirar un solo proyecto</b>, que es el fallo
    /// silencioso de siempre. Devolver la lista completa deja los <c>.Where(prefijo)</c> de cada
    /// gate intactos, que es donde está escrito lo que ese gate quiere decir.
    /// </remarks>
    public static IReadOnlyList<string> Directorios()
        => _mapa.Value.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Value).ToList();

    /// <summary>Los NOMBRES de todos los proyectos con ese prefijo, ordenados.</summary>
    public static IReadOnlyList<string> Nombres(string prefijo)
        => _mapa.Value.Keys
            .Where(k => k.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToList();
}
