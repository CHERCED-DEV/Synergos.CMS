using System.Text.Json;
using System.Text.Json.Serialization;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Lo que una capacidad o un orquestador contestan cuando dicen que NO — y, lo único de eso que
/// no se puede deducir desde acá, si volver a intentarlo tiene sentido.
/// </summary>
/// <remarks>
/// <para><b>Es el ÚNICO sitio del CMS que nombra la bandera <c>transient</c>, y hay gate</b>
/// (<c>TransitoriedadTests</c>, #129). La regla que este tipo encarna estaba escrita, completa y
/// correcta, dentro del único de los quince clientes <c>Http*</c> que la cumplía:</para>
///
/// <para><i>«Se mira esa bandera y no el código de estado: repetir aquí la tabla de códigos sería
/// una segunda verdad que se desincroniza.»</i></para>
///
/// <para><b>Una regla escrita en el único sitio que la obedece no se difunde: se entierra — y
/// encima PARECE difundida</b>, porque quien la lee la está leyendo ya cumplida. Es
/// <c>feedback_a_fabrication_can_be_a_derivation</c> addendum #123 con el sujeto cambiado (allá
/// lo blindado era un defecto; acá, una regla buena) y con la misma salida: lo que reemplaza a
/// la nota no es otra nota, es un TIPO más un gate.</para>
///
/// <para><b>Quién decide la transitoriedad es la capacidad, y por eso se LEE.</b>
/// <c>Rejection.IsTransient</c> viaja como extensión del <c>ProblemDetails</c>
/// (<c>Synergos.Shared/RejectionResults.cs</c>): un rechazo de negocio sale marcado
/// <c>false</c>, y lo que es «ahora no, probá luego» —una pasarela caída, el
/// <c>{prefijo}.store_busy</c> del turno de escritura (#112), un <c>retry_in_flight</c>— sale
/// marcado <c>true</c>. Rehacer esa tabla acá, en cada cliente, con códigos de estado, es
/// **catorce copias privadas de un dato que es de la capacidad**: el día que una cambie con qué
/// código sale un rechazo transitorio, las catorce seguirían compilando y decidiendo distinto.
/// </para>
///
/// <para><b>Los tres estados de <c>Transitorio</c> importan, y por eso es <c>bool?</c>.</b>
/// <c>true</c> es «volvé a intentarlo»; <c>false</c> es «la capacidad dice que no»; <c>null</c>
/// es <b>«no consta»</b> — un cuerpo que no se pudo leer, una capacidad anterior a la bandera, o
/// un 5xx de un intermediario que no viene de ninguna capacidad. La ausencia va a afirmar algo
/// pase lo que pase (<c>feedback_an_omitted_key_can_be_an_assertion</c>) y acá se elige que
/// afirme lo menos: <b>nunca firme</b>. Tratar un cuerpo ilegible como rechazo firme convertiría
/// cualquier proxy que devuelva HTML en «el banco dijo que no», que es la mentira cara; tratarlo
/// como transitorio haría reintentar contra un error permanente. Por eso
/// <see cref="EsFirme"/> exige el <c>false</c> EXPLÍCITO y
/// <see cref="EsTransitorio"/> exige el <c>true</c> explícito: sin dato, las dos son
/// <c>false</c> y quien llama tiene que mirar <see cref="Consta"/> para saber que no sabe.</para>
///
/// <para><b>Lo que este tipo NO decide, y va dicho en vez de rellenado: QUIÉN reintenta.</b> El
/// CMS no tiene ese reparto escrito. En el árbol de servicios sí —«la capacidad sabe QUÉ está
/// colgado y CÓMO se reintenta; el orquestador, CUÁNDO y CUÁNTAS VECES» (#29)— y traerlo a este
/// lado es otro trabajo con su propia pregunta de diseño. Medido al escribir esto: de los quince
/// clientes <c>Http*</c>, <b>cero</b> tienen bucle de reintento, así que hoy no hay ninguna
/// decisión que esta bandera cambie. Lo que este tipo cierra es que, el día que se tome, se tome
/// sobre UN solo dato en vez de sobre catorce tablas privadas. El disparador: el primer sitio del
/// CMS que necesite reintentar de verdad.</para>
///
/// <para><b>Y lo que cada cliente SÍ sigue decidiendo por su cuenta es cómo PRESENTAR el
/// fallo</b>, que es otra pregunta y casi siempre está bien resuelta: un 404 es «no existe», un
/// 401 nombra la llave compartida, un 4xx con motivo sube como rechazo de negocio. Eso no se
/// centraliza — depende del dominio de cada seam.</para>
/// </remarks>
public sealed record RechazoDelArbolDeServicios(
    [property: JsonPropertyName("code")] string? Codigo,
    [property: JsonPropertyName("detail")] string? Detalle,
    [property: JsonPropertyName("transient")] bool? Transitorio)
{
    /// <summary>
    /// La capacidad dijo que no, y lo dijo a sabiendas: volver a intentarlo dará lo mismo.
    /// </summary>
    /// <remarks>
    /// Exige el <c>false</c> explícito. «No consta» NO es firme — ver el <c>&lt;remarks&gt;</c>
    /// del tipo.
    /// </remarks>
    public bool EsFirme => Transitorio == false;

    /// <summary>
    /// No es que no se pueda: es que ahora no. Volver a intentarlo puede salir distinto.
    /// </summary>
    /// <remarks>
    /// Exige el <c>true</c> explícito, por la misma razón que <see cref="EsFirme"/>.
    /// </remarks>
    public bool EsTransitorio => Transitorio == true;

    /// <summary>
    /// Si la respuesta trajo la bandera. <c>false</c> significa «no se sabe», que no es lo mismo
    /// que ninguna de las dos respuestas.
    /// </summary>
    public bool Consta => Transitorio is not null;

    /// <summary>
    /// Lee el rechazo del cuerpo de una respuesta que no fue exitosa.
    /// </summary>
    /// <remarks>
    /// <para><b>Un cuerpo que no se puede leer devuelve <c>null</c>, no lanza.</b> Quien llama ya
    /// está en su camino de fallo y lo que necesita es decidir qué contar, no una segunda
    /// excepción con la causa equivocada. Un <c>null</c> aquí y un rechazo con
    /// <c>Transitorio = null</c> significan lo mismo para las tres propiedades de arriba, que es
    /// lo que hace que el llamador no tenga que distinguirlos.</para>
    ///
    /// <para><b>Se lee por BYTES y no por <c>ReadFromJsonAsync</c>, y es lo que deja leerlo DOS
    /// veces</b> (#178). La cadena de reintento de <c>ClienteDelArbolDeServicios</c> mira la
    /// bandera antes que el cliente; <c>ReadFromJsonAsync</c> abre el flujo del contenido, lo
    /// guarda dentro de él y lo CIERRA al terminar, así que la segunda lectura —la del cliente,
    /// que es la que cuenta el motivo— lanza <see cref="ObjectDisposedException"/>, que el filtro
    /// de abajo no captura — medido con la mutación: «Cannot access a closed Stream». Un «no»
    /// firme del medio de pago habría subido como una excepción sin traducir.
    /// <c>ReadAsByteArrayAsync</c> deja el cuerpo en el búfer del contenido y no guarda flujo,
    /// así que cada lectura lo vuelve a encontrar entero.</para>
    /// </remarks>
    public static async Task<RechazoDelArbolDeServicios?> LeerAsync(
        HttpResponseMessage respuesta, JsonSerializerOptions json, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(respuesta);

        try
        {
            var cuerpo = await respuesta.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return cuerpo.Length == 0
                ? null
                : JsonSerializer.Deserialize<RechazoDelArbolDeServicios>(cuerpo, json);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or HttpRequestException)
        {
            return null;
        }
    }
}
