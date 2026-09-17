namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Resuelve una ruta de configuración contra la raíz de CONTENIDO de la aplicación.
/// </summary>
/// <remarks>
/// <para><b>Sale de promover al SEGUNDO consumidor</b> (CLAUDE.md §17). La resolución vivía dentro
/// de <see cref="RaizDelCdnLocal"/> desde el #132, con un solo cliente; al arreglar el #137
/// —el certificado de desarrollo y el buzón de correo apuntaban a
/// <c>C:\LOCAL_CDN</c> y a <c>C:\Users\HITMA\…</c>— hicieron falta otros dos. Tragarlos dentro de
/// una clase que se llama «raíz del CDN local» habría sido peor que la copia: el día que el CDN
/// necesite otra política, cambiaría la del certificado sin que nadie lo pidiera
/// (<c>feedback_the_same_algorithm_is_not_the_same_thing</c>).</para>
///
/// <para><b>Lo que se promueve es el ALGORITMO, no la política.</b> Acá vive «una ruta relativa se
/// resuelve contra la raíz de contenido», y nada más. Qué hacer cuando falta
/// —lanzar, caer a otro modo, avisar— se queda en cada consumidor, porque no es lo mismo: sin CDN
/// la página sale muerta y en silencio (#126), y sin certificado Kestrel no levanta el endpoint.</para>
/// </remarks>
public static class RutaRelativaAlContenido
{
    /// <summary>
    /// La ruta absoluta. Una RELATIVA se resuelve contra <paramref name="raizDelContenido"/>,
    /// nunca contra el directorio de trabajo.
    /// </summary>
    /// <remarks>
    /// <para><b>Contra la raíz de CONTENIDO y no contra el <c>cwd</c></b>, que es la diferencia
    /// entre que funcione siempre y que funcione cuando se arranca desde la carpeta correcta:
    /// <c>dotnet run</c> deja el <c>cwd</c> en el proyecto y <c>dotnet exec bin/…/Web.dll</c> lo
    /// deja donde se tecleó. Un default que depende de desde dónde se lanzó el proceso es el mismo
    /// defecto con otra cara.</para>
    /// </remarks>
    /// <param name="ruta">Lo que trae la configuración, tal cual.</param>
    /// <param name="raizDelContenido">La raíz de contenido de la aplicación.</param>
    /// <returns>La ruta absoluta, o cadena vacía si no hay nada configurado.</returns>
    public static string Resolver(string? ruta, string raizDelContenido)
    {
        var crudo = ruta?.Trim();
        if (string.IsNullOrWhiteSpace(crudo)) return string.Empty;
        if (Path.IsPathRooted(crudo)) return Path.GetFullPath(crudo);
        if (string.IsNullOrWhiteSpace(raizDelContenido)) return crudo;
        return Path.GetFullPath(Path.Combine(raizDelContenido, crudo));
    }
}
