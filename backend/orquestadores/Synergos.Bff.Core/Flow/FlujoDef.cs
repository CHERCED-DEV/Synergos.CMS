using System.Text.Json;

namespace Synergos.Bff.Core.Flow;

/// <summary>
/// Un flujo de negocio DECLARADO: qué entra, en qué fases se parte y qué pasos da cada una (ADR 0140).
/// </summary>
/// <param name="Clave">Cómo se llama el flujo (<c>eventos.compra</c>). Es también el <c>Kind</c> del
/// <see cref="Synergos.Core.Ref"/> con el que sus pasos le dicen a una capacidad para qué reservan.</param>
/// <param name="Entrada">Lo que el llamador pone en el contexto antes del primer paso.</param>
/// <param name="Fases">En el orden del documento: la primera abre la saga y las demás la continúan.</param>
/// <param name="Pasos">Cada paso por su identificador dentro del flujo.</param>
/// <remarks>
/// <para><b>Por qué un dato y no otro <c>*Flow.cs</c>.</b> El orden de los pasos y la reserva en dos
/// tiempos —anotar cómo se deshace al apartar, cambiarle el carácter al consumir— se escribían a
/// mano en cada orquestador, intercalados en un <c>Select</c>. Declarados, los ejecuta UN intérprete
/// (<see cref="FlowRunner{TSaga}"/>) y el orquestador sólo aporta lo que es suyo: los pasos del
/// dominio y cómo se guarda su saga.</para>
///
/// <para><b>Sin lenguaje de expresiones, a propósito.</b> Un paso lee por NOMBRE lo que otro escribió
/// y nada más: ni plantillas, ni condiciones, ni aritmética. Lo que haría falta calcular es un paso
/// del dominio en C#. Si portar un flujo exigiera expresiones, la definición se estaría volviendo
/// un lenguaje de programación, y eso es el criterio de reapertura de la ADR, no algo que se añade
/// en silencio.</para>
/// </remarks>
public sealed record FlujoDef(
    string Clave,
    IReadOnlyList<string> Entrada,
    IReadOnlyList<FaseDef> Fases,
    IReadOnlyDictionary<string, PasoDef> Pasos)
{
    /// <summary>La fase con ese nombre, o rechaza: una fase que no existe es un defecto del llamador.</summary>
    public FaseDef Fase(string nombre)
        => Fases.FirstOrDefault(f => string.Equals(f.Nombre, nombre, StringComparison.Ordinal))
           ?? throw new InvalidOperationException($"El flujo «{Clave}» no tiene la fase «{nombre}».");

    /// <summary>Lee una definición. Rechaza todo campo que el intérprete no conozca.</summary>
    /// <exception cref="FormatException">Si no es JSON, o si su forma no es la de un flujo.</exception>
    public static FlujoDef Leer(string json) => LectorDeFlujo.Leer(json);
}

/// <summary>Una fase: los pasos que se dan seguidos, en ese orden.</summary>
public sealed record FaseDef(string Nombre, IReadOnlyList<PasoRef> Pasos);

/// <summary>Un elemento de una fase: un paso suelto o un bloque que se repite por ítem.</summary>
/// <param name="Paso">El identificador del paso, si es suelto.</param>
/// <param name="ParaCada">El bloque, si se repite.</param>
public sealed record PasoRef(string? Paso, ParaCadaDef? ParaCada);

/// <summary>
/// Pasos que se repiten, en orden, por cada ítem de <paramref name="Fuente"/>.
/// </summary>
/// <param name="Fuente">Una entrada del flujo que es una lista de ítems, o
/// <c>reservas:&lt;paso&gt;</c>: los apartados que ese paso dejó en la saga.</param>
/// <param name="Como">El nombre del ítem dentro del bloque: <c>linea</c> se lee entero y
/// <c>linea.itemId</c> es un campo suyo.</param>
/// <param name="Pasos">Los pasos del bloque.</param>
/// <remarks>
/// Existe porque apartar aforo es una línea a la vez, y cada apartado se anota compensable en el
/// instante en que existe: si la tercera línea falla, las dos primeras ya tienen quién las suelte.
/// </remarks>
public sealed record ParaCadaDef(string Fuente, string Como, IReadOnlyList<string> Pasos);

/// <summary>Un paso del flujo: qué tipo de paso es, qué lee, qué escribe y qué reserva.</summary>
/// <param name="Id">Su identificador dentro del flujo.</param>
/// <param name="Tipo">El paso registrado que lo ejecuta (<c>payments.autorizar</c>,
/// <c>eventos.revisar-lineas</c>).</param>
/// <param name="Lee">Los nombres que lee, en el orden en que el tipo los pide.</param>
/// <param name="Escribe">Los nombres donde deja lo que produce, en el orden en que lo produce.</param>
/// <param name="Llave">El sufijo FIJO de su llave de idempotencia: <c>{sagaId}|authorize</c>.</param>
/// <param name="LlaveBase">La base de una llave POR ÍTEM: el paso le añade el ítem en C#
/// (<c>{sagaId}|hold:{itemId}</c>). Es la alternativa a una plantilla en el dato.</param>
/// <param name="Reserva">Si el paso reserva algo que después se consuma o se deshace.</param>
/// <param name="CierraReserva">El paso cuya reserva éste consuma.</param>
/// <param name="Motivo">Con qué motivo se deshace la saga si este paso falla.</param>
public sealed record PasoDef(
    string Id,
    string Tipo,
    IReadOnlyList<string> Lee,
    IReadOnlyList<string> Escribe,
    string? Llave,
    string? LlaveBase,
    ReservaDef? Reserva,
    string? CierraReserva,
    string? Motivo);

/// <summary>
/// La reserva en dos tiempos: con qué paso se consuma y cómo se deshace antes y después.
/// </summary>
/// <param name="ConsumadoPor">El paso que la consuma, en una fase posterior.</param>
/// <param name="Antes">El <c>Kind</c> de la compensación mientras no se consumió (soltar, anular).</param>
/// <param name="Despues">El <c>Kind</c> una vez consumida (reponer, devolver). Sin él, consumirla deja
/// la compensación hecha: no queda nada que deshacer.</param>
/// <param name="Motivo">El motivo con el que se anota la compensación.</param>
/// <remarks>
/// <b>Es el «cambio de carácter» como forma de primera clase.</b> Liberar una autorización ya
/// capturada lo rechaza <c>Api.Payments</c>, y soltar un apartado ya consumido lo rechaza
/// <c>Api.Inventory</c>: sin reescribir la compensación al consumir, quedaría colgada para siempre
/// por una razón que no tiene nada que ver con el mundo real. Hoy cada <c>*Flow.cs</c> la reescribe
/// a mano; declarada, la reescribe el intérprete.
/// </remarks>
public sealed record ReservaDef(string ConsumadoPor, string Antes, string? Despues, string? Motivo);

/// <summary>Lee el JSON de un flujo con la forma exacta que el intérprete ejecuta.</summary>
/// <remarks>
/// <b>Estricto con los campos que no conoce.</b> Un campo que el intérprete no lee es una regla que
/// nadie cumple: quien escribe <c>"al_fallar"</c> creería haber declarado algo, y la primera compra
/// le enseñaría que no. Mejor que no arranque.
/// </remarks>
internal static class LectorDeFlujo
{
    public static FlujoDef Leer(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"La definición del flujo no es JSON válido: {ex.Message}", ex);
        }

        using (doc)
        {
            var raiz = doc.RootElement;
            Objeto(raiz, "el flujo");
            SoloEstos(raiz, "el flujo", "clave", "entrada", "fases", "pasos");

            var clave = Texto(raiz, "clave", "el flujo");
            var donde = $"el flujo «{clave}»";

            var entrada = Textos(raiz, "entrada", donde);

            var pasos = new Dictionary<string, PasoDef>(StringComparer.Ordinal);
            var nodoPasos = Requerido(raiz, "pasos", donde);
            Objeto(nodoPasos, $"{donde}, «pasos»");
            foreach (var p in nodoPasos.EnumerateObject())
            {
                pasos[p.Name] = Paso(p.Name, p.Value, donde);
            }

            var fases = new List<FaseDef>();
            var nodoFases = Requerido(raiz, "fases", donde);
            Objeto(nodoFases, $"{donde}, «fases»");
            foreach (var f in nodoFases.EnumerateObject())
            {
                fases.Add(Fase(f.Name, f.Value, donde));
            }

            return new FlujoDef(clave, entrada, fases, pasos);
        }
    }

    private static FaseDef Fase(string nombre, JsonElement nodo, string donde)
    {
        var aqui = $"{donde}, fase «{nombre}»";
        if (nodo.ValueKind != JsonValueKind.Array) throw new FormatException($"{aqui}: tiene que ser una lista de pasos.");

        var refs = new List<PasoRef>();
        foreach (var e in nodo.EnumerateArray())
        {
            if (e.ValueKind == JsonValueKind.String)
            {
                refs.Add(new PasoRef(e.GetString()!, null));
                continue;
            }

            Objeto(e, aqui);
            SoloEstos(e, $"{aqui}, bloque", "para_cada", "como", "pasos");
            refs.Add(new PasoRef(null, new ParaCadaDef(
                Texto(e, "para_cada", aqui), Texto(e, "como", aqui), Textos(e, "pasos", aqui))));
        }

        return new FaseDef(nombre, refs);
    }

    private static PasoDef Paso(string id, JsonElement nodo, string donde)
    {
        var aqui = $"{donde}, paso «{id}»";
        Objeto(nodo, aqui);
        SoloEstos(nodo, aqui,
            "tipo", "lee", "escribe", "llave", "llave_base", "reserva", "cierra_reserva", "motivo");

        ReservaDef? reserva = null;
        if (nodo.TryGetProperty("reserva", out var r))
        {
            var deLaReserva = $"{aqui}, «reserva»";
            Objeto(r, deLaReserva);
            SoloEstos(r, deLaReserva, "consumado_por", "antes", "despues", "motivo");
            reserva = new ReservaDef(
                Texto(r, "consumado_por", deLaReserva), Texto(r, "antes", deLaReserva),
                Opcional(r, "despues", deLaReserva), Opcional(r, "motivo", deLaReserva));
        }

        return new PasoDef(
            id,
            Texto(nodo, "tipo", aqui),
            nodo.TryGetProperty("lee", out _) ? Textos(nodo, "lee", aqui) : Array.Empty<string>(),
            nodo.TryGetProperty("escribe", out _) ? Textos(nodo, "escribe", aqui) : Array.Empty<string>(),
            Opcional(nodo, "llave", aqui),
            Opcional(nodo, "llave_base", aqui),
            reserva,
            Opcional(nodo, "cierra_reserva", aqui),
            Opcional(nodo, "motivo", aqui));
    }

    private static void Objeto(JsonElement nodo, string donde)
    {
        if (nodo.ValueKind != JsonValueKind.Object) throw new FormatException($"{donde}: tiene que ser un objeto.");
    }

    private static void SoloEstos(JsonElement nodo, string donde, params string[] conocidos)
    {
        foreach (var p in nodo.EnumerateObject())
        {
            if (!conocidos.Contains(p.Name, StringComparer.Ordinal))
            {
                throw new FormatException(
                    $"{donde}: trae «{p.Name}», que el intérprete no conoce. Los que conoce: {string.Join(", ", conocidos)}.");
            }
        }
    }

    private static JsonElement Requerido(JsonElement nodo, string campo, string donde)
        => nodo.TryGetProperty(campo, out var v) ? v : throw new FormatException($"{donde}: falta «{campo}».");

    private static string Texto(JsonElement nodo, string campo, string donde)
    {
        var v = Requerido(nodo, campo, donde);
        return v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!
            : throw new FormatException($"{donde}: «{campo}» tiene que ser un texto no vacío.");
    }

    private static string? Opcional(JsonElement nodo, string campo, string donde)
        => nodo.TryGetProperty(campo, out _) ? Texto(nodo, campo, donde) : null;

    private static IReadOnlyList<string> Textos(JsonElement nodo, string campo, string donde)
    {
        var v = Requerido(nodo, campo, donde);
        if (v.ValueKind != JsonValueKind.Array) throw new FormatException($"{donde}: «{campo}» tiene que ser una lista.");

        return v.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString())
                ? e.GetString()!
                : throw new FormatException($"{donde}: «{campo}» sólo admite textos no vacíos."))
            .ToList();
    }
}
