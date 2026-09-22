namespace Synergos.CMS.Interfaces;

/// <summary>
/// Un equipo que se alquila: el objeto central del vertical Alquiler (eje 1 del doc 12).
/// </summary>
/// <remarks>
/// <para><b>El precio es <c>decimal</c> y llega desde un <c>Umbraco.Integer</c>, no desde un
/// TextBox.</b> Ésa es la lección del #123 aplicada por construcción en vez de por parseo
/// defensivo: <c>"49.000"</c> con <c>NumberStyles.Number</c> e <c>InvariantCulture</c> da
/// <b>49</b>, un precio plausible equivocado por 1000× que ninguna guarda de «&gt; 0» ve. Con el
/// editor de enteros de Umbraco ese valor no se puede teclear.</para>
///
/// <para><b><see cref="Deposit"/> no es un cobro y por eso tiene nombre propio.</b> Es lo que se
/// RETIENE mientras el equipo está fuera: se autoriza al reservar y se <i>anula</i> al devolver
/// si no hay daño. Ningún otro vertical del repo mueve plata así — los cuatro flujos existentes
/// autorizan para capturar.</para>
/// </remarks>
/// <param name="Id">El slug que escribió el editor. Es lo que viaja como <c>subjectId</c>.</param>
/// <param name="Name">Nombre visible del equipo.</param>
/// <param name="Category">Familia, para el filtro del listado.</param>
/// <param name="Summary">Una o dos líneas para la tarjeta.</param>
/// <param name="Description">El cuerpo de la ficha.</param>
/// <param name="CoverUrl">Foto principal, o null si el editor no la puso.</param>
/// <param name="GalleryUrls">Fotos del estado real — las que alguien mira al discutir un daño.</param>
/// <param name="Units">Cuántas unidades hay. Es el cupo de la ventana (<c>Resource.Capacity</c>).</param>
/// <param name="DailyRate">Tarifa base por día, en pesos enteros.</param>
/// <param name="Deposit">La garantía que se retiene. Cero significa sin garantía.</param>
/// <param name="MinDays">Alquiler mínimo en días; nunca menor que 1.</param>
/// <param name="MaxDays">Alquiler máximo en días, ya acotado por el tope del despliegue.</param>
/// <param name="Includes">Lo que va con el equipo.</param>
/// <param name="Requirements">Lo que tiene que cumplir quien lo alquila.</param>
/// <param name="Rates">Tramos de descuento por duración, ordenados por <c>MinDays</c>.</param>
/// <param name="Specs">Las filas de la ficha técnica.</param>
public sealed record RentalEquipment(
    string Id,
    string Name,
    string Category,
    string Summary,
    string Description,
    string? CoverUrl,
    IReadOnlyList<string> GalleryUrls,
    int Units,
    decimal DailyRate,
    decimal Deposit,
    int MinDays,
    int MaxDays,
    IReadOnlyList<string> Includes,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<EquipmentRate> Rates,
    IReadOnlyList<EquipmentSpec> Specs);

/// <summary>Un tramo de tarifa: a partir de cuántos días aplica y cuánto vale el día ahí.</summary>
/// <param name="Code">Identificador corto dentro del equipo.</param>
/// <param name="Label">Nombre visible del tramo.</param>
/// <param name="MinDays">Desde cuántos días aplica.</param>
/// <param name="PerDay">Cuánto vale UN día dentro del tramo.</param>
/// <param name="Description">Qué cubre el tramo; vacío oculta la fila.</param>
public sealed record EquipmentRate(string Code, string Label, int MinDays, decimal PerDay, string Description);

/// <summary>Una fila de la ficha técnica.</summary>
/// <param name="Label">Qué se mide.</param>
/// <param name="Value">El dato con su unidad.</param>
public sealed record EquipmentSpec(string Label, string Value);
