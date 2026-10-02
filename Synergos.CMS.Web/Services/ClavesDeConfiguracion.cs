using System.Reflection;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Lo que el binder de .NET tira en silencio al enlazar una sección a su POCO: las claves que
/// ninguna propiedad lee (ADR 0137).
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> <c>Configure&lt;T&gt;</c> enlaza lo que casa y descarta el
/// resto sin avisar (la lección del #138): <c>FeePercnt: 8</c> deja la comisión en su valor por
/// defecto y el servidor arranca como si nadie la hubiera configurado. Cada validador de una
/// sección de negocio llama acá para que eso falle al arrancar.</para>
///
/// <para><b>Los valores no se miran acá.</b> Un número que no se puede convertir —<c>1,5</c>, como
/// se escribe en es-CO— ya lo rechaza el binder al arrancar (medido: no lo lee como 15). Lo que el
/// binder no ve es la CLAVE que no casa.</para>
///
/// <para>Las claves se comparan sin mayúsculas, como las compara el binder.</para>
/// </remarks>
public static class ClavesDeConfiguracion
{
    /// <summary>
    /// Las claves de <paramref name="seccion"/> que <paramref name="tipo"/> no lee, con su ruta
    /// completa. Vacío si no hay ninguna.
    /// </summary>
    public static IReadOnlyList<string> QueNadieLee(IConfigurationSection seccion, Type tipo)
    {
        ArgumentNullException.ThrowIfNull(seccion);
        ArgumentNullException.ThrowIfNull(tipo);

        var fallos = new List<string>();
        Recorrer(seccion, tipo, fallos);
        return fallos;
    }

    private static void Recorrer(IConfigurationSection seccion, Type tipo, List<string> fallos)
    {
        var propiedades = tipo
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var hijo in seccion.GetChildren())
        {
            if (!propiedades.TryGetValue(hijo.Key, out var propiedad))
            {
                fallos.Add($"{hijo.Path}: {tipo.Name} no tiene esa clave, así que nadie la lee. "
                    + $"Las que lee: {string.Join(", ", propiedades.Keys)}.");
                continue;
            }

            Valor(hijo, propiedad.PropertyType, fallos);
        }
    }

    private static void Valor(IConfigurationSection valor, Type tipo, List<string> fallos)
    {
        if (Diccionario(tipo) is { } elemento)
        {
            foreach (var entrada in valor.GetChildren())
            {
                Valor(entrada, elemento, fallos);
            }

            return;
        }

        var simple = Nullable.GetUnderlyingType(tipo) ?? tipo;
        if (simple != typeof(string) && simple.IsClass)
        {
            Recorrer(valor, simple, fallos);
        }
    }

    private static Type? Diccionario(Type tipo)
        => tipo.IsGenericType
            && tipo.GetGenericTypeDefinition() == typeof(Dictionary<,>)
            && tipo.GetGenericArguments()[0] == typeof(string)
            ? tipo.GetGenericArguments()[1]
            : null;
}
