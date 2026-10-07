namespace Synergos.Bff.Core.Flow;

/// <summary>
/// Lo que los pasos de un flujo se pasan entre sí, por nombre, durante UNA llamada.
/// </summary>
/// <remarks>
/// <para><b>Es transitorio y no se guarda nunca</b>, y eso es lo que deja intacto lo persistido: lo
/// que sobrevive entre fases es la saga del dominio, con sus campos de siempre. La fase que
/// continúa una saga reconstruye lo que necesita con <see cref="IFlowBinding{TSaga}.Leer"/>. Un
/// contexto guardado sería un segundo formato en disco, y la vuelta atrás a un binario anterior
/// dejaría de leer las sagas en vuelo.</para>
///
/// <para><b>Un valor que falta o tiene otro tipo RECHAZA</b>, con el nombre en el mensaje. Devolver
/// un default sería el defecto que este repo ya pagó con <c>System.Text.Json</c>: el paso seguiría
/// con un cero y nadie sabría de dónde salió.</para>
/// </remarks>
public sealed class FlowContext
{
    private readonly Dictionary<string, object?> _valores = new(StringComparer.Ordinal);

    /// <summary>Los nombres que tiene.</summary>
    public IReadOnlyCollection<string> Claves => _valores.Keys;

    /// <summary>Si tiene un valor con ese nombre, aunque sea <c>null</c>.</summary>
    public bool Has(string clave) => _valores.ContainsKey(clave);

    /// <summary>Pone (o reemplaza) un valor. Devuelve el mismo contexto, para encadenar.</summary>
    public FlowContext Set(string clave, object? valor)
    {
        _valores[clave] = valor;
        return this;
    }

    /// <summary>Copia encima todos los valores de <paramref name="otro"/>.</summary>
    public FlowContext CopiarDe(FlowContext otro)
    {
        ArgumentNullException.ThrowIfNull(otro);
        foreach (var (clave, valor) in otro._valores) _valores[clave] = valor;
        return this;
    }

    /// <summary>El valor con ese nombre, del tipo pedido.</summary>
    /// <exception cref="InvalidOperationException">Si no está, o si es de otro tipo.</exception>
    public T Get<T>(string clave)
    {
        if (!_valores.TryGetValue(clave, out var valor))
        {
            throw new InvalidOperationException(
                $"El contexto del flujo no tiene «{clave}». Tiene: {string.Join(", ", _valores.Keys.Order(StringComparer.Ordinal))}.");
        }

        return valor switch
        {
            T t => t,
            null when default(T) is null => default!,
            _ => throw new InvalidOperationException(
                $"«{clave}» es {valor?.GetType().Name ?? "null"} y se pidió como {typeof(T).Name}."),
        };
    }
}

/// <summary>
/// Cómo se resuelve un nombre dentro de un paso: el ítem del bloque, un campo suyo, o el contexto.
/// </summary>
/// <remarks>
/// <c>linea</c> es el ítem entero y <c>linea.itemId</c> un campo del ítem; todo lo demás es del
/// contexto del flujo. No hay más sintaxis que ésa, y es deliberado: el punto separa ítem de campo,
/// no navega objetos.
/// </remarks>
internal static class Ambito
{
    public static T Leer<T>(string nombre, FlowContext ctx, string? alias, FlowContext? item)
    {
        if (alias is not null && item is not null)
        {
            if (string.Equals(nombre, alias, StringComparison.Ordinal))
            {
                return item is T t
                    ? t
                    : throw new InvalidOperationException($"«{nombre}» es un ítem y se pidió como {typeof(T).Name}.");
            }

            if (Campo(nombre, alias) is { } campo) return item.Get<T>(campo);
        }

        return ctx.Get<T>(nombre);
    }

    public static void Escribir(string nombre, object? valor, FlowContext ctx, string? alias, FlowContext? item)
    {
        if (alias is not null && item is not null && Campo(nombre, alias) is { } campo)
        {
            item.Set(campo, valor);
            return;
        }

        ctx.Set(nombre, valor);
    }

    /// <summary>El campo de <paramref name="nombre"/> si es <c>alias.campo</c>; si no, <c>null</c>.</summary>
    public static string? Campo(string nombre, string alias)
        => nombre.Length > alias.Length + 1
           && nombre.StartsWith(alias, StringComparison.Ordinal)
           && nombre[alias.Length] == '.'
            ? nombre[(alias.Length + 1)..]
            : null;
}
