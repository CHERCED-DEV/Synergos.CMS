namespace Synergos.CMS.Web.Services;

/// <summary>
/// Las claves del Layout Composer que el código server-side necesita nombrar.
/// </summary>
/// <remarks>
/// <para>Un area de Block Grid se identifica por su <b>Key</b>, no por su alias: el JSON que
/// Umbraco guarda en la propiedad <c>sections</c> lleva el GUID. Ese GUID lo declara el
/// DataType <c>DTBlockGridSections</c> y es inmutable por la regla de uSync (una Key que
/// cambia es un area nueva, no la misma renombrada).</para>
///
/// <para><b>Por qué una constante compartida y no una por servicio.</b> Estaba escrita dos
/// veces —<c>SynergosIdentitySeeder</c> y <c>DevContentFiller</c>— y el tercer consumidor
/// (<see cref="StarterPortadaSeeder"/>) obligaba a elegir entre copiarla otra vez o
/// unificarla. Tres copias de un GUID que nadie cruza contra el schema es cómo una se queda
/// atrás sin que nada falle: el bloque hijo se guardaría en un area que no existe y la
/// página publicaría <b>vacía</b>, sin error.</para>
///
/// <para>Hoy quedan dos consumidores: <c>SynergosIdentitySeeder</c> dejó de componer cuerpo
/// al borrarse sus <c>Build*Sections</c> muertas (#119), y la constante se queda compartida
/// porque los dos que restan la siguen necesitando.</para>
/// </remarks>
internal static class LayoutComposerKeys
{
    /// <summary>
    /// Area <c>sectionContent</c> de <c>elementLayoutSection</c> — el único area que ese
    /// preset declara, y donde va todo bloque de contenido dentro de una sección.
    /// </summary>
    /// <remarks>
    /// Fuente: <c>uSync/v9/DataTypes/DTBlockGridSections.config</c>, bloque cuyo
    /// <c>contentElementTypeKey</c> es el de <c>elementLayoutSection</c>.
    /// </remarks>
    public static readonly Guid SectionContentArea = new("3525d41c-ae84-47ac-9297-2148f6a4aae8");
}
