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

    /// <summary>El rol de personal que ya existía antes de poder declararlo: el de la consola de devoluciones.</summary>
    public const string RolPorDefecto = "admin";

    /// <summary>
    /// Quién es personal de la tienda: los roles de miembro que atienden la consola del vendedor, las devoluciones y
    /// la oferta (013-seller-operaciones, lote 12: P1 = A). Sin declararlos, <see cref="RolPorDefecto"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Una sola declaración.</b> La leen las guardas del CMS y la cruzan las entradas de la puerta de los
    /// flujos del personal de la tienda (<c>RolesDelPersonalDeLaTiendaTests</c>): un rol que valiera en una y no en
    /// la otra dejaría a alguien atender por una ruta y no por la otra.</para>
    ///
    /// <para><b>Vacía es «sin declarar», no «nadie».</b> La configuración no distingue una lista vacía de una que
    /// falta, así que no hay forma de declarar una tienda sin personal. Un rol en blanco sí es un error, y el CMS no
    /// arranca (<c>ValidadorDeLaTienda</c>).</para>
    /// </remarks>
    public List<string> RolesDelPersonal { get; init; } = [];

    /// <summary>
    /// Lo que la tienda promete en la ficha de cada producto (016-contenido-piezas): <c>Envio</c>, <c>Devoluciones</c>,
    /// <c>Garantia</c>. El texto de cada promesa lo escribe el editor en el diccionario (<c>Shop.Product.Trust.*</c>);
    /// esto dice cuáles son VERDAD para esta tienda.
    /// </summary>
    /// <remarks>
    /// <b>Sin declarar, la ficha no promete nada.</b> Antes pintaba siempre «Envío gratis a todo el país · Devoluciones en
    /// 30 días · Garantía del fabricante», en un sitio que avisa que no hace envíos. Un nombre que no se conoce no
    /// pinta nada.
    /// </remarks>
    public List<string> Promesas { get; init; } = [];

    /// <summary>
    /// Las raíces de sitio (<c>siteRoot</c>, por su Key) cuya ficha no muestra la referencia ni la disponibilidad
    /// (016, D-016-C24): en Booking la ficha es un servicio que se reserva, y «Referencia: WEL-MASAJE-60» o «Disponible»
    /// no le dicen nada a quien reserva.
    /// </summary>
    /// <remarks>
    /// <b>Por la Key del sitio y no por el dominio:</b> la base real no tiene dominios, y lo que se resuelve por dominio
    /// cae siempre a la base sin avisar. La vista toma el sitio del <c>siteRoot</c> ancestro de la ficha.
    /// </remarks>
    public List<Guid> SitiosSinReferenciaNiStock { get; init; } = [];

    /// <summary>Si la ficha de un producto del sitio <paramref name="sitio"/> muestra la referencia y la disponibilidad.</summary>
    public bool MuestraReferenciaYStock(Guid? sitio) => sitio is not { } key || !SitiosSinReferenciaNiStock.Contains(key);

    /// <summary>Los roles que valen: los declarados o, sin declarar, <see cref="RolPorDefecto"/>.</summary>
    public IReadOnlyList<string> Personal() => RolesDelPersonal.Count > 0 ? RolesDelPersonal : [RolPorDefecto];

    /// <summary>Los roles como los pide <c>IMemberAccessGate.HasAnyRole</c>: separados por comas.</summary>
    public string PersonalCsv() => string.Join(',', Personal());
}
