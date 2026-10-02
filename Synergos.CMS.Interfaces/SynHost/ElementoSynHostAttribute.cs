namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Ata un <c>record</c> al elemento publicado que lo recibe: el record declara EXACTAMENTE lo que
/// viaja en el atributo <c>config</c> de <c>&lt;synergos-{nombre}&gt;</c> (ADR 0135).
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> Cada vista SynHost armaba a mano un diccionario libre de claves y
/// nada obligaba a que fueran las que lee el elemento: 43 colocables tiraban al hidratar lo que el
/// editor escribió (D1). <c>kpi-card</c> mandaba <c>kpiLabel</c> y el elemento leía
/// <c>label</c>. Con el record, la forma del payload deja de decidirse en Razor: la escribe un tipo
/// que compila, del que sale el contrato de <c>docs/contracts/elementos-synhost.json</c> y, de
/// ese, el tipo TS contra el que se tipa el sanitizador del elemento.</para>
///
/// <para><b>Los nombres los elige lo que el elemento LEE</b> (ADR 0083): el record es el sitio
/// donde quedan escritos para que los comprueben los dos compiladores, no una fuente nueva que
/// los invente. uSync sigue mandando sobre lo que el editor EDITA (ADR 0008); el resolver de cada
/// elemento traduce de lo uno a lo otro.</para>
///
/// <para><b>La identidad es el <c>name</c> del registry</b>, no el alias del ElementType: es la
/// clave con la que el emitter resuelve el bundle y de la que sale el tag. Un nombre que diverge
/// deja el elemento sin hidratar, sin error.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ElementoSynHostAttribute : Attribute
{
    public ElementoSynHostAttribute(string nombre, TipoDeColocable tipo)
    {
        Nombre = nombre;
        Tipo = tipo;
    }

    /// <summary>El <c>name</c> del elemento en el registry del CDN (<c>kpi-card</c>).</summary>
    public string Nombre { get; }

    /// <summary>Funcionalidad o pieza (ADR 0134): de eso depende qué le llega.</summary>
    public TipoDeColocable Tipo { get; }

    /// <summary>
    /// Las secciones del diccionario de uSync que el elemento usa (<c>Synhost.Kpi</c>): el prefijo
    /// de sus claves. Vacío si no usa ninguna. Un gate comprueba que cada una exista.
    /// </summary>
    public string[] Diccionario { get; set; } = [];

    /// <summary>
    /// Los selectores del ElementType que eligen DATOS, no vocabulario del elemento: la fuente de
    /// un listado elige de dónde salen sus filas (#196, tanda D). El gate de vocabulario (#181) no
    /// los sondea —cambiarlos cambia el contenido entero, no una clave—, y el contrato los declara
    /// sin campo. Un gate comprueba que cada uno exista en el ElementType y que el resolver lo lea.
    /// </summary>
    public string[] SelectoresDeDatos { get; set; } = [];
}

/// <summary>
/// Los dos tipos de cosa que el editor coloca (ADR 0134, Aceptada).
/// </summary>
public enum TipoDeColocable
{
    /// <summary>Nombrada por lo que hace, grande por dentro; recibe sólo cableado.</summary>
    Funcionalidad,

    /// <summary>Colocable suelta; recibe contenido y decisiones del editor y monta su pieza del DS.</summary>
    Pieza,
}

/// <summary>
/// Declara de dónde sale un campo del record: si es CONTENIDO que el editor escribe o una
/// DECISIÓN que el editor toma (variante, mostrar/ocultar, valor inicial).
/// </summary>
/// <remarks>
/// Se exige en todo campo de un record <see cref="ElementoSynHostAttribute"/> (hay gate): es la
/// clasificación que el informe 16 tuvo que hacer a mano clave por clave, y la que la fábrica
/// necesita para contestar «¿qué dato pide este elemento, y es el mío?» sin abrir una vista.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CampoSynHostAttribute : Attribute
{
    public CampoSynHostAttribute(OrigenDelCampo origen) => Origen = origen;

    /// <summary>De dónde sale el valor.</summary>
    public OrigenDelCampo Origen { get; }
}

/// <summary>
/// De dónde sale un campo: contenido o decisión del editor (informe 16 §3: clases C y D), o la
/// configuración de negocio de la funcionalidad, que el editor no toca (ADR 0137).
/// </summary>
public enum OrigenDelCampo
{
    /// <summary>Lo que el editor escribe: textos, cifras, listas.</summary>
    Contenido,

    /// <summary>Lo que el editor elige: variantes, interruptores, el valor inicial de un control.</summary>
    Decision,

    /// <summary>
    /// La configuración de negocio de una funcionalidad: sale de su sección
    /// <c>Synergos:Features:&lt;X&gt;</c> del despliegue, con override por siteRoot, y el editor no la
    /// ve (ADR 0137). Sólo una funcionalidad lleva campos de este origen (ADR 0134).
    /// </summary>
    Negocio,

    /// <summary>
    /// De quién son los datos que se muestran: lo decide el servidor para la petición, no el editor
    /// (ADR 0137 §5, «de runtime»). Hoy es una identidad de demo; #197 la pasa al miembro de la
    /// sesión.
    /// </summary>
    Sesion,
}
