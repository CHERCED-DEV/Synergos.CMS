using System.Text.Json;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Lo que el árbol de servicios contesta cuando dice que NO: su código, su motivo, y —lo único
/// que decide si se puede volver a intentar— si el rechazo es <b>transitorio</b>.
/// </summary>
/// <remarks>
/// <para><b>Esta regla ya estaba escrita, y en el sitio equivocado</b> (#129). Vivía en el
/// <c>&lt;remarks&gt;</c> de <c>HttpPaymentProvider</c>, el único de los quince clientes que la
/// aplicaba, con todas las letras: <i>«Se mira esa bandera y no el código de estado: repetir aquí
/// la tabla de códigos sería una segunda verdad que se desincroniza»</i>. Es
/// <c>feedback_a_fabrication_can_be_a_derivation</c> addendum #123 tal cual — arreglar una copia
/// y dejar la regla escrita dentro de ella es la forma más eficaz de que las otras catorce sigan
/// como están.</para>
///
/// <para><b>La transitoriedad la decide la CAPACIDAD, no el código de estado.</b> El mismo 409
/// puede ser un rechazo firme de negocio y el mismo 503 puede ser «la réplica de al lado no soltó
/// el turno de escritura» —<c>{prefijo}.store_busy</c>, que emiten las diecinueve capacidades que
/// guardan por <c>JsonCollectionStore</c> desde el #112—. Por eso el #112 eligió
/// <c>Unavailable</c> y no <c>Conflict</c> para ese caso: «con <c>Conflict</c> un orquestador
/// deshace una saga sana por quince milisegundos de cola». Una tabla de códigos en el CMS
/// desharía esa decisión sin enterarse.</para>
///
/// <para><b><c>Transitorio</c> es <c>bool?</c> y los tres estados importan</b>
/// (<c>feedback_gethashcode_is_not_a_seed</c>, addendum #111): <c>true</c> es «volvé a
/// intentarlo», <c>false</c> es «la capacidad dice que no y no va a cambiar», y <c>null</c> es
/// <b>no consta</b> — un cuerpo que no se pudo leer. Y el suelo lo fijó el mismo cliente que ya
/// lo tenía bien: <i>«Si el cuerpo no se puede leer, es "no sé": tratarlo como rechazo firme
/// convertiría cualquier intermediario que devuelva HTML en "el banco dijo que no"»</i>. Por eso
/// <see cref="EsFirme"/> exige el <c>false</c> explícito y nunca lo deduce de la ausencia.</para>
///
/// <para><b>Lo que esto NO hace, y va dicho porque es la mitad que falta:</b> no reintenta.
/// Medido al escribirlo, <b>cero</b> de los quince clientes tiene bucle de reintento, y quién
/// debería tenerlo —el cliente o quien lo llama— es una decisión que el CMS no tiene escrita. En
/// el árbol de servicios sí está («la capacidad sabe QUÉ está colgado y CÓMO se reintenta; el
/// orquestador, CUÁNDO y CUÁNTAS VECES») y traerla acá es otro trabajo. Lo que este tipo cierra
/// es que la decisión, el día que se tome, se tome <b>sobre un solo dato</b>.</para>
/// </remarks>
internal sealed record RechazoDelArbolDeServicios(string? Codigo, string? Motivo, bool? Transitorio)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };

    /// <summary>La capacidad dijo que no, y dijo que no va a cambiar.</summary>
    internal bool EsFirme => Transitorio == false;

    /// <summary>La capacidad dijo que sí se puede volver a intentar.</summary>
    internal bool EsTransitorio => Transitorio == true;

    /// <summary>
    /// Lee el cuerpo del rechazo. Nunca lanza: un cuerpo ilegible es <c>null</c> en
    /// <see cref="Transitorio"/>, que es «no consta» y no «firme».
    /// </summary>
    internal static async Task<RechazoDelArbolDeServicios> LeerAsync(
        HttpResponseMessage respuesta, CancellationToken ct = default)
    {
        try
        {
            var dto = await respuesta.Content
                .ReadFromJsonAsync<CuerpoDelRechazo>(Json, ct)
                .ConfigureAwait(false);

            return dto is null
                ? new RechazoDelArbolDeServicios(null, null, null)
                : new RechazoDelArbolDeServicios(dto.Code, dto.Detail, dto.Transient);
        }
        catch (Exception e) when (e is JsonException or HttpRequestException or NotSupportedException)
        {
            // Un proxy que devuelve HTML, una respuesta vacía, un 502 del balanceador. No es
            // firme: es que nadie del árbol de servicios habló.
            return new RechazoDelArbolDeServicios(null, null, null);
        }
    }

    /// <summary>La forma con la que <c>Synergos.Shared</c> serializa un <c>Rejection</c>.</summary>
    private sealed record CuerpoDelRechazo(string? Code, string? Detail, bool? Transient);
}
