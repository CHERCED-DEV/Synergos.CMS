using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Lo que uSync le ofrece al editor en los selectores de un elemento migrado: por cada propiedad
/// del ElementType cuyo DataType es un desplegable, una lista de radios o de casillas, sus
/// prevalores (#181).
/// </summary>
/// <remarks>
/// <para><b>Es la mitad «lo que el editor puede elegir» del gate de vocabulario.</b> La otra mitad
/// —lo que el elemento pinta— vive en el UI y se deriva de su sanitizador. En el medio va el
/// resolver: <see cref="ContratoSynHostTests"/> pasa cada prevalor por el resolver REAL y escribe
/// en el contrato lo que viaja, que es lo que el UI cruza.</para>
///
/// <para><b>El ElementType de un record no se escribe: se sigue.</b> La vista SynHost que inyecta
/// <c>IResolutorSynHost&lt;XProps&gt;</c> es UNA (hay gate), y los componentes del Block Grid que
/// la llaman (<c>blockgrid/Components/elementSynX.cshtml</c> → <c>SynHost/X</c>) se llaman como el
/// alias del ElementType. Una tabla record → alias sería la tercera copia del vínculo.</para>
///
/// <para>Lee el XML con <see cref="XDocument"/> y no con regex: una propiedad partida en líneas o
/// un atributo de más no tienen que dejar el gate en verde sobre nada.</para>
/// </remarks>
internal static class SelectoresDeUSync
{
    /// <summary>Los editores de Umbraco 13 que ofrecen una lista CERRADA de valores.</summary>
    public static readonly IReadOnlySet<string> EditoresDeSeleccion = new HashSet<string>(StringComparer.Ordinal)
    {
        "Umbraco.DropDown.Flexible",
        "Umbraco.RadioButtonList",
        "Umbraco.CheckBoxList",
    };

    /// <summary>Un selector de un ElementType, con lo que ofrece al editor.</summary>
    /// <param name="Propiedad">El alias de la propiedad (<c>position</c>).</param>
    /// <param name="DataType">El alias del DataType (<c>DTSelectScreenPosition</c>).</param>
    /// <param name="Multiple">Si el editor puede marcar varios (el valor llega como lista).</param>
    /// <param name="Prevalores">Lo que el editor ve, en el orden de uSync.</param>
    /// <param name="Propia">Si la declara el ElementType y no una composición.</param>
    public sealed record Selector(string Propiedad, string DataType, bool Multiple, IReadOnlyList<string> Prevalores, bool Propia);

    private static string Web(string repo) => Path.Combine(repo, "Synergos.CMS.Web");

    private static string USync(string repo) => Path.Combine(Web(repo), "uSync", "v9");

    /// <summary>
    /// Los alias de los ElementTypes que se pintan con el resolver de <paramref name="record"/>:
    /// los componentes del Block Grid que llaman a la vista que lo inyecta.
    /// </summary>
    public static IReadOnlyList<string> ElementTypesDe(string repo, Type record)
    {
        var vistas = Path.Combine(Web(repo), "Views", "Partials");
        var inyecta = new Regex(@"@inject\s+[\w.]*IResolutorSynHost<[\w.]*\b" + Regex.Escape(record.Name) + ">");

        var vista = Directory.EnumerateFiles(Path.Combine(vistas, "SynHost"), "*.cshtml")
            .Where(f => inyecta.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileNameWithoutExtension)
            .SingleOrDefault()
            ?? throw new InvalidOperationException($"Ninguna vista de SynHost/ inyecta IResolutorSynHost<{record.Name}>.");

        var llamada = new Regex("[\"/]SynHost/" + Regex.Escape(vista) + "\"");
        return Directory.EnumerateFiles(Path.Combine(vistas, "blockgrid", "Components"), "*.cshtml")
            .Where(f => llamada.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Los selectores de <paramref name="aliasDelElementType"/>: los suyos y los de sus composiciones
    /// (a cualquier profundidad), ordenados por alias.
    /// </summary>
    public static IReadOnlyList<Selector> De(string repo, string aliasDelElementType)
    {
        var (tipos, dataTypes) = Leidos.GetOrAdd(repo, r => (TiposDeContenido(r), DataTypes(r)));

        if (!tipos.TryGetValue(aliasDelElementType, out var propio))
        {
            throw new InvalidOperationException($"No hay ContentType «{aliasDelElementType}» en uSync/v9/ContentTypes.");
        }

        var selectores = new List<Selector>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var pendientes = new Queue<(XElement Tipo, bool Propia)>([(propio, true)]);

        while (pendientes.TryDequeue(out var actual))
        {
            foreach (var propiedad in actual.Tipo.Descendants("GenericProperty"))
            {
                var alias = (string?)propiedad.Element("Alias") ?? string.Empty;
                var definicion = (string?)propiedad.Element("Definition") ?? string.Empty;
                if (!vistos.Add(alias) || !dataTypes.TryGetValue(definicion, out var dataType))
                {
                    continue;
                }

                if (EditoresDeSeleccion.Contains(dataType.Editor))
                {
                    selectores.Add(new Selector(alias, dataType.Alias, dataType.Multiple, dataType.Items, actual.Propia));
                }
            }

            foreach (var composicion in actual.Tipo.Descendants("Composition").Select(c => c.Value.Trim()))
            {
                if (tipos.TryGetValue(composicion, out var tipo))
                {
                    pendientes.Enqueue((tipo, false));
                }
            }
        }

        return selectores.OrderBy(s => s.Propiedad, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// uSync leído UNA vez por proceso: son ~390 XML y la derivación pregunta por cada uno de los
    /// 36 elementos. Una mutación del schema se ve en la corrida siguiente, que es como corre.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (Dictionary<string, XElement> Tipos, Dictionary<string, DataTypeDeUSync> DataTypes)> Leidos =
        new(StringComparer.Ordinal);

    private static Dictionary<string, XElement> TiposDeContenido(string repo)
        => Directory.EnumerateFiles(Path.Combine(USync(repo), "ContentTypes"), "*.config")
            .Select(f => XDocument.Load(f).Root)
            .Where(r => r is not null && r.Name.LocalName == "ContentType")
            .ToDictionary(r => (string)r!.Attribute("Alias")!, r => r!, StringComparer.Ordinal);

    private sealed record DataTypeDeUSync(string Alias, string Editor, bool Multiple, IReadOnlyList<string> Items);

    /// <summary>Los DataTypes por su <c>Key</c>, que es lo que guarda <c>&lt;Definition&gt;</c>.</summary>
    private static Dictionary<string, DataTypeDeUSync> DataTypes(string repo)
    {
        var porKey = new Dictionary<string, DataTypeDeUSync>(StringComparer.OrdinalIgnoreCase);
        foreach (var fichero in Directory.EnumerateFiles(Path.Combine(USync(repo), "DataTypes"), "*.config"))
        {
            var raiz = XDocument.Load(fichero).Root;
            if (raiz is null || raiz.Name.LocalName != "DataType")
            {
                continue;
            }

            var editor = (string?)raiz.Element("Info")?.Element("EditorAlias") ?? string.Empty;
            var (multiple, items) = EditoresDeSeleccion.Contains(editor)
                ? Opciones(editor, (string?)raiz.Element("Config") ?? string.Empty, fichero)
                : (false, Array.Empty<string>());

            porKey[(string)raiz.Attribute("Key")!] = new DataTypeDeUSync((string)raiz.Attribute("Alias")!, editor, multiple, items);
        }

        return porKey;
    }

    /// <summary>
    /// Los prevalores de un selector y si admite varios. Una casilla múltiple
    /// (<c>CheckBoxList</c>) admite varios siempre; un desplegable, si su <c>multiple</c> lo dice.
    /// </summary>
    /// <remarks>
    /// Las claves se buscan sin mayúsculas: los DataTypes de fábrica de Umbraco (<c>Checkbox list</c>,
    /// <c>Radiobox</c>) escriben <c>Items</c>, vacío, y los nuestros <c>items</c>. Un selector sin
    /// opciones no ofrece nada y no se cruza; uno cuyo <c>items</c> no es una lista es un XML roto.
    /// </remarks>
    private static (bool Multiple, IReadOnlyList<string> Items) Opciones(string editor, string config, string fichero)
    {
        using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(config) ? "{}" : config);
        var raiz = json.RootElement;

        JsonElement? Clave(string nombre)
            => raiz.ValueKind == JsonValueKind.Object
                ? raiz.EnumerateObject().Where(p => string.Equals(p.Name, nombre, StringComparison.OrdinalIgnoreCase)).Select(p => (JsonElement?)p.Value).FirstOrDefault()
                : null;

        var items = Clave("items");
        if (items is { ValueKind: not JsonValueKind.Array })
        {
            throw new InvalidOperationException($"{Path.GetFileName(fichero)}: `items` del Config no es una lista.");
        }

        var valores = (items is { } lista ? lista.EnumerateArray().ToList() : new List<JsonElement>())
            .Select(i => i.ValueKind == JsonValueKind.String ? i.GetString() : i.TryGetProperty("value", out var v) ? v.GetString() : null)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .ToList();

        var multiple = editor == "Umbraco.CheckBoxList" || Clave("multiple") is { ValueKind: JsonValueKind.True };

        return (multiple, valores);
    }
}
