using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El cuerpo de una página (un Block Grid) abierto para editarlo por piezas: sumar secciones, ponerlas arriba, cambiar
/// una propiedad de los bloques de un tipo, poner un bloque antes de otro y desconectar las secciones de un tipo.
/// </summary>
/// <remarks>
/// Lo usa <see cref="DevContenidoDeVerticales"/> (016-contenido-piezas). Cada operación es idempotente por sí sola:
/// repetirla sobre su propio resultado no cambia nada, y <see cref="Cambio"/> dice si hay algo que guardar.
/// «Desconectar» quita el bloque de ESTA página, nunca su tipo del esquema (SUPERSET).
/// </remarks>
public sealed class CuerpoEditable
{
    /// <summary>Sin escapar tildes ni eñes: lo que se busca con <see cref="Contiene"/> se escribe como se lee.</summary>
    private static readonly JsonSerializerOptions Legible = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly JsonObject _cuerpo;

    public CuerpoEditable(string? json)
    {
        _cuerpo = string.IsNullOrWhiteSpace(json)
            ? new JsonObject()
            : JsonNode.Parse(json)!.AsObject();
        _ = Layout;
        _ = Lista("contentData");
        _ = Lista("settingsData");
    }

    /// <summary>Cuántas operaciones cambiaron algo.</summary>
    public int Cambios { get; private set; }

    public bool Cambio => Cambios > 0;

    public string Json => _cuerpo.ToJsonString(Legible);

    public bool Contiene(string texto) => Json.Contains(texto, StringComparison.Ordinal);

    /// <summary>Suma las secciones de <paramref name="nuevo"/> al final. Devuelve cuántas.</summary>
    public int Sumar(string nuevo) => Pegar(nuevo, alPrincipio: false);

    /// <summary>Pone las secciones de <paramref name="nuevo"/> arriba de todo. Devuelve cuántas.</summary>
    public int InsertarAlPrincipio(string nuevo) => Pegar(nuevo, alPrincipio: true);

    /// <summary>
    /// Pone las secciones de <paramref name="nuevo"/> justo antes de la sección de arriba que contiene un bloque del
    /// tipo <paramref name="antesDe"/>; si no hay ninguna, al final. Devuelve cuántas.
    /// </summary>
    public int InsertarSeccionesAntesDe(Guid antesDe, string nuevo)
    {
        var udis = DatosDe(antesDe).Select(d => d["udi"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var indice = Layout.Select((s, i) => (s, i)).FirstOrDefault(x => UdisDe(x.s!).Any(udis.Contains), (null, -1)).Item2;
        if (indice < 0) { return Sumar(nuevo); }
        return Pegar(nuevo, alPrincipio: false, en: indice);
    }

    /// <summary>
    /// Fija <paramref name="propiedad"/> = <paramref name="valor"/> en cada bloque del tipo dado. Devuelve cuántos
    /// cambiaron (los que ya tenían ese valor no cuentan).
    /// </summary>
    public int Reemplazar(Guid tipo, string propiedad, JsonNode valor)
    {
        var n = 0;
        foreach (var dato in DatosDe(tipo))
        {
            if (JsonNode.DeepEquals(dato[propiedad], valor)) { continue; }
            dato[propiedad] = valor.DeepClone();
            n++;
        }
        if (n > 0) { Cambios++; }
        return n;
    }

    /// <summary>Lo mismo con un texto.</summary>
    public int Reemplazar(Guid tipo, string propiedad, string valor) => Reemplazar(tipo, propiedad, JsonValue.Create(valor)!);

    /// <summary>
    /// Pone el primer bloque de <paramref name="bloque"/> (un cuerpo de una sola sección construida con
    /// <see cref="BlockGridJsonBuilder"/>; se toma su bloque de arriba) justo antes del primer bloque del tipo
    /// <paramref name="antesDe"/>, en la misma área. Si el área ya tiene uno con <paramref name="siYaContiene"/>, no hace nada.
    /// </summary>
    public bool InsertarAntesDe(Guid antesDe, string bloque, string siYaContiene)
    {
        var objetivo = DatosDe(antesDe).FirstOrDefault();
        if (objetivo is null) { return false; }
        var udi = objetivo["udi"]!.GetValue<string>();
        var (area, indice) = Ubicar(Layout, udi);
        if (area is null) { return false; }

        var udisDelArea = area.Select(i => i!["contentUdi"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var yaEsta = Lista("contentData").Any(d =>
            udisDelArea.Contains(d!["udi"]!.GetValue<string>()) && d.ToJsonString(Legible).Contains(siYaContiene, StringComparison.Ordinal));
        if (yaEsta) { return false; }

        var nuevo = JsonNode.Parse(bloque)!.AsObject();
        var item = ((JsonArray)nuevo["layout"]!["Umbraco.BlockGrid"]!)[0]!.DeepClone();
        area.Insert(indice, item);
        foreach (var d in (JsonArray)nuevo["contentData"]!) { Lista("contentData").Add(d!.DeepClone()); }
        Cambios++;
        return true;
    }

    /// <summary>
    /// Desconecta de esta página cada sección de arriba que contiene un bloque del tipo dado: sale del layout y salen
    /// sus datos. El tipo sigue en el esquema. Devuelve cuántas secciones quitó.
    /// </summary>
    public int DesconectarSeccionesCon(Guid tipo)
    {
        var udis = DatosDe(tipo).Select(d => d["udi"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        if (udis.Count == 0) { return 0; }

        var quitadas = 0;
        foreach (var seccion in Layout.ToList())
        {
            var delArbol = UdisDe(seccion!).ToList();
            if (!delArbol.Any(udis.Contains)) { continue; }
            Layout.Remove(seccion);
            var fuera = delArbol.ToHashSet(StringComparer.Ordinal);
            foreach (var lista in new[] { Lista("contentData"), Lista("settingsData") })
            {
                foreach (var d in lista.Where(d => fuera.Contains(d!["udi"]!.GetValue<string>())).ToList()) { lista.Remove(d); }
            }
            quitadas++;
        }
        if (quitadas > 0) { Cambios++; }
        return quitadas;
    }

    private int Pegar(string nuevo, bool alPrincipio, int? en = null)
    {
        var b = JsonNode.Parse(nuevo)!.AsObject();
        var origen = (JsonArray)b["layout"]!["Umbraco.BlockGrid"]!;
        var items = origen.Select(i => i!.DeepClone()).ToList();
        if (items.Count == 0) { return 0; }
        for (var i = 0; i < items.Count; i++)
        {
            if (en is { } desde) { Layout.Insert(desde + i, items[i]); }
            else if (alPrincipio) { Layout.Insert(i, items[i]); }
            else { Layout.Add(items[i]); }
        }
        foreach (var prop in new[] { "contentData", "settingsData" })
        {
            if (b[prop] is JsonArray datos)
            {
                foreach (var d in datos) { Lista(prop).Add(d!.DeepClone()); }
            }
        }
        Cambios++;
        return items.Count;
    }

    private IEnumerable<JsonObject> DatosDe(Guid tipo)
    {
        var clave = tipo.ToString("D");
        return Lista("contentData").OfType<JsonObject>()
            .Where(d => string.Equals(d["contentTypeKey"]?.GetValue<string>(), clave, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private JsonArray Layout
    {
        get
        {
            var layout = _cuerpo["layout"] as JsonObject ?? new JsonObject();
            _cuerpo["layout"] = layout;
            var grid = layout["Umbraco.BlockGrid"] as JsonArray ?? new JsonArray();
            layout["Umbraco.BlockGrid"] = grid;
            return grid;
        }
    }

    private JsonArray Lista(string nombre)
    {
        var lista = _cuerpo[nombre] as JsonArray ?? new JsonArray();
        _cuerpo[nombre] = lista;
        return lista;
    }

    private static IEnumerable<string> UdisDe(JsonNode item)
    {
        if (item["contentUdi"]?.GetValue<string>() is { } c) { yield return c; }
        if (item["settingsUdi"]?.GetValue<string>() is { } s) { yield return s; }
        if (item["areas"] is JsonArray areas)
        {
            foreach (var area in areas)
            {
                if (area?["items"] is not JsonArray hijos) { continue; }
                foreach (var hijo in hijos)
                {
                    foreach (var u in UdisDe(hijo!)) { yield return u; }
                }
            }
        }
    }

    private static (JsonArray? Area, int Indice) Ubicar(JsonArray items, string udi)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i]!;
            if (item["contentUdi"]?.GetValue<string>() == udi) { return (items, i); }
            if (item["areas"] is not JsonArray areas) { continue; }
            foreach (var area in areas)
            {
                if (area?["items"] is not JsonArray hijos) { continue; }
                var hallado = Ubicar(hijos, udi);
                if (hallado.Area is not null) { return hallado; }
            }
        }
        return (null, -1);
    }
}
