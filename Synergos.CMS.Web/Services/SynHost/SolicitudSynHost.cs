using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// Convierte lo que resolvió un <see cref="IResolutorSynHost{TProps}"/> en la
/// <see cref="SynHostEmitRequest"/> de siempre. Es lo único genérico del camino: se escribe una
/// vez y lo usan todos los elementos migrados.
/// </summary>
/// <remarks>
/// <para><b>El cable no cambia</b> (ADR 0135 §7). El emitter sigue recibiendo un diccionario y
/// escribiendo <c>&lt;synergos-x config='{json}'&gt;</c> con el import map del registry; lo que
/// cambia es quién arma el diccionario: sale del record, serializado con las mismas reglas que el
/// emitter (camelCase, sin nulos), así que el nombre de cada clave es el del record y no uno
/// escrito a mano en Razor.</para>
///
/// <para><b><c>configOverride</c> sólo puede pisar lo que el record declara.</b> Es el JSON libre
/// del editor que el emitter fusiona ENCIMA de los props; sin filtro, cualquier clave viajaría
/// —la puerta por la que el record dejaría de ser la verdad de lo que llega—. Una clave que el
/// record no declara se descarta acá, antes del emitter; un override de un campo declarado se
/// respeta como siempre. Es la decisión del piloto para las PIEZAS (ADR 0135 §6).</para>
/// </remarks>
public static class SolicitudSynHost
{
    /// <summary>
    /// Las reglas del cable: las mismas que <c>DefaultSynHostEmitter</c> aplica al serializar.
    /// </summary>
    /// <remarks>
    /// El nombre de cada clave se lee de los METADATOS de System.Text.Json con estas opciones
    /// (<see cref="NombresDelCable"/>), no se recalcula aparte: así el que viaja y el que se
    /// declara en el contrato no pueden divergir por una regla de nombres escrita dos veces.
    /// </remarks>
    public static JsonSerializerOptions Cable { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    /// <summary>La solicitud de emisión para lo que resolvió el resolver de un elemento.</summary>
    /// <param name="resuelto">Lo que devolvió el resolver.</param>
    /// <param name="configOverride">El JSON libre del editor (<c>compIntegration.configOverride</c>).</param>
    /// <param name="culture">La cultura de la petición.</param>
    public static SynHostEmitRequest Para<TProps>(
        ElementoResuelto<TProps> resuelto,
        string? configOverride,
        CultureInfo culture)
        where TProps : class
    {
        ArgumentNullException.ThrowIfNull(resuelto);
        ArgumentNullException.ThrowIfNull(culture);

        var elemento = Elemento(typeof(TProps));

        return new SynHostEmitRequest(
            BlockAlias: elemento.Nombre,
            Props: Props(resuelto.Props),
            // ADR 0135 §6: en una FUNCIONALIDAD el JSON libre del editor no entra — su
            // configuración de negocio no la escribe el editor (ADR 0137) y su microcopia sale del
            // diccionario (ADR 0136). En una pieza sólo pisa lo que el record declara.
            ConfigOverrideJson: elemento.Tipo == TipoDeColocable.Funcionalidad
                ? null
                : SoloLoDeclarado(configOverride, NombresDelCable(typeof(TProps))),
            Culture: culture,
            FallbackHtml: resuelto.RespaldoHtml,
            StructuredDataJson: resuelto.DatosEstructurados,
            Diccionario: elemento.Diccionario);
    }

    /// <summary>El elemento al que está atado <paramref name="record"/>.</summary>
    /// <exception cref="InvalidOperationException">El tipo no lleva <see cref="ElementoSynHostAttribute"/>.</exception>
    public static ElementoSynHostAttribute Elemento(Type record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.GetCustomAttribute<ElementoSynHostAttribute>()
            ?? throw new InvalidOperationException(
                $"{record.Name} no lleva [ElementoSynHost]: sin él no se sabe a qué elemento del registry va.");
    }

    /// <summary>Los nombres de las claves de <paramref name="record"/> en el cable, en orden de declaración.</summary>
    public static IReadOnlyList<string> NombresDelCable(Type record)
        => Cable.GetTypeInfo(record).Properties.Select(p => p.Name).ToList();

    /// <summary>El record como el diccionario que espera el emitter: sus claves del cable, sin nulos.</summary>
    public static IReadOnlyDictionary<string, object?> Props<TProps>(TProps props)
        where TProps : class
    {
        ArgumentNullException.ThrowIfNull(props);

        var salida = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var propiedad in JsonSerializer.SerializeToElement(props, Cable).EnumerateObject())
        {
            salida[propiedad.Name] = propiedad.Value;
        }

        return salida;
    }

    /// <summary>
    /// <paramref name="configOverride"/> con sólo las claves de <paramref name="declarados"/>, o
    /// <c>null</c> si no queda ninguna o no es un objeto JSON.
    /// </summary>
    /// <remarks>
    /// Un JSON que no parsea se descarta igual que lo descartaba el emitter: el editor ve en la
    /// vista previa el bloque con su configuración de siempre.
    /// </remarks>
    public static string? SoloLoDeclarado(string? configOverride, IReadOnlyCollection<string> declarados)
    {
        ArgumentNullException.ThrowIfNull(declarados);
        if (string.IsNullOrWhiteSpace(configOverride))
        {
            return null;
        }

        JsonObject? objeto;
        try
        {
            objeto = JsonNode.Parse(configOverride) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }

        if (objeto is null)
        {
            return null;
        }

        var filtrado = new JsonObject();
        foreach (var (clave, valor) in objeto)
        {
            if (declarados.Contains(clave, StringComparer.Ordinal))
            {
                filtrado[clave] = valor?.DeepClone();
            }
        }

        return filtrado.Count == 0 ? null : filtrado.ToJsonString();
    }
}
