using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Synergos.CMS.Web.Services.Puerta;

/// <summary>Un parámetro de una operación del orquestador, que en la puerta viaja en la consulta.</summary>
/// <param name="Nombre">Su nombre, el mismo en la ruta del orquestador y en la consulta de la puerta.</param>
/// <param name="EnLaRuta">Si va en la ruta del orquestador (<c>{id}</c>) o en su consulta.</param>
/// <param name="Requerido">Si sin él la operación no se puede pedir.</param>
public sealed record ParametroDeLaPuerta(string Nombre, bool EnLaRuta, bool Requerido);

/// <summary>Una operación que un orquestador expone en la puerta, tal como la publica su contrato.</summary>
/// <param name="Flujo">La clave del flujo.</param>
/// <param name="Operacion">Su nombre en la puerta.</param>
/// <param name="Orquestador">El orquestador, por la convención de nombres: <c>Synergos.Bff.X</c> → <c>X</c>, con
/// su destino en <c>Synergos:X</c>.</param>
/// <param name="Metodo">GET o POST.</param>
/// <param name="Ruta">La ruta del orquestador, relativa y con sus parámetros entre llaves.</param>
/// <param name="Parametros">Los de la ruta y los de la consulta.</param>
/// <param name="ConCuerpo">Si lleva el cuerpo del navegador.</param>
/// <param name="LargoDeLaLlave">El largo de <c>Idempotency-Key</c> que acepta, o nulo si no la lee.</param>
/// <param name="LlaveRequerida">Si sin llave rechaza.</param>
/// <param name="CabecerasDeLaPuerta">Las cabeceras que pone la puerta (<c>x-synergos-puerta</c>), con si son requeridas.</param>
public sealed record OperacionDeLaPuerta(
    string Flujo, string Operacion, string Orquestador, string Metodo, string Ruta,
    IReadOnlyList<ParametroDeLaPuerta> Parametros, bool ConCuerpo, int? LargoDeLaLlave, bool LlaveRequerida,
    IReadOnlyDictionary<string, bool> CabecerasDeLaPuerta)
{
    /// <summary>Si la operación lee la cabecera <paramref name="nombre"/> que pone la puerta.</summary>
    public bool Declara(string nombre) => CabecerasDeLaPuerta.ContainsKey(nombre);
}

/// <summary>
/// Qué puede pedir el navegador por la puerta, sacado de los contratos de los orquestadores que el CMS
/// lleva incrustados (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>Una sola fuente, ya vigilada.</b> El orquestador marca cada endpoint que expone con
/// <c>.EnLaPuerta(flujo, operacion)</c>, el contrato lo publica como <c>x-synergos-flujo</c> y los cinco
/// gates del contrato mantienen ese documento fiel al código. La puerta lo incrusta y lo lee: lo que no
/// está marcado no existe para ella, sin tener que listarlo en ningún sitio, y lo marcado trae su método,
/// su ruta, sus parámetros, si lleva cuerpo y qué cabeceras de la puerta lee.</para>
///
/// <para><b>Incrustado y no leído del disco en ejecución</b>: el CMS y el orquestador se despliegan como
/// imágenes distintas, y el contrato contra el que la puerta se armó tiene que viajar dentro de la del CMS.
/// Que lo incrustado sea lo del disco lo vigila <c>PuertaGenericaTests</c>.</para>
///
/// <para><b>Lo que la puerta no sabe hacer revienta al arrancar, no en la primera petición</b>: una marca
/// mal formada, dos operaciones con el mismo nombre, un flujo repartido entre dos orquestadores, una
/// cabecera requerida que la puerta no sabe poner, o un parámetro que llegaría dos veces a la consulta.</para>
/// </remarks>
public sealed class TablaDeLaPuerta
{
    /// <summary>Con qué nombre lógico se incrustan los contratos.</summary>
    public const string PrefijoDelRecurso = "contratos/";

    /// <summary>El prefijo de un contrato de orquestador; los de capacidad no se leen.</summary>
    public const string PrefijoDelOrquestador = "Synergos.Bff.";

    /// <summary>La marca de una operación expuesta.</summary>
    public const string MarcaDelFlujo = "x-synergos-flujo";

    /// <summary>La marca de una cabecera que pone la puerta.</summary>
    public const string MarcaDeLaPuerta = "x-synergos-puerta";

    /// <summary>El vocabulario fijo de saga con que la puerta nombra las operaciones.</summary>
    public static readonly IReadOnlyList<string> Operaciones = ["abrir", "cancelar", "cerrar", "consultar"];

    private static readonly Regex ClaveDeFlujo = new(@"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ParametroDeRuta = new(@"\{([^}]+)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private readonly Dictionary<(string Flujo, string Operacion), OperacionDeLaPuerta> _operaciones;

    private TablaDeLaPuerta(IReadOnlyList<string> orquestadores, Dictionary<(string, string), OperacionDeLaPuerta> operaciones)
    {
        Orquestadores = orquestadores;
        _operaciones = operaciones;
    }

    /// <summary>Los orquestadores cuyo contrato va incrustado, tengan o no operaciones marcadas.</summary>
    public IReadOnlyList<string> Orquestadores { get; }

    /// <summary>Todas las operaciones expuestas.</summary>
    public IReadOnlyCollection<OperacionDeLaPuerta> Todas => _operaciones.Values;

    /// <summary>La operación <paramref name="operacion"/> de <paramref name="flujo"/>, o nula si no está expuesta.</summary>
    public OperacionDeLaPuerta? Buscar(string flujo, string operacion)
        => _operaciones.GetValueOrDefault((flujo, operacion));

    /// <summary>Las operaciones de un flujo.</summary>
    public IReadOnlyList<OperacionDeLaPuerta> DelFlujo(string flujo)
        => _operaciones.Values.Where(o => string.Equals(o.Flujo, flujo, StringComparison.Ordinal)).ToList();

    /// <summary>La tabla de los contratos incrustados en este ensamblado.</summary>
    public static TablaDeLaPuerta Incrustada()
    {
        var ensamblado = typeof(TablaDeLaPuerta).Assembly;
        return De(Recursos(ensamblado).Select(n =>
        {
            using var s = ensamblado.GetManifestResourceStream(n)!;
            using var r = new StreamReader(s);
            return (n[PrefijoDelRecurso.Length..], r.ReadToEnd());
        }));
    }

    /// <summary>Los nombres de los contratos de orquestador incrustados en <paramref name="ensamblado"/>.</summary>
    public static IReadOnlyList<string> Recursos(Assembly ensamblado)
        => ensamblado.GetManifestResourceNames()
            .Where(n => n.StartsWith(PrefijoDelRecurso + PrefijoDelOrquestador, StringComparison.Ordinal)
                        && n.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>Arma la tabla de unos contratos: <c>(fichero, json)</c>.</summary>
    /// <exception cref="InvalidOperationException">Si un contrato marca algo que la puerta no sabe pasar.</exception>
    public static TablaDeLaPuerta De(IEnumerable<(string Fichero, string Json)> contratos)
    {
        var orquestadores = new List<string>();
        var operaciones = new Dictionary<(string, string), OperacionDeLaPuerta>();
        var errores = new List<string>();

        foreach (var (fichero, json) in contratos)
        {
            using var doc = JsonDocument.Parse(json);
            var titulo = doc.RootElement.GetProperty("info").GetProperty("title").GetString() ?? string.Empty;
            if (!titulo.StartsWith(PrefijoDelOrquestador, StringComparison.Ordinal))
            {
                errores.Add($"{fichero}: info.title «{titulo}» no es de un orquestador ({PrefijoDelOrquestador}X).");
                continue;
            }
            var orquestador = titulo[PrefijoDelOrquestador.Length..];
            orquestadores.Add(orquestador);

            foreach (var ruta in doc.RootElement.GetProperty("paths").EnumerateObject())
            {
                foreach (var metodo in ruta.Value.EnumerateObject())
                {
                    if (!metodo.Value.TryGetProperty(MarcaDelFlujo, out var marca)) continue;

                    var quien = $"{fichero} {metodo.Name.ToUpperInvariant()} {ruta.Name}";
                    var op = Operacion(orquestador, ruta.Name, metodo.Name, metodo.Value, marca, quien, errores);
                    if (op is null) continue;

                    if (!operaciones.TryAdd((op.Flujo, op.Operacion), op))
                    {
                        errores.Add($"{quien}: {op.Flujo}/{op.Operacion} ya la expone otra operación.");
                    }
                }
            }
        }

        foreach (var flujo in operaciones.Values.GroupBy(o => o.Flujo, StringComparer.Ordinal))
        {
            if (flujo.Select(o => o.Orquestador).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                errores.Add($"El flujo {flujo.Key} está repartido entre orquestadores: una saga es de uno solo.");
            }
        }

        if (errores.Count > 0)
        {
            throw new InvalidOperationException(
                "Los contratos incrustados exponen en la puerta algo que ella no sabe pasar:" + Environment.NewLine
                + string.Join(Environment.NewLine, errores.Select(e => "  " + e)));
        }

        return new TablaDeLaPuerta(orquestadores, operaciones);
    }

    private static OperacionDeLaPuerta? Operacion(
        string orquestador, string ruta, string metodo, JsonElement op, JsonElement marca, string quien, List<string> errores)
    {
        if (marca.ValueKind != JsonValueKind.Object
            || marca.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).SequenceEqual(["flujo", "operacion"]) is false)
        {
            errores.Add($"{quien}: {MarcaDelFlujo} tiene que ser {{ flujo, operacion }} y nada más.");
            return null;
        }

        var flujo = marca.GetProperty("flujo").GetString() ?? string.Empty;
        var nombre = marca.GetProperty("operacion").GetString() ?? string.Empty;
        if (!ClaveDeFlujo.IsMatch(flujo)) errores.Add($"{quien}: «{flujo}» no es una clave de flujo (dominio.flujo).");
        if (!Operaciones.Contains(nombre, StringComparer.Ordinal))
        {
            errores.Add($"{quien}: «{nombre}» no es del vocabulario de la puerta ({string.Join(", ", Operaciones)}).");
        }

        var verbo = metodo.ToUpperInvariant();
        if (verbo is not ("GET" or "POST")) errores.Add($"{quien}: la puerta sólo pasa GET y POST.");

        var parametros = new List<ParametroDeLaPuerta>();
        var cabeceras = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        int? largoDeLaLlave = null;
        var llaveRequerida = false;

        foreach (var p in op.TryGetProperty("parameters", out var ps) ? ps.EnumerateArray() : Enumerable.Empty<JsonElement>())
        {
            var nombreDelParametro = p.GetProperty("name").GetString() ?? string.Empty;
            var requerido = p.TryGetProperty("required", out var r) && r.GetBoolean();
            switch (p.GetProperty("in").GetString())
            {
                case "path":
                case "query":
                    if (parametros.Any(x => string.Equals(x.Nombre, nombreDelParametro, StringComparison.Ordinal)))
                    {
                        errores.Add($"{quien}: «{nombreDelParametro}» llegaría dos veces a la consulta de la puerta.");
                    }
                    parametros.Add(new ParametroDeLaPuerta(nombreDelParametro, p.GetProperty("in").GetString() == "path", requerido));
                    break;
                case "header" when string.Equals(nombreDelParametro, LoQuePoneLaPuerta.CabeceraDeLaLlave, StringComparison.OrdinalIgnoreCase):
                    largoDeLaLlave = p.TryGetProperty("schema", out var s) && s.TryGetProperty("maxLength", out var m) ? m.GetInt32() : int.MaxValue;
                    llaveRequerida = requerido;
                    if (largoDeLaLlave < LoQuePoneLaPuerta.LargoDeLaLlave)
                    {
                        errores.Add($"{quien}: acepta llaves de {largoDeLaLlave} y la puerta reemite de {LoQuePoneLaPuerta.LargoDeLaLlave}.");
                    }
                    break;
                case "header" when p.TryGetProperty(MarcaDeLaPuerta, out var dePuerta) && dePuerta.ValueKind == JsonValueKind.True:
                    if (!LoQuePoneLaPuerta.Cabeceras.Contains(nombreDelParametro, StringComparer.OrdinalIgnoreCase))
                    {
                        errores.Add($"{quien}: la puerta no sabe poner la cabecera {nombreDelParametro}.");
                    }
                    cabeceras[nombreDelParametro] = requerido;
                    break;
                default:
                    // Del navegador no se copia ninguna cabecera: una que la operación exija y la puerta no
                    // ponga daría un rechazo seguro en cada petición.
                    if (requerido) errores.Add($"{quien}: exige {nombreDelParametro} y la puerta no la pone ni la copia.");
                    break;
            }
        }

        foreach (Match m in ParametroDeRuta.Matches(ruta))
        {
            if (!parametros.Any(p => p.EnLaRuta && string.Equals(p.Nombre, m.Groups[1].Value, StringComparison.Ordinal)))
            {
                errores.Add($"{quien}: la ruta lleva {{{m.Groups[1].Value}}} y no lo declara.");
            }
        }

        return new OperacionDeLaPuerta(
            flujo, nombre, orquestador, verbo, ruta.TrimStart('/'), parametros, op.TryGetProperty("requestBody", out _),
            largoDeLaLlave, llaveRequerida, cabeceras);
    }
}
