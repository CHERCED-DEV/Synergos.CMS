namespace Synergos.CMS.Interfaces;

/// <summary>
/// Una butaca del mapa de asientos.
/// </summary>
/// <param name="Id">
/// Identificador estable de la butaca, tal como lo nombra el proveedor. En una cabina es la
/// convención aeronáutica <c>{fila}{columna}</c> — <c>12A</c>.
/// </param>
/// <param name="Column">
/// La letra de la columna. <b>Se salta la I</b>: en un asiento impreso se confunde con un 1, y
/// por eso ninguna aerolínea la usa. El componente que pinta el mapa aplica la misma regla.
/// </param>
/// <param name="Status">
/// <c>free</c> | <c>held</c> | <c>sold</c> | <c>blocked</c>. <c>blocked</c> es la butaca que
/// existe físicamente pero no se vende (tripulación, avería, distanciamiento), y es distinta de
/// <c>sold</c>: no se libera sola ni la compró nadie.
/// </param>
/// <param name="Position">
/// <c>window</c> | <c>aisle</c> | <c>middle</c>. Lo deriva el proveedor, que es quien conoce la
/// geometría real; deducirlo del índice de columna falla en cuanto la cabina no es simétrica.
/// </param>
/// <param name="Price">Precio de esta butaca en la moneda del mapa. 0 = sin recargo.</param>
/// <param name="Features">
/// Rasgos que cambian lo que el pasajero recibe: <c>extra-legroom</c>, <c>exit-row</c>,
/// <c>bulkhead</c>, <c>recline-limited</c>. Lista abierta a propósito — es vocabulario del
/// proveedor, y un rasgo nuevo no debe obligar a desplegar el CMS.
/// </param>
public sealed record SeatMapSeat(
    string Id,
    string Column,
    string Status,
    string Position,
    decimal Price,
    IReadOnlyList<string>? Features = null);

/// <summary>
/// Una fila del mapa.
/// </summary>
/// <param name="Label">
/// El rótulo tal como se ve: <c>12</c> en una cabina, <c>A</c> en un teatro. Texto y no número
/// porque las filas reales se saltan valores (no hay fila 13 en media flota) y algunas llevan
/// letra.
/// </param>
/// <param name="ServiceClass">
/// La clase de servicio de la fila: <c>first</c> | <c>business</c> | <c>premium</c> |
/// <c>economy</c> en una cabina, o el nombre de la zona en un recinto. Vive en la FILA y no en
/// la butaca porque las secciones de una cabina son rangos contiguos de filas — es donde el
/// dato es verdad sin repetirse seis veces por fila.
/// </param>
/// <param name="Seats">Las butacas, en orden de columna.</param>
/// <param name="IsExitRow">
/// Fila de salida de emergencia. Se declara aparte de <see cref="SeatMapSeat.Features"/> porque
/// es una propiedad de la FILA con consecuencias regulatorias (edad mínima, sin equipaje en el
/// piso), no un rasgo cómodo de una butaca suelta.
/// </param>
public sealed record SeatMapRow(
    string Label,
    string ServiceClass,
    IReadOnlyList<SeatMapSeat> Seats,
    bool IsExitRow = false);

/// <summary>
/// El mapa de asientos completo que sirve un proveedor.
/// </summary>
/// <param name="Ref">La referencia con la que se pidió (matrícula, vuelo, función, recinto).</param>
/// <param name="Name">Cómo se le muestra al pasajero. Ej: <c>Airbus A320 · BOG → MDE</c>.</param>
/// <param name="Currency">Moneda de los precios. ISO 4217.</param>
/// <param name="AisleAfterColumns">
/// Después de qué columnas van los pasillos, en base 1 y en orden ascendente. Un <c>3-3</c>
/// trae <c>[3]</c>; un <c>3-3-3</c> trae <c>[3, 6]</c>; un <c>1-2-1</c> trae <c>[1, 3]</c>.
///
/// <para><b>Es una LISTA porque un widebody tiene dos pasillos.</b> Con un solo valor, un
/// <c>3-3-3</c> se dibujaba con el pasillo tras la C y nada entre F y G: el bloque derecho se
/// soldaba al central y la cabina se leía como un <c>3-6</c> que no existe en ningún avión.</para>
///
/// <para>Vacía, el componente parte la fila más ancha por la mitad — que acierta en un
/// narrowbody y en nada más. Dónde termina un bloque no se deduce de la cuenta de butacas, así
/// que un proveedor que conozca la cabina debería declararlo siempre.</para>
/// </param>
/// <param name="Rows">Las filas, en orden de proa a popa.</param>
public sealed record SeatMapLayout(
    string Ref,
    string Name,
    string Currency,
    IReadOnlyList<int> AisleAfterColumns,
    IReadOnlyList<SeatMapRow> Rows);

/// <summary>
/// De dónde sale el mapa de asientos. Es un proveedor <b>EXÓGENO</b>: el inventario de butacas
/// no se autora en el CMS.
/// </summary>
/// <remarks>
/// <b>Esta seam existe para separar dos cosas que se confundían.</b> El CMS configura el mapa
/// —qué cabina se muestra, cuántas butacas puede elegir alguien, cómo se ve—; el inventario
/// —qué butacas existen, cuáles están libres y a qué precio— lo publica quien lo conoce: el
/// sistema de la aerolínea, el del recinto, o un adapter que los emule. Autorar butacas en un
/// backoffice no es un modelo de contenido: es una hoja de cálculo que se desincroniza en el
/// primer vuelo.
///
/// <para><b>Stub-first</b>, como el resto del motor: el default emula una cabina real
/// (distribución <c>3-3</c> o <c>2-4-2</c>, secciones por clase, filas de salida, butacas
/// bloqueadas) para que la demo corra end-to-end; el adapter real se enchufa sin tocar ni el
/// CMS ni el componente.</para>
///
/// <para><b>El host resuelve el mapa en el SERVIDOR</b> y se lo entrega al componente ya
/// resuelto, que es como el elemento publicado espera recibirlo hoy. Eso no es solo comodidad:
/// mantiene fuera del navegador las credenciales del proveedor y evita exponer su API al
/// público.</para>
/// </remarks>
public interface ISeatMapProvider
{
    /// <summary>
    /// El mapa identificado por <paramref name="mapRef"/>, o <c>null</c> si el proveedor no lo
    /// conoce.
    /// </summary>
    /// <remarks>
    /// Devuelve null en vez de lanzar: una referencia que el proveedor no reconoce es un dato
    /// del editor, no una falla del sistema, y debe degradar la pantalla — no tumbarla.
    /// </remarks>
    Task<SeatMapLayout?> GetLayoutAsync(string mapRef, CancellationToken cancellationToken = default);
}
