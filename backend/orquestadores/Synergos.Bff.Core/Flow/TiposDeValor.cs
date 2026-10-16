using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Synergos.Core;

namespace Synergos.Bff.Core.Flow;

/// <summary>
/// Un tipo de valor que un flujo puede declarar: cómo se lee de JSON, cómo se escribe y qué esquema
/// publica (núcleo N1).
/// </summary>
/// <remarks>
/// <para><b>Es lo que deja a un flujo sin fachada propia.</b> El host convierte el cuerpo y las
/// cabeceras de la puerta en los valores que leen los pasos con estos tipos, guarda en la saga genérica
/// los que una fase posterior necesita, y publica el contrato con el mismo esquema. Una sola fuente
/// para las tres cosas: no hay un record de petición que pueda separarse del JSON.</para>
///
/// <para><b>Leer rechaza, no adivina.</b> Un valor con otra forma es un <see cref="FormatException"/>
/// con el nombre del campo, que el host contesta como <c>400</c>: un default sería el defecto que este
/// repo ya pagó con <c>System.Text.Json</c>.</para>
/// </remarks>
public interface ITipoDeValor
{
    /// <summary>El nombre con el que lo declara una definición: <c>dinero</c>.</summary>
    string Nombre { get; }

    /// <summary>El tipo .NET del valor en el contexto del flujo (sin anulable).</summary>
    Type Clr { get; }

    /// <summary>Si la saga genérica puede guardarlo. Una lista no; un contacto (dato personal) tampoco.</summary>
    bool Guardable { get; }

    /// <summary>El valor desde JSON.</summary>
    /// <param name="valor">El nodo; nunca <c>null</c> de JSON (eso lo resuelve el anulable).</param>
    /// <param name="donde">Cómo nombrarlo en el error.</param>
    /// <exception cref="FormatException">Si no tiene la forma del tipo.</exception>
    object Leer(JsonElement valor, string donde);

    /// <summary>El valor como JSON, con la forma que <see cref="Leer"/> acepta.</summary>
    JsonNode Escribir(object valor);

    /// <summary>El esquema JSON (OpenAPI 3.1) del valor, sin anulable.</summary>
    JsonObject Esquema();
}

/// <summary>Los tipos de valor que conoce un orquestador, por nombre.</summary>
/// <remarks>
/// <para><b>Ampliable a propósito</b>: <c>Bff.Core</c> trae los que no nombran ninguna capacidad
/// (<see cref="Comunes"/>); <c>Bff.Pasos</c> añade el contacto del aviso, que es suyo.</para>
///
/// <para><b>El sufijo <c>?</c> hace anulable a cualquiera</b>: <c>texto?</c> acepta <c>null</c> o la
/// ausencia. Un <c>texto</c> sin sufijo no acepta ni nulo ni en blanco; un <c>texto?</c> en blanco se
/// lee como nulo (un espacio pegado de un correo no es un código).</para>
/// </remarks>
public sealed class TiposDeValor
{
    private readonly Dictionary<string, ITipoDeValor> _tipos = new(StringComparer.Ordinal);

    /// <summary>Un registro con <paramref name="tipos"/>.</summary>
    /// <exception cref="InvalidOperationException">Si dos dicen llamarse igual.</exception>
    public TiposDeValor(IEnumerable<ITipoDeValor> tipos)
    {
        ArgumentNullException.ThrowIfNull(tipos);
        foreach (var t in tipos)
        {
            if (!_tipos.TryAdd(t.Nombre, t)) throw new InvalidOperationException($"Hay dos tipos de valor llamados «{t.Nombre}».");
        }
    }

    /// <summary>Los tipos que no nombran ninguna capacidad.</summary>
    public static IReadOnlyList<ITipoDeValor> Comunes { get; } =
    [
        new Texto(), new Entero(), new NumeroDecimal(), new Booleano(), new Instante(), new TipoRef(), new Dinero(), new Ventana(),
        new Fecha(),
    ];

    /// <summary>Un registro con los comunes y <paramref name="mas"/>.</summary>
    public static TiposDeValor Con(params ITipoDeValor[] mas) => new(Comunes.Concat(mas));

    /// <summary>Los nombres registrados.</summary>
    public IReadOnlyCollection<string> Nombres => _tipos.Keys;

    /// <summary>El tipo registrado cuyo valor es de <paramref name="clr"/>; <c>null</c> si ninguno.</summary>
    /// <remarks>Es como se sabe el tipo de lo que escribe un paso (<see cref="IPaso.TiposDeEscritura"/>).</remarks>
    public ITipoDeValor? PorClr(Type clr) => _tipos.Values.FirstOrDefault(t => t.Clr == clr);

    /// <summary>El tipo de un nombre declarado (<c>dinero?</c>), o <c>null</c> si nadie lo registró.</summary>
    /// <param name="declarado">El nombre, con <c>?</c> si es anulable.</param>
    /// <param name="clase">Si es una referencia del cuerpo, la clase que admite (<see cref="TipoDeCampo.Clase"/>).</param>
    public TipoResuelto? Resolver(string declarado, string? clase = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(declarado);
        var anulable = declarado.EndsWith('?');
        var nombre = anulable ? declarado[..^1] : declarado;
        return _tipos.TryGetValue(nombre, out var tipo) ? new TipoResuelto(tipo, anulable, clase) : null;
    }

    /// <summary>
    /// Una lista de ítems desde JSON: cada ítem es un objeto con exactamente <paramref name="campos"/>
    /// (los anulables pueden faltar), y queda como el <see cref="FlowContext"/> que lee un bloque.
    /// </summary>
    /// <exception cref="FormatException">Si no es una lista de objetos con esos campos y sus tipos.</exception>
    /// <exception cref="InvalidOperationException">Si un campo declara un tipo que nadie registró: el
    /// validador lo habría impedido al arrancar.</exception>
    public IReadOnlyList<FlowContext> LeerLista(JsonElement valor, IReadOnlyDictionary<string, TipoDeCampo> campos, string donde)
    {
        ArgumentNullException.ThrowIfNull(campos);
        if (valor.ValueKind != JsonValueKind.Array) throw Json.Mal(donde, "una lista");

        var items = new List<FlowContext>();
        var i = 0;
        foreach (var item in valor.EnumerateArray())
        {
            var aqui = $"{donde}[{i++}]";
            if (item.ValueKind != JsonValueKind.Object) throw Json.Mal(aqui, "un objeto");
            Json.SoloEstas(item, aqui, campos.Keys.ToArray());

            var ctx = new FlowContext();
            foreach (var (campo, declarado) in campos)
            {
                ctx.Set(campo, Requerir(declarado).Leer(item.TryGetProperty(campo, out var v) ? v : (JsonElement?)null, $"{aqui}.{campo}"));
            }
            items.Add(ctx);
        }
        return items;
    }

    /// <summary>El esquema de una lista de ítems con <paramref name="campos"/>.</summary>
    public JsonObject EsquemaDeLista(IReadOnlyDictionary<string, TipoDeCampo> campos)
    {
        ArgumentNullException.ThrowIfNull(campos);
        var props = new JsonObject();
        foreach (var (campo, declarado) in campos) props[campo] = Requerir(declarado).Esquema();

        return new JsonObject
        {
            ["type"] = "array",
            ["items"] = new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray(campos.Where(c => !Requerir(c.Value).Anulable)
                    .Select(c => (JsonNode)JsonValue.Create(c.Key)!).ToArray()),
                ["properties"] = props,
            },
        };
    }

    private TipoResuelto Requerir(TipoDeCampo declarado)
        => Resolver(declarado.Tipo, declarado.Clase)
           ?? throw new InvalidOperationException($"Nadie registró el tipo de valor «{declarado.Tipo}».");
}

/// <summary>Un tipo con su anulabilidad: lo que una definición declara para un valor.</summary>
/// <param name="Tipo">El tipo de valor.</param>
/// <param name="Anulable">Si admite <c>null</c> o la ausencia.</param>
/// <param name="Clase">Si es una referencia con clase declarada, la única que lee (<see cref="TipoDeCampo.Clase"/>).</param>
public sealed record TipoResuelto(ITipoDeValor Tipo, bool Anulable, string? Clase = null)
{
    /// <summary>El nombre como lo declara una definición.</summary>
    /// <remarks>Sin la clase: es también como se guarda el valor en la saga, y así se vuelve a resolver.</remarks>
    public string Declarado => Anulable ? Tipo.Nombre + "?" : Tipo.Nombre;

    /// <summary>El valor desde JSON; <c>null</c> (o ausente) sólo si es anulable.</summary>
    /// <exception cref="FormatException">Si falta, es nulo sin ser anulable, no tiene la forma, o es una
    /// referencia de otra clase que la declarada.</exception>
    public object? Leer(JsonElement? valor, string donde)
    {
        if (valor is not { } v || v.ValueKind == JsonValueKind.Null)
        {
            return Anulable ? null : throw new FormatException($"{donde}: hace falta, y es «{Tipo.Nombre}».");
        }

        var leido = Tipo.Leer(v, donde);
        if (Clase is not null && leido is Ref r && !string.Equals(r.Kind, Clase, StringComparison.Ordinal))
        {
            throw new FormatException($"{donde}.kind: tiene que ser «{Clase}».");
        }
        return Anulable && leido is string s && string.IsNullOrWhiteSpace(s) ? null : leido;
    }

    /// <summary>El valor como JSON; <c>null</c> para el nulo.</summary>
    public JsonNode? Escribir(object? valor) => valor is null ? null : Tipo.Escribir(valor);

    /// <summary>El esquema, con <c>null</c> admitido si es anulable y la clase como único <c>kind</c> si la declara.</summary>
    public JsonObject Esquema()
    {
        var esquema = Tipo.Esquema();
        if (Clase is not null && esquema["properties"]?["kind"] is JsonObject kind) kind["enum"] = new JsonArray(Clase);
        if (!Anulable) return esquema;

        // Un escalar anulable lleva "null" en su `type`; un objeto, un oneOf: es lo que el suelo del
        // contrato reconoce como anulable (SueloDelContratoTests.Anulable).
        if (esquema["type"] is JsonValue t && (string?)t is { } nombre && nombre != "object")
        {
            esquema["type"] = new JsonArray(nombre, "null");
            return esquema;
        }
        return new JsonObject { ["oneOf"] = new JsonArray(esquema, new JsonObject { ["type"] = "null" }) };
    }
}

// ── Los comunes ─────────────────────────────────────────────────────────────

internal static class Json
{
    public static FormatException Mal(string donde, string esperado)
        => new($"{donde}: tiene que ser {esperado}.");

    public static JsonElement Propiedad(JsonElement objeto, string nombre, string donde)
        => objeto.TryGetProperty(nombre, out var p) ? p : throw new FormatException($"{donde}: falta «{nombre}».");

    public static void SoloEstas(JsonElement objeto, string donde, params string[] conocidas)
    {
        foreach (var p in objeto.EnumerateObject())
        {
            if (!conocidas.Contains(p.Name, StringComparer.Ordinal))
            {
                throw new FormatException($"{donde}: trae «{p.Name}», y sólo admite {string.Join(", ", conocidas)}.");
            }
        }
    }

    public static string TextoNoVacio(JsonElement v, string donde)
        => v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!
            : throw Mal(donde, "un texto no vacío");

    public static JsonObject Objeto(params (string Nombre, JsonObject Esquema)[] propiedades)
    {
        var props = new JsonObject();
        foreach (var (n, e) in propiedades) props[n] = e;
        return new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray(propiedades.Select(p => (JsonNode)JsonValue.Create(p.Nombre)!).ToArray()),
            ["properties"] = props,
        };
    }
}

/// <summary><c>texto</c>: un texto no vacío.</summary>
public sealed class Texto : ITipoDeValor
{
    public string Nombre => "texto";
    public Type Clr => typeof(string);
    public bool Guardable => true;

    public object Leer(JsonElement valor, string donde)
        => valor.ValueKind == JsonValueKind.String ? valor.GetString()! : throw Json.Mal(donde, "un texto");

    public JsonNode Escribir(object valor) => JsonValue.Create((string)valor)!;

    public JsonObject Esquema() => new() { ["type"] = "string" };
}

/// <summary><c>entero</c>: un número entero de 32 bits.</summary>
public sealed class Entero : ITipoDeValor
{
    public string Nombre => "entero";
    public Type Clr => typeof(int);
    public bool Guardable => true;

    public object Leer(JsonElement valor, string donde)
        => valor.ValueKind == JsonValueKind.Number && valor.TryGetInt32(out var n) ? n : throw Json.Mal(donde, "un entero");

    public JsonNode Escribir(object valor) => JsonValue.Create((int)valor)!;

    public JsonObject Esquema() => new() { ["type"] = "integer", ["format"] = "int32" };
}

/// <summary><c>decimal</c>: un número con decimales exactos (el dinero sin moneda, un porcentaje).</summary>
/// <remarks>Un número JSON, nunca un texto: «500.000» en es-CO es quinientos mil y en JSON, quinientos.</remarks>
public sealed class NumeroDecimal : ITipoDeValor
{
    public string Nombre => "decimal";
    public Type Clr => typeof(decimal);
    public bool Guardable => true;

    public object Leer(JsonElement valor, string donde)
        => valor.ValueKind == JsonValueKind.Number && valor.TryGetDecimal(out var d) ? d : throw Json.Mal(donde, "un número");

    public JsonNode Escribir(object valor) => JsonValue.Create((decimal)valor)!;

    public JsonObject Esquema() => new() { ["type"] = "number", ["format"] = "decimal" };
}

/// <summary><c>booleano</c>.</summary>
public sealed class Booleano : ITipoDeValor
{
    public string Nombre => "booleano";
    public Type Clr => typeof(bool);
    public bool Guardable => true;

    public object Leer(JsonElement valor, string donde)
        => valor.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw Json.Mal(donde, "true o false"),
        };

    public JsonNode Escribir(object valor) => JsonValue.Create((bool)valor)!;

    public JsonObject Esquema() => new() { ["type"] = "boolean" };
}

/// <summary><c>instante</c>: ISO-8601 CON su desfase (<c>Z</c> o <c>±hh:mm</c>).</summary>
/// <remarks>
/// Sin desfase, el instante dependería del huso del servidor: una agenda en otro huso apartaría otra
/// hora sin decirlo. Se rechaza en vez de suponer.
/// </remarks>
public sealed partial class Instante : ITipoDeValor
{
    public string Nombre => "instante";
    public Type Clr => typeof(DateTimeOffset);
    public bool Guardable => true;

    [GeneratedRegex(@"(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex ConDesfase();

    public object Leer(JsonElement valor, string donde)
    {
        if (valor.ValueKind == JsonValueKind.String && valor.GetString() is { } texto && ConDesfase().IsMatch(texto)
            && DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var instante))
        {
            return instante;
        }
        throw Json.Mal(donde, "un instante ISO-8601 con su desfase (Z o ±hh:mm)");
    }

    public JsonNode Escribir(object valor) => JsonValue.Create(((DateTimeOffset)valor).ToString("O", CultureInfo.InvariantCulture))!;

    public JsonObject Esquema() => new() { ["type"] = "string", ["format"] = "date-time" };
}

/// <summary><c>fecha</c>: un día del calendario, <c>AAAA-MM-DD</c> (núcleo N3, NE-10 de 007).</summary>
/// <remarks>
/// Un día y no un instante: la fecha de un evento o de una oferta no tiene hora ni huso, y leerla como
/// instante la correría de día según el servidor. Sólo la forma ISO exacta: «15/10/2026» se lee distinto en
/// cada cultura, y una fecha con hora es otro tipo.
/// </remarks>
public sealed class Fecha : ITipoDeValor
{
    public string Nombre => "fecha";
    public Type Clr => typeof(DateOnly);
    public bool Guardable => true;

    public object Leer(JsonElement valor, string donde)
    {
        if (valor.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(valor.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha))
        {
            return fecha;
        }
        throw Json.Mal(donde, "una fecha AAAA-MM-DD");
    }

    public JsonNode Escribir(object valor) => JsonValue.Create(((DateOnly)valor).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))!;

    public JsonObject Esquema() => new() { ["type"] = "string", ["format"] = "date" };
}

/// <summary><c>ref</c>: <c>{ kind, id }</c>, las dos partes no vacías.</summary>
/// <remarks>Qué <c>kind</c> admite lo dice la definición (<see cref="TipoResuelto.Clase"/>), no este tipo.</remarks>
public sealed class TipoRef : ITipoDeValor
{
    public string Nombre => "ref";
    public Type Clr => typeof(Ref);
    public bool Guardable => true;

    public object Leer(JsonElement valor, string donde)
    {
        if (valor.ValueKind != JsonValueKind.Object) throw Json.Mal(donde, "un objeto { kind, id }");
        Json.SoloEstas(valor, donde, "kind", "id");
        return Ref.Create(
            Json.TextoNoVacio(Json.Propiedad(valor, "kind", donde), $"{donde}.kind"),
            Json.TextoNoVacio(Json.Propiedad(valor, "id", donde), $"{donde}.id"));
    }

    public JsonNode Escribir(object valor)
    {
        var r = (Ref)valor;
        return new JsonObject { ["kind"] = r.Kind, ["id"] = r.Id };
    }

    public JsonObject Esquema()
        => Json.Objeto(("kind", new JsonObject { ["type"] = "string" }), ("id", new JsonObject { ["type"] = "string" }));
}

/// <summary><c>dinero</c>: <c>{ amount, currency }</c>, la moneda ISO 4217.</summary>
public sealed class Dinero : ITipoDeValor
{
    public string Nombre => "dinero";
    public Type Clr => typeof(Money);
    public bool Guardable => true;

    public object Leer(JsonElement valor, string donde)
    {
        if (valor.ValueKind != JsonValueKind.Object) throw Json.Mal(donde, "un objeto { amount, currency }");
        Json.SoloEstas(valor, donde, "amount", "currency");
        var monto = (decimal)new NumeroDecimal().Leer(Json.Propiedad(valor, "amount", donde), $"{donde}.amount");
        var moneda = Json.TextoNoVacio(Json.Propiedad(valor, "currency", donde), $"{donde}.currency");
        if (moneda.Trim().Length != 3) throw Json.Mal($"{donde}.currency", "un código ISO 4217 de tres letras");
        return Money.Of(monto, moneda);
    }

    public JsonNode Escribir(object valor)
    {
        var m = (Money)valor;
        return new JsonObject { ["amount"] = m.Amount, ["currency"] = m.Currency };
    }

    public JsonObject Esquema()
        => Json.Objeto(
            ("amount", new JsonObject { ["type"] = "number", ["format"] = "decimal" }),
            ("currency", new JsonObject { ["type"] = "string" }));
}

/// <summary><c>ventana</c>: <c>{ start, end }</c>, dos instantes con desfase y el fin después del inicio.</summary>
public sealed class Ventana : ITipoDeValor
{
    public string Nombre => "ventana";
    public Type Clr => typeof(TimeWindow);
    public bool Guardable => true;

    public object Leer(JsonElement valor, string donde)
    {
        if (valor.ValueKind != JsonValueKind.Object) throw Json.Mal(donde, "un objeto { start, end }");
        Json.SoloEstas(valor, donde, "start", "end");
        var instante = new Instante();
        var inicio = (DateTimeOffset)instante.Leer(Json.Propiedad(valor, "start", donde), $"{donde}.start");
        var fin = (DateTimeOffset)instante.Leer(Json.Propiedad(valor, "end", donde), $"{donde}.end");
        if (fin <= inicio) throw Json.Mal(donde, "una ventana cuyo fin va después de su inicio");
        return TimeWindow.Of(inicio, fin);
    }

    public JsonNode Escribir(object valor)
    {
        var v = (TimeWindow)valor;
        var instante = new Instante();
        return new JsonObject { ["start"] = instante.Escribir(v.Start), ["end"] = instante.Escribir(v.End) };
    }

    public JsonObject Esquema()
        => Json.Objeto(("start", new Instante().Esquema()), ("end", new Instante().Esquema()));
}
