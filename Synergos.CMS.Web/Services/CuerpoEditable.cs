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
    public int InsertarSeccionesAntesDe(Guid antesDe, string nuevo, Func<JsonObject, bool>? filtro = null)
    {
        var udis = DatosDe(antesDe, filtro).Select(d => d["udi"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var indice = Layout.Select((s, i) => (s, i)).FirstOrDefault(x => UdisDe(x.s!).Any(udis.Contains), (null, -1)).Item2;
        if (indice < 0) { return Sumar(nuevo); }
        return Pegar(nuevo, alPrincipio: false, en: indice);
    }

    /// <summary>
    /// Fija <paramref name="propiedad"/> = <paramref name="valor"/> en cada bloque del tipo dado. Devuelve cuántos
    /// cambiaron (los que ya tenían ese valor no cuentan).
    /// </summary>
    public int Reemplazar(Guid tipo, string propiedad, JsonNode valor, Func<JsonObject, bool>? filtro = null)
    {
        var n = 0;
        foreach (var dato in DatosDe(tipo, filtro))
        {
            if (JsonNode.DeepEquals(dato[propiedad], valor)) { continue; }
            dato[propiedad] = valor.DeepClone();
            n++;
        }
        if (n > 0) { Cambios++; }
        return n;
    }

    /// <summary>
    /// Transforma una propiedad que guarda una LISTA en JSON (como texto o como arreglo) en cada bloque del tipo dado.
    /// La forma de guardarla se respeta. Devuelve cuántos bloques cambiaron.
    /// </summary>
    public int TransformarLista(Guid tipo, string propiedad, Func<JsonArray, JsonArray> transformar, Func<JsonObject, bool>? filtro = null)
    {
        var n = 0;
        foreach (var dato in DatosDe(tipo, filtro))
        {
            var actual = dato[propiedad];
            var comoTexto = actual is JsonValue v && v.TryGetValue<string>(out _);
            var lista = comoTexto
                ? JsonNode.Parse(actual!.GetValue<string>()) as JsonArray
                : actual?.DeepClone() as JsonArray;
            if (lista is null) { continue; }
            var nueva = transformar(lista);
            JsonNode valor = comoTexto ? JsonValue.Create(nueva.ToJsonString(Legible))! : nueva;
            if (JsonNode.DeepEquals(actual, valor)) { continue; }
            dato[propiedad] = valor;
            n++;
        }
        if (n > 0) { Cambios++; }
        return n;
    }

    /// <summary>
    /// Cambia el rótulo y el enlace de los botones de una lista anidada (<c>ctaItems</c> de una portada), en orden y en
    /// su sitio: los bloques de la lista conservan su UDI, así que repetirlo no cambia nada. Un botón de más se ignora.
    /// </summary>
    public int CambiarBotones(Guid tipo, string propiedad, IReadOnlyList<(string Rotulo, string Enlace)> botones,
        Func<JsonObject, bool>? filtro = null)
    {
        var n = 0;
        foreach (var dato in DatosDe(tipo, filtro))
        {
            var actual = dato[propiedad];
            var comoTexto = actual is JsonValue v && v.TryGetValue<string>(out _);
            var lista = (comoTexto ? JsonNode.Parse(actual!.GetValue<string>()) : actual?.DeepClone()) as JsonObject;
            if (lista?["contentData"] is not JsonArray datos) { continue; }
            for (var i = 0; i < Math.Min(datos.Count, botones.Count); i++)
            {
                var boton = (JsonObject)datos[i]!;
                boton["ctaLabel"] = botones[i].Rotulo;
                // El enlace llega como el JSON de un selector de enlaces; se guarda como lo guarda el bloque.
                boton["ctaLink"] = boton["ctaLink"] is JsonArray ? JsonNode.Parse(botones[i].Enlace) : botones[i].Enlace;
            }
            JsonNode valor = comoTexto ? JsonValue.Create(lista.ToJsonString(Legible))! : lista;
            if (JsonNode.DeepEquals(actual, valor)) { continue; }
            dato[propiedad] = valor;
            n++;
        }
        if (n > 0) { Cambios++; }
        return n;
    }

    /// <summary>
    /// Transforma una propiedad de TEXTO (un HTML, un rótulo) en cada bloque del tipo dado. Devuelve cuántos cambiaron;
    /// si la función devuelve lo mismo, no cuenta.
    /// </summary>
    public int TransformarTexto(Guid tipo, string propiedad, Func<string, string> transformar, Func<JsonObject, bool>? filtro = null)
    {
        var n = 0;
        foreach (var dato in DatosDe(tipo, filtro))
        {
            if (dato[propiedad] is not JsonValue v || !v.TryGetValue<string>(out var actual)) { continue; }
            var nuevo = transformar(actual);
            if (string.Equals(nuevo, actual, StringComparison.Ordinal)) { continue; }
            dato[propiedad] = nuevo;
            n++;
        }
        if (n > 0) { Cambios++; }
        return n;
    }

    /// <summary>Un filtro para las operaciones: los bloques cuya <paramref name="propiedad"/> vale <paramref name="valor"/>.</summary>
    public static Func<JsonObject, bool> Donde(string propiedad, string valor) =>
        d => d[propiedad] is JsonValue v && v.TryGetValue<string>(out var texto) && string.Equals(texto, valor, StringComparison.Ordinal);

    /// <summary>Lo mismo con un texto.</summary>
    public int Reemplazar(Guid tipo, string propiedad, string valor, Func<JsonObject, bool>? filtro = null)
        => Reemplazar(tipo, propiedad, JsonValue.Create(valor)!, filtro);

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
    public int DesconectarSeccionesCon(Guid tipo, Func<JsonObject, bool>? filtro = null)
    {
        var udis = DatosDe(tipo, filtro).Select(d => d["udi"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
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

    private IEnumerable<JsonObject> DatosDe(Guid tipo, Func<JsonObject, bool>? filtro = null)
    {
        var clave = tipo.ToString("D");
        return Lista("contentData").OfType<JsonObject>()
            .Where(d => string.Equals(d["contentTypeKey"]?.GetValue<string>(), clave, StringComparison.OrdinalIgnoreCase))
            .Where(d => filtro is null || filtro(d))
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
