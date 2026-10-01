namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-app-launcher&gt;</c>, el lanzador de las apps de cada dominio.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134: se nombra por lo que hace —presentar y lanzar las
/// apps— y es grande por dentro: búsqueda, tres filtros derivados de los datos, contador). Es la
/// funcionalidad del piloto de la ADR 0136 (#186): recibe su contenido —título, subtítulo y la lista
/// de apps— y <b>nada de microcopia</b>.</para>
///
/// <para><b>La microcopia sale del diccionario, sección <c>AppLauncher</c></b>, y la traduce el
/// elemento con <c>t()</c>. Antes eran 18 textos escritos a mano en el componente, y cinco de ellos
/// (<c>searchLabel</c>, <c>searchPlaceholder</c>, <c>ctaLabel</c>, <c>emptyLabel</c>,
/// <c>allFiltersLabel</c>) sólo se podían cambiar tecleando JSON en <c>configOverride</c>: el
/// editor escribiendo microcopia por instancia, la puerta contraria al modelo. Una funcionalidad no
/// recibe <c>configOverride</c> (ADR 0135 §6; <c>SolicitudSynHost.Para</c>), y medido el
/// 2026-09-30 ningún bloque lo usa. <c>Common.States</c> aporta «Próximamente» (el estado
/// <c>soon</c>, la misma intención que <c>Common.States.ComingSoon</c>).</para>
///
/// <para><b>Los nombres son los que el elemento lee</b>: <c>title</c> y <c>subtitle</c> salen de
/// los alias <c>heading</c> y <c>subheading</c> del ElementType; <c>apps</c> deja de viajar como
/// el TEXTO del TextArea y viaja como lista tipada.</para>
/// </remarks>
[ElementoSynHost("app-launcher", TipoDeColocable.Funcionalidad, Diccionario = ["AppLauncher", "Common.States"])]
public sealed record AppLauncherProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Title,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subtitle,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<AppDelLanzador>? Apps);

/// <summary>
/// Una app del lanzador: su identidad, cómo se presenta, de qué se filtra y a dónde lleva.
/// </summary>
/// <remarks>
/// <c>Status</c> y <c>DemoMode</c> viajan como el editor los escribió: el elemento los normaliza
/// (<c>live|beta|soon</c>, <c>embed|deeplink</c>) y su vocabulario vive en el UI, no se copia acá.
/// <c>Capabilities</c> acepta en el JSON una lista o un texto separado por comas; viaja como lista.
/// </remarks>
public sealed record AppDelLanzador(
    string Name,
    string? Id = null,
    string? Tagline = null,
    string? Icon = null,
    string? Status = null,
    string? Industry = null,
    string? Persona = null,
    IReadOnlyList<string>? Capabilities = null,
    string? Url = null,
    string? DemoMode = null);
