namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Contra qué compra la tienda (HU #24) — sección <c>Synergos:Tienda</c>.
/// </summary>
/// <remarks>
/// <para><b>El default es el stub, y no es una transición.</b> Un clon limpio arranca y vende sin
/// levantar seis servicios. Poner <see cref="Mode"/> en <c>Bff</c> sin el orquestador arriba
/// degrada —no se puede comprar, y lo dice— pero no tumba la tienda: el catálogo, las fichas y el
/// historial siguen sirviendo.</para>
/// </remarks>
public sealed class TiendaSettings
{
    /// <summary>
    /// <c>Stub</c> (default, el motor en proceso) o <c>Bff</c> (contra <c>Synergos.Bff.Tienda</c>).
    /// </summary>
    public string Mode { get; init; } = "Stub";

    /// <summary>Dónde vive el orquestador.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5300/";

    /// <summary>Dónde vive <c>Synergos.Api.Cart</c>: el BFF compra una CANASTA, no una lista.</summary>
    /// <remarks>
    /// <b>Es la costura que la HU daba por resuelta y no lo estaba.</b> <c>POST /v1/purchases</c>
    /// recibe un <c>cartId</c> —«copiar las líneas en la petición dejaría que el cliente pidiera
    /// algo distinto de lo que tiene en pantalla»—, así que el CMS tiene que crear la canasta
    /// antes de comprarla. Eso son dos capacidades y no una.
    /// </remarks>
    public string CartBaseUrl { get; init; } = "http://127.0.0.1:5210/";

    /// <summary>La llave compartida servicio↔servicio. Sin ella todo responde 401.</summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Transportadora por defecto al confirmar.
    /// </summary>
    /// <remarks>
    /// Va en configuración y no en el cuerpo del checkout porque <b>es una decisión del comercio,
    /// no del comprador</b>: quien compra elige a dónde le llega, no con qué empresa se despacha.
    /// La dirección, que sí es del comprador, viaja en la petición.
    /// </remarks>
    public string Carrier { get; init; } = "default";

    /// <summary>
    /// Segundos de espera. Generoso a propósito: comprar cruza seis servicios y NO es auxiliar.
    /// </summary>
    /// <remarks>
    /// Cortarlo pronto es peor que esperar. Un timeout no dice «no se cobró» — dice «no sé»; y el
    /// peor resultado posible de esta HU es un cobro sin pedido. Ver
    /// <c>HttpShopOrderService</c>: ante un timeout se consulta si la compra existió, y para eso
    /// hay que haberle dado tiempo a existir.
    /// </remarks>
    public int TimeoutSeconds { get; init; } = 30;
}
