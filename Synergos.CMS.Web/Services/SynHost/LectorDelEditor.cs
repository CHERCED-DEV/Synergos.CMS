using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// Lee lo que el editor autoró en un bloque, con la sobrecarga de Umbraco que recibe el
/// <see cref="IPublishedValueFallback"/> en vez de sacarlo del proveedor estático.
/// </summary>
/// <remarks>
/// <para><b>Por qué no <c>Model.Value&lt;string&gt;("alias")</c></b>, que es lo que hacían las
/// vistas: esa forma «amigable» saca el fallback de <c>StaticServiceProvider</c>, así que un
/// resolver escrito con ella no se puede probar sin arrancar Umbraco. Con el fallback inyectado
/// es la misma lectura —misma cultura, mismo segmento— y un test la alimenta con un
/// <c>IPublishedElement</c> falso.</para>
///
/// <para>Es COMPOSICIÓN, no una base: cada resolver crea uno y le pide lo que necesita.</para>
///
/// <para><b>Lo que el editor escribió mal no tumba la página ni se pierde callado.</b> Un número
/// que no es número deja la clave fuera —el elemento aplica su valor por defecto— y se anota en el
/// log con el bloque y la propiedad (ADR 0135 §2: fallar ruidosamente, nunca un widget vacío en
/// silencio).</para>
/// </remarks>
public sealed class LectorDelEditor
{
    private readonly IPublishedElement _elemento;
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger? _log;
    private readonly IPublishedUrlProvider? _urls;

    /// <param name="elemento">El bloque autorado.</param>
    /// <param name="fallback">El fallback de valores de Umbraco (inyectado: ver arriba).</param>
    /// <param name="log">Donde se anota lo que el editor escribió y no se pudo usar.</param>
    /// <param name="urls">
    /// Sólo lo necesita quien lee medios (<see cref="Medio"/>): la URL de un medio la calcula
    /// Umbraco con el proveedor de URLs, y la forma «amigable» <c>media.Url()</c> lo saca del
    /// proveedor estático — el mismo motivo por el que el fallback se inyecta.
    /// </param>
    public LectorDelEditor(
        IPublishedElement elemento,
        IPublishedValueFallback fallback,
        ILogger? log = null,
        IPublishedUrlProvider? urls = null)
    {
        ArgumentNullException.ThrowIfNull(elemento);
        ArgumentNullException.ThrowIfNull(fallback);
        _elemento = elemento;
        _fallback = fallback;
        _log = log;
        _urls = urls;
    }

    /// <summary>
    /// El texto de <paramref name="alias"/>, recortado; <c>null</c> si el editor lo dejó vacío.
    /// </summary>
    /// <remarks>
    /// Vacío y ausente son lo mismo para el elemento, y así la clave no viaja: el emitter omite
    /// los nulos y el elemento aplica su valor por defecto.
    /// </remarks>
    public string? Texto(string alias)
    {
        var crudo = _elemento.Value<string>(_fallback, alias);
        return string.IsNullOrWhiteSpace(crudo) ? null : crudo.Trim();
    }

    /// <summary>El interruptor (TrueFalse) de <paramref name="alias"/>: <c>false</c> si el editor no lo tocó.</summary>
    public bool Interruptor(string alias) => _elemento.Value<bool>(_fallback, alias);

    /// <summary>
    /// Las entradas de la lista JSON que el editor escribió en <paramref name="alias"/> (un
    /// TextArea); <c>null</c> si no escribió nada, si no parsea o si no es una lista.
    /// </summary>
    /// <param name="alias">La propiedad, para el log.</param>
    /// <param name="json">Lo que leyó <see cref="Texto"/> de esa propiedad.</param>
    /// <remarks>
    /// Parsear acá y no en el navegador es lo que deja al record declarar una LISTA tipada en vez de
    /// un texto: el elemento lee una lista, y un texto con la forma de una lista es lo que dejaba a
    /// <c>dropdown</c> y <c>carousel</c> vacíos (D1). Cada entrada sale clonada: sobrevive al
    /// documento.
    /// </remarks>
    public IReadOnlyList<JsonElement>? ListaJson(string alias, string? json)
    {
        if (json is null)
        {
            return null;
        }

        try
        {
            using var documento = JsonDocument.Parse(json);
            if (documento.RootElement.ValueKind == JsonValueKind.Array)
            {
                return documento.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
            }
        }
        catch (JsonException)
        {
            NoEsValido(alias, json, "un JSON válido");
            return null;
        }

        NoEsValido(alias, json, "una lista JSON");
        return null;
    }

    /// <summary>El texto de <paramref name="clave"/> en una entrada de una lista JSON (un número vale como texto).</summary>
    public static string? Cadena(JsonElement entrada, string clave)
    {
        if (entrada.ValueKind != JsonValueKind.Object || !entrada.TryGetProperty(clave, out var valor))
        {
            return null;
        }

        var texto = valor.ValueKind switch
        {
            JsonValueKind.String => valor.GetString(),
            JsonValueKind.Number => valor.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    }

    /// <summary>
    /// Un número que el editor escribió como texto; <c>null</c> si lo dejó vacío, si no es un número
    /// o si admite dos lecturas.
    /// </summary>
    /// <remarks>
    /// <para><b>El editor es es-CO: el punto separa MILES y la coma, decimales.</b> Leerlo con la
    /// regla invariante —como se hizo en el piloto, cambiando la coma por punto— convertía
    /// «500.000» en 500, mil veces menos y en silencio: la trampa que el motor de catálogo ya pagó
    /// con «49.000». Se lee lo inequívoco: «4», «4,5», «4.5» (un punto seguido de menos o más de
    /// tres cifras no puede separar miles), «1.234.567», «1.234,5», y sus gemelos con coma de
    /// miles («1,234,567», «1,234.5»).</para>
    ///
    /// <para><b>Lo que admite dos lecturas no viaja y se anota</b>: «1.234» y «500.000» son mil
    /// doscientos treinta y cuatro y quinientos mil para el editor es-CO, y uno coma dos y
    /// quinientos para quien escribe a la inglesa; «1,234», al revés. Adivinar es exactamente el
    /// defecto. El editor lo ve en el log y lo escribe sin separador.</para>
    ///
    /// <para>No se acotan rangos: el rango es regla del elemento, que es quien lo pinta.</para>
    /// </remarks>
    public decimal? Numero(string alias)
    {
        var texto = Texto(alias);
        if (texto is null)
        {
            return null;
        }

        var lectura = LeerNumero(texto);
        if (lectura.Ambiguo)
        {
            NoEsValido(alias, texto, EsperadoSinAmbiguedad);
            return null;
        }

        if (lectura.Valor is null)
        {
            NoEsValido(alias, texto, "un número");
        }

        return lectura.Valor;
    }

    /// <summary>
    /// Un entero que el editor escribió como texto; <c>null</c> si vacío, si no es un entero, si
    /// tiene decimales o si admite dos lecturas.
    /// </summary>
    /// <remarks>
    /// La misma lectura es-CO que <see cref="Numero"/>: «1.000.000» es un millón y «5.000» no
    /// viaja (cinco mil o cinco). Un número con decimales («5,5», «4.5») no es un entero y se
    /// anota; no se redondea.
    /// </remarks>
    public int? Entero(string alias)
    {
        var texto = Texto(alias);
        if (texto is null)
        {
            return null;
        }

        var lectura = LeerNumero(texto);
        if (lectura.Ambiguo)
        {
            NoEsValido(alias, texto, EsperadoSinAmbiguedad);
            return null;
        }

        if (lectura.Valor is { } valor && !lectura.ConDecimales && valor is >= int.MinValue and <= int.MaxValue)
        {
            return (int)valor;
        }

        NoEsValido(alias, texto, "un entero");
        return null;
    }

    private const string EsperadoSinAmbiguedad =
        "un número con una sola lectura («1.234» es mil doscientos treinta y cuatro en es-CO y uno coma dos a la inglesa: escribilo sin separador de miles)";

    private static readonly Regex SoloCifras = new(@"^\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex MilesConPunto = new(@"^\d{1,3}(\.\d{3}){2,}$", RegexOptions.CultureInvariant);
    private static readonly Regex MilesConComa = new(@"^\d{1,3}(,\d{3}){2,}$", RegexOptions.CultureInvariant);
    private static readonly Regex MilesConPuntoYDecimales = new(@"^\d{1,3}(\.\d{3})+,\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex MilesConComaYDecimales = new(@"^\d{1,3}(,\d{3})+\.\d+$", RegexOptions.CultureInvariant);
    private static readonly Regex DosLecturas = new(@"^\d{1,3}[.,]\d{3}$", RegexOptions.CultureInvariant);
    private static readonly Regex ConDecimales = new(@"^\d+[.,]\d+$", RegexOptions.CultureInvariant);

    /// <summary>Cómo se lee un número escrito por un editor es-CO (ver <see cref="Numero"/>).</summary>
    private static (decimal? Valor, bool ConDecimales, bool Ambiguo) LeerNumero(string texto)
    {
        var negativo = texto.StartsWith('-');
        var cifras = negativo ? texto[1..] : texto;

        var (normalizado, conDecimales) = cifras switch
        {
            _ when SoloCifras.IsMatch(cifras) => (cifras, false),
            _ when MilesConPunto.IsMatch(cifras) => (cifras.Replace(".", string.Empty, StringComparison.Ordinal), false),
            _ when MilesConComa.IsMatch(cifras) => (cifras.Replace(",", string.Empty, StringComparison.Ordinal), false),
            _ when MilesConPuntoYDecimales.IsMatch(cifras) => (cifras.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.'), true),
            _ when MilesConComaYDecimales.IsMatch(cifras) => (cifras.Replace(",", string.Empty, StringComparison.Ordinal), true),
            _ when DosLecturas.IsMatch(cifras) => ((string?)null, false),
            _ when ConDecimales.IsMatch(cifras) => (cifras.Replace(',', '.'), true),
            _ => (null, false),
        };

        if (normalizado is null)
        {
            return (null, false, DosLecturas.IsMatch(cifras));
        }

        return decimal.TryParse(normalizado, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var valor)
            ? (negativo ? -valor : valor, conDecimales, false)
            : (null, false, false);
    }

    /// <summary>
    /// El medio que el editor eligió en <paramref name="alias"/> (un <c>Umbraco.MediaPicker3</c>):
    /// su URL absoluta y su texto alternativo; <c>null</c> si no eligió ninguno o si el medio no
    /// tiene fichero.
    /// </summary>
    /// <remarks>
    /// <para><b>Viaja la URL, no el medio.</b> El elemento lee una cadena (<c>audioFile</c>,
    /// <c>src</c>, <c>media</c>…); lo que las vistas hacían con <c>media?.Url(mode: Absolute)</c> se
    /// hace acá con el proveedor inyectado y el mismo modo, así que el valor que viaja es el de
    /// siempre y el resolver se prueba sin arrancar Umbraco.</para>
    ///
    /// <para><b>El texto alternativo es el del medio</b> (<c>altDefault</c> de <c>synImage</c> y
    /// <c>synIcon</c>): lo escribe el editor una vez en la biblioteca. Un medio de otro tipo no lo
    /// tiene y sale <c>null</c>. Si el ElementType ofrece además un texto por instancia, gana ése:
    /// lo decide el resolver, que es quien sabe de dónde sale cada campo.</para>
    ///
    /// <para>Los DataTypes de hoy son de un solo medio (<c>Multiple: false</c>). Si uno pasara a
    /// varios, el valor llega como lista y se toma el primero en vez de perderlo en silencio.</para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">El lector se construyó sin
    /// <see cref="IPublishedUrlProvider"/>: es un defecto del resolver, no del editor.</exception>
    public MedioDelEditor? Medio(string alias)
    {
        if (_urls is null)
        {
            throw new InvalidOperationException(
                $"Se pidió el medio «{alias}» a un LectorDelEditor construido sin IPublishedUrlProvider: "
                + "el resolver que lee medios tiene que inyectarlo y pasárselo.");
        }

        var medio = _elemento.Value<object>(_fallback, alias) switch
        {
            IPublishedContent uno => uno,
            IEnumerable<IPublishedContent> varios => varios.FirstOrDefault(),
            _ => null,
        };

        if (medio is null)
        {
            return null;
        }

        var url = _urls.GetMediaUrl(medio, UrlMode.Absolute, culture: null, propertyAlias: Constants.Conventions.Media.File);
        if (string.IsNullOrWhiteSpace(url) || url == "#")
        {
            NoEsValido(alias, medio.Key.ToString(), "un medio con fichero");
            return null;
        }

        var alt = medio.Value<string>(_fallback, "altDefault");
        return new MedioDelEditor(url.Trim(), string.IsNullOrWhiteSpace(alt) ? null : alt.Trim());
    }

    /// <summary>
    /// El enlace que el editor puso en <paramref name="alias"/> (un <c>Umbraco.MultiUrlPicker</c>):
    /// su destino, su texto y dónde abre; <c>null</c> si no puso ninguno o si no tiene destino.
    /// </summary>
    /// <remarks>
    /// <para><b>Viaja el destino ya resuelto</b> (<c>Link.Url</c>): para un enlace a una página o a
    /// un medio, Umbraco lo calcula al convertir el valor, así que el elemento recibe una URL y no
    /// un identificador que no sabe resolver. Es lo que las vistas hacían con <c>link?.Url</c>.</para>
    ///
    /// <para><b>El texto y el destino de apertura son del editor</b>: <c>Link.Name</c> es el título
    /// que escribe en el selector y <c>Link.Target</c> la casilla «abrir en una ventana nueva»
    /// (<c>_blank</c>). Cuál de los dos viaja, y con qué nombre, lo decide cada resolver según lo
    /// que su elemento pinte.</para>
    ///
    /// <para>Un enlace sin destino (vacío, o <c>#</c>, que es lo que da Umbraco para una página que
    /// no tiene ruta) no viaja y se anota. Los DataTypes de hoy son de un solo enlace
    /// (<c>MaxNumber: 1</c>); si uno pasara a varios, se toma el primero.</para>
    /// </remarks>
    public EnlaceDelEditor? Enlace(string alias)
    {
        var enlace = _elemento.Value<object>(_fallback, alias) switch
        {
            Link uno => uno,
            IEnumerable<Link> varios => varios.FirstOrDefault(),
            _ => null,
        };

        if (enlace is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(enlace.Url) || enlace.Url.Trim() == "#")
        {
            NoEsValido(alias, enlace.Name ?? enlace.Udi?.ToString() ?? "(enlace sin nombre)", "un enlace con destino");
            return null;
        }

        return new EnlaceDelEditor(
            enlace.Url.Trim(),
            string.IsNullOrWhiteSpace(enlace.Name) ? null : enlace.Name.Trim(),
            string.IsNullOrWhiteSpace(enlace.Target) ? null : enlace.Target.Trim());
    }

    /// <summary>
    /// Las opciones que el editor marcó en <paramref name="alias"/> (un
    /// <c>Umbraco.DropDown.Flexible</c> con <c>multiple: true</c>), en el orden en que Umbraco las
    /// guarda; <c>null</c> si no marcó ninguna.
    /// </summary>
    /// <remarks>
    /// <para><b>Viaja una LISTA, no un texto con comas.</b> La vista unía las opciones en
    /// <c>platformsCsv</c> para que el elemento las volviera a partir: dos copias de la misma
    /// regla —el separador— en dos lenguajes, y un nombre de clave que el elemento no leía.</para>
    ///
    /// <para>Umbraco entrega un desplegable múltiple como lista de cadenas y uno simple como una
    /// cadena: las dos formas se leen igual, así que un DataType que cambie de simple a múltiple
    /// no deja al elemento sin valor. Una opción vacía no viaja; una repetida viaja una vez.</para>
    ///
    /// <para><b>Los VALORES no se traducen acá.</b> Si el vocabulario del DataType y el del
    /// elemento llaman distinto a lo mismo, lo traduce el resolver, que es quien sabe a qué
    /// elemento va.</para>
    /// </remarks>
    public IReadOnlyList<string>? Opciones(string alias)
    {
        IEnumerable<string>? marcadas = _elemento.Value<object>(_fallback, alias) switch
        {
            string una => [una],
            IEnumerable<string> varias => varias,
            _ => null,
        };

        var limpias = marcadas?
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return limpias is { Count: > 0 } ? limpias : null;
    }

    /// <summary>Anota lo que el editor escribió y no se pudo usar: no viaja, pero no se pierde callado.</summary>
    public void NoEsValido(string alias, string valor, string esperado)
        => _log?.LogWarning(
            "El bloque {Bloque} ({Tipo}) trae en «{Alias}» algo que no es {Esperado}: «{Valor}». No viaja al elemento.",
            _elemento.Key,
            _elemento.ContentType?.Alias,
            alias,
            esperado,
            valor);
}

/// <summary>Un medio que el editor eligió, como lo necesita un elemento: dónde está y cómo se describe.</summary>
/// <param name="Url">La URL absoluta del fichero.</param>
/// <param name="Alt">El texto alternativo del medio (<c>altDefault</c>), o <c>null</c> si no tiene.</param>
public sealed record MedioDelEditor(string Url, string? Alt);

/// <summary>Un enlace que el editor puso, como lo necesita un elemento.</summary>
/// <param name="Url">El destino ya resuelto por Umbraco (página, medio o URL externa).</param>
/// <param name="Nombre">El texto que el editor escribió para el enlace, o <c>null</c>.</param>
/// <param name="Destino">Dónde abre (<c>_blank</c> si marcó «ventana nueva»), o <c>null</c>.</param>
public sealed record EnlaceDelEditor(string Url, string? Nombre, string? Destino);
