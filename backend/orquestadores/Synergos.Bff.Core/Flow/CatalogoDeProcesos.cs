using System.Text.Json;

namespace Synergos.Bff.Core.Flow;

/// <summary>Una transición de un proceso: su nombre, de dónde a dónde y qué roles la pueden disparar (vacío: cualquiera).</summary>
public sealed record TransicionDeProceso(string Nombre, string Desde, string Hacia, IReadOnlyList<string> Roles);

/// <summary>Un proceso de <c>Api.Workflow</c> como dato: su clave y sus transiciones.</summary>
public sealed record DefinicionDeProceso(string Clave, string Inicial, IReadOnlyList<TransicionDeProceso> Transiciones);

/// <summary>
/// Los procesos contra los que se cruzan, AL ARRANCAR, los pasos de procesos de un flujo (núcleo, lote 7; 009-P3 = A): el
/// proceso que nombran existe, cada transición que disparan está en él, y el rol con que actúan es uno de los que exige.
/// </summary>
/// <remarks>
/// <para><b>Lee el formato de <c>tools/provisionar.procesos.json</c></b>, la definición versionada que publica la
/// provisión (009): una lista de definiciones, y las entradas con sólo <c>_</c> son comentarios. El orquestador lo lee
/// de <c>{Origen}:Procesos:Archivo</c>; las pruebas, del repositorio.</para>
///
/// <para><b>Lo que no cruza:</b> desde qué estado sale cada transición. Eso depende de la instancia, y lo dice la
/// capacidad al disparar (<c>workflow.transition_not_allowed</c>).</para>
/// </remarks>
public sealed class CatalogoDeProcesos
{
    private readonly Dictionary<string, DefinicionDeProceso> _procesos;

    private CatalogoDeProcesos(IEnumerable<DefinicionDeProceso> procesos)
    {
        _procesos = new Dictionary<string, DefinicionDeProceso>(StringComparer.Ordinal);
        foreach (var p in procesos)
        {
            if (!_procesos.TryAdd(p.Clave, p)) throw new FormatException($"El proceso «{p.Clave}» sale dos veces en el catálogo.");
        }
    }

    /// <summary>Las claves de los procesos.</summary>
    public IReadOnlyCollection<string> Claves => _procesos.Keys;

    /// <summary>El catálogo desde el JSON de la provisión.</summary>
    /// <exception cref="FormatException">Si no tiene la forma: una lista de definiciones con clave, inicial y transiciones.</exception>
    public static CatalogoDeProcesos Leer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new FormatException("El catálogo de procesos tiene que ser una lista.");

        var procesos = new List<DefinicionDeProceso>();
        foreach (var d in doc.RootElement.EnumerateArray())
        {
            if (d.ValueKind != JsonValueKind.Object) throw new FormatException("Cada entrada del catálogo de procesos tiene que ser un objeto.");
            if (d.EnumerateObject().All(p => p.Name == "_")) continue;

            var clave = Texto(d, "key", "una definición");
            var donde = $"el proceso «{clave}»";
            var transiciones = Propiedad(d, "transitions", donde);
            if (transiciones.ValueKind != JsonValueKind.Array) throw new FormatException($"{donde}: «transitions» tiene que ser una lista.");

            procesos.Add(new DefinicionDeProceso(clave, Texto(d, "initialState", donde), transiciones.EnumerateArray()
                .Select(t => new TransicionDeProceso(
                    Texto(t, "name", donde), Texto(t, "from", donde), Texto(t, "to", donde),
                    t.TryGetProperty("requiredRoles", out var roles) && roles.ValueKind == JsonValueKind.Array
                        ? roles.EnumerateArray().Select(r => r.GetString() ?? throw new FormatException($"{donde}: un rol tiene que ser un texto.")).ToList()
                        : []))
                .ToList()));
        }
        return new CatalogoDeProcesos(procesos);
    }

    /// <summary>
    /// Lo que no cuadra entre <paramref name="paso"/> y su proceso; vacío si cuadra, o si el paso no nombra ninguno.
    /// </summary>
    public IReadOnlyList<string> Revisar(PasoDef paso)
    {
        ArgumentNullException.ThrowIfNull(paso);
        if (paso.Proceso is not { } clave) return [];
        if (!_procesos.TryGetValue(clave, out var proceso))
        {
            return [$"el paso «{paso.Id}» actúa sobre el proceso «{clave}», que no está en el catálogo de procesos "
                    + $"({string.Join(", ", _procesos.Keys.Order(StringComparer.Ordinal))})."];
        }

        var errores = new List<string>();
        var nombradas = paso.Transiciones ?? (paso.Transicion is { } fija ? [fija] : []);
        foreach (var nombre in nombradas)
        {
            var reglas = proceso.Transiciones.Where(t => string.Equals(t.Nombre, nombre, StringComparison.Ordinal)).ToList();
            if (reglas.Count == 0)
            {
                errores.Add($"el paso «{paso.Id}» dispara «{nombre}», y «{clave}» no la tiene (tiene: "
                    + $"{string.Join(", ", proceso.Transiciones.Select(t => t.Nombre).Distinct(StringComparer.Ordinal))}).");
                continue;
            }

            // El rol, contra TODAS las reglas de ese nombre: una transición que sale de dos estados puede pedir roles
            // distintos en cada uno, y el paso no sabe desde cuál la va a disparar.
            if (paso.Rol is { } rol && reglas.FirstOrDefault(t => t.Roles.Count > 0 && !t.Roles.Contains(rol, StringComparer.Ordinal)) is { } exige)
            {
                errores.Add($"el paso «{paso.Id}» dispara «{nombre}» como «{rol}», y «{clave}» la reserva desde «{exige.Desde}» a "
                    + $"{string.Join(", ", exige.Roles)}: la capacidad la rechazaría (workflow.role_required).");
            }
        }
        return errores;
    }

    private static JsonElement Propiedad(JsonElement o, string nombre, string donde)
        => o.TryGetProperty(nombre, out var v) ? v : throw new FormatException($"{donde}: falta «{nombre}».");

    private static string Texto(JsonElement o, string nombre, string donde)
        => Propiedad(o, nombre, donde) is { ValueKind: JsonValueKind.String } v && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!
            : throw new FormatException($"{donde}: «{nombre}» tiene que ser un texto no vacío.");
}
