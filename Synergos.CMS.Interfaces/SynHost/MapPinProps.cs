namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-map-pin&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba el centro y el zoom como TEXTO y el
/// TEXTO <c>pinsJson</c>; el elemento lee números y la LISTA <c>pins</c>: el mapa colocado se
/// centraba en el default y sin un solo pin, con las coordenadas del editor escritas.</para>
///
/// <para><b>Las coordenadas son NÚMEROS</b> y se leen con la lectura es-CO del lector: en el
/// TextBox del centro, «4.7110» y «-74,0721» viajan; en el JSON de pines un número JSON se lee tal
/// cual (<c>"lat": 4.71</c>). Una coordenada escrita como cadena con EXACTAMENTE tres decimales
/// («4.711») admite dos lecturas para el editor es-CO y no viaja: se anota para que la escriba con
/// cuatro («4.7110») o como número JSON. <c>zoomLevel</c> es un entero. El rango (latitud ±90,
/// zoom 1-18) lo aplica el elemento.</para>
///
/// <para><b>Cada pin</b>: el editor escribe <c>title</c> y el elemento lee <c>label</c>; lo
/// traduce el resolver. Un pin sin latitud o sin longitud legibles no viaja. <c>integration</c>
/// (el proveedor de mapas) lo acepta el elemento y no lo autora el ElementType: queda como
/// atributo.</para>
///
/// <para><b>Su microcopia sale del diccionario</b> (ADR 0136): sección <c>Map</c> —el nombre de la
/// región y de la lista de pines, «Ubicación {n}» para un pin sin título, el título del mapa y «Ver mapa
/// ampliado»— y <c>Common.Actions</c> para el «Ver más» de cada pin, la misma acción genérica del resto
/// del sitio (la sección se publica entera: 16 claves para una).</para>
/// </remarks>
[ElementoSynHost("map-pin", TipoDeColocable.Pieza, Diccionario = ["Map", "Common.Actions"])]
public sealed record MapPinProps(
    [property: CampoSynHost(OrigenDelCampo.Decision)] decimal? CenterLat,
    [property: CampoSynHost(OrigenDelCampo.Decision)] decimal? CenterLng,
    [property: CampoSynHost(OrigenDelCampo.Decision)] int? ZoomLevel,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<MapPinItem>? Pins);

/// <summary>Un pin: dónde está, cómo se llama y su descripción.</summary>
public sealed record MapPinItem(decimal Lat, decimal Lng, string? Label = null, string? Description = null);
