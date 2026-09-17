namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Resuelve —y exige— la carpeta del disco de la que sale el CDN cuando el registry corre en
/// <c>Mode=FileSystem</c> y cuando el sitio monta esos ficheros en <c>Synergos:LocalCdn</c>.
/// </summary>
/// <remarks>
/// <para><b>Existe por dos defectos de la misma familia, y los dos están medidos</b> (#132).</para>
///
/// <para><b>Uno: la ruta por defecto era de UNA máquina.</b>
/// <c>appsettings.Development.json</c> traía <c>C:\LOCAL_CDN</c> en los dos sitios, así que un
/// clon limpio —CI, contenedor, el portátil de cualquiera que no sea el arquitecto— arrancaba en
/// <c>FileSystem</c> apuntando a una carpeta que no existe. Es la misma enfermedad que el repo
/// hermano ya vigila con su gate <c>rutas-hermanas</c> («ninguna lectura cae por defecto a una
/// ruta de una sola máquina»), sin nadie vigilándola de este lado.</para>
///
/// <para><b>Dos: eso NO fallaba — servía una página muerta.</b> Medido el 2026-09-16 con la misma
/// portada sembrada, cambiando sólo <c>LocalPath</c>:</para>
/// <list type="bullet">
///   <item>hacia <c>Synergos.UI/public</c> → <b>21 577 bytes</b>, import map de <b>23</b>
///   entradas, <b>2</b> <c>&lt;script type="module"&gt;</c>, <c>/_health</c> 200.</item>
///   <item>hacia <c>C:\LOCAL_CDN</c> → <b>19 785 bytes</b>, <b>NO hay import map</b>, <b>0</b>
///   <c>&lt;script type="module"&gt;</c>, <c>/_health</c> 503.</item>
/// </list>
/// <para>Los dos <c>&lt;synergos-*&gt;</c> salían pintados por el SSR en los dos casos. O sea el
/// defecto #126 exacto —200, la página entera, y nada interactivo— en el modo que se usa en la
/// máquina de quien desarrolla. Son los mismos dos números que midió <c>humo-conectado</c> para el
/// nombre de variable equivocado, lo que confirma que lo que se pierde es lo mismo.</para>
///
/// <para><b>Y no es una degradación transitoria: es permanente.</b>
/// <c>FileSystemBundleRegistryClient.InitialLoad</c> ve la carpeta ausente, escribe un
/// <c>LogWarning</c> y <b>vuelve sin cargar nada y sin dejar vigilante</b>; el
/// <c>FileSystemWatcher</c> sólo se engancha si la carpeta estaba ahí. No hay reintento, así que
/// el estado del disco EN EL ARRANQUE decide para siempre — y por eso lo correcto es decirlo al
/// arrancar, como <see cref="BundleRegistrySettings.ExigirUrlPublicaAbsoluta"/> hace con la URL
/// del modo <c>Http</c> (#56).</para>
///
/// <para><b>Por qué se LANZA en vez de caer a <c>Stub</c>.</b> Porque <c>Mode=FileSystem</c> lo
/// escribió alguien: es una afirmación, no un descubrimiento. Caer a <c>Stub</c> en silencio deja
/// exactamente la página de 19 785 bytes que se ve igual que la buena, y el <c>/_health</c> en 503
/// no lo mira nadie en su portátil. La salida para quien quiera levantar el CMS sin construir el
/// repo hermano es una línea —<c>Synergos:BundleRegistry:Mode=Stub</c>— y el mensaje la nombra.
/// Lanzar sin decir cómo salir sí sería un peaje.</para>
/// </remarks>
public static class RaizDelCdnLocal
{
    /// <summary>
    /// Devuelve la ruta absoluta de la raíz del CDN. Una ruta RELATIVA se resuelve contra la raíz
    /// de contenido de la aplicación, nunca contra el directorio de trabajo.
    /// </summary>
    /// <remarks>
    /// <para><b>El relativo es lo que permite que el default deje de ser de una máquina.</b> La
    /// disposición que documenta <c>docs/onboarding/arrancar-los-dos-arboles.md</c> es de repos
    /// hermanos, así que desde <c>Synergos.CMS.Web/</c> el CDN construido está en
    /// <c>../../Synergos.UI/public</c> — y eso es cierto en Windows, en Linux y en CI.</para>
    ///
    /// <para><b>Contra la raíz de CONTENIDO y no contra el <c>cwd</c></b>, que es la diferencia
    /// entre que funcione siempre y que funcione cuando se arranca desde la carpeta correcta:
    /// <c>dotnet run</c> deja el <c>cwd</c> en el proyecto y <c>dotnet exec bin/…/Web.dll</c> lo
    /// deja donde se tecleó. Un default que depende de desde dónde se lanzó el proceso es el mismo
    /// defecto con otra cara.</para>
    ///
    /// <para><b>Y por eso se normaliza ANTES de que nadie enlace opciones</b>: el composer decide
    /// si lanzar leyendo la configuración, y el cliente la lee por <c>IOptions</c>. Si se
    /// resolviera en uno de los dos, el otro seguiría viendo el relativo y los dos hablarían de
    /// carpetas distintas.</para>
    /// </remarks>
    /// <param name="localPath">Lo que trae la configuración, tal cual.</param>
    /// <param name="raizDelContenido">La raíz de contenido de la aplicación.</param>
    /// <returns>La ruta absoluta, o cadena vacía si no hay nada configurado.</returns>
    public static string Resolver(string? localPath, string raizDelContenido)
    {
        var crudo = localPath?.Trim();
        if (string.IsNullOrWhiteSpace(crudo)) return string.Empty;
        if (Path.IsPathRooted(crudo)) return Path.GetFullPath(crudo);
        if (string.IsNullOrWhiteSpace(raizDelContenido)) return crudo;
        return Path.GetFullPath(Path.Combine(raizDelContenido, crudo));
    }

    /// <summary>
    /// Exige que la raíz ya resuelta exista <b>y traiga el registry</b>. Lanza <b>al cablear</b>.
    /// </summary>
    /// <remarks>
    /// <para>Recibe la ruta YA resuelta —absoluta— y no la que escribió el operador, a propósito:
    /// con un default relativo, lo que hace falta saber para arreglarlo es contra qué se resolvió,
    /// que es lo que no se puede deducir leyendo el <c>appsettings</c>.</para>
    ///
    /// <para><b>Y se mira el registry, no sólo la carpeta.</b> «El hermano clonado pero sin
    /// construir» y «el hermano construido» se distinguen por ese fichero; una carpeta vacía pasa
    /// la comprobación de existencia y deja exactamente la misma página muerta. Los dos casos
    /// llevan mensajes distintos porque el remedio es distinto.</para>
    /// </remarks>
    /// <param name="raizResuelta">Lo que devolvió <see cref="Resolver"/>.</param>
    /// <param name="bundlesNamespace">Subcarpeta donde viven los bundles.</param>
    /// <param name="registryFileName">Nombre del registry global.</param>
    /// <exception cref="InvalidOperationException">Si falta la carpeta o el registry.</exception>
    public static void Exigir(string? raizResuelta, string bundlesNamespace, string registryFileName)
    {
        var resuelto = raizResuelta?.Trim();

        if (string.IsNullOrWhiteSpace(resuelto) || !Directory.Exists(resuelto))
        {
            throw new InvalidOperationException(
                "Synergos:BundleRegistry:Mode=FileSystem exige que "
                + "Synergos:BundleRegistry:LocalPath apunte a una carpeta que exista. Resuelve a "
                + $"«{(string.IsNullOrWhiteSpace(resuelto) ? "(vacío)" : resuelto)}», y ahí no hay "
                + "nada." + ComoSalir);
        }

        var registry = Path.Combine(resuelto, bundlesNamespace, registryFileName);
        if (!File.Exists(registry))
        {
            throw new InvalidOperationException(
                $"Synergos:BundleRegistry:LocalPath apunta a «{resuelto}», que existe, y ahí no "
                + $"está «{Path.Combine(bundlesNamespace, registryFileName)}». Una carpeta vacía "
                + "no es un CDN: es el repo hermano clonado y sin construir." + ComoSalir);
        }
    }

    /// <summary>
    /// Las dos salidas, y por qué no se cae a <c>Stub</c> solo. Va en los dos mensajes porque los
    /// dos dejan a alguien parado: lanzar sin decir cómo salir sí sería un peaje.
    /// </summary>
    private const string ComoSalir =
        "\n  · Si querés el CDN: construí el repo hermano (npm run build:cdn en Synergos.UI) y "
        + "dejá el default ../../Synergos.UI/public.\n"
        + "  · Si querés levantar el CMS SIN CDN: Synergos:BundleRegistry:Mode=Stub.\n"
        + "No se cae a Stub solo a propósito: medido, el sitio serviría la portada en 200 con el "
        + "SSR entero, sin un solo <script type=\"module\"> y sin import map — o sea idéntica a "
        + "una que funciona, y muerta (defecto #126). El camino completo está en "
        + "Synergos.CMS.Web/docs/onboarding/arrancar-los-dos-arboles.md.";
}
