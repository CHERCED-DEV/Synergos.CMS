namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Exige el par certificado + llave del endpoint HTTPS de desarrollo, y dice cómo crearlo (#137).
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> <c>appsettings.Development.json</c> apuntaba a
/// <c>C:\LOCAL_CDN\synergos-dev.crt</c> y <c>.key</c> — la misma carpeta de UNA máquina que el
/// #132 acababa de sacar del CDN, dos secciones más arriba en el mismo fichero. Y el gate de
/// entonces (<c>RaizDelCdnTests</c>) perseguía <b>las claves del <c>BundleRegistry</c></b>, no el
/// literal, así que las de Kestrel se le escaparon enteras.</para>
///
/// <para><b>Y había una segunda mitad peor que la primera: nada en el repo creaba ese
/// certificado.</b> Medido con un <c>grep</c> de <c>synergos-dev</c>, <c>dev-certs</c> y
/// <c>openssl</c> sobre todo el árbol, las únicas dos menciones eran las que lo CONSUMEN. Ni
/// script, ni paso del onboarding, ni nota. O sea que un clon nuevo se encontraba con Kestrel
/// pidiendo un fichero inexistente, con un mensaje que no dice cómo crearlo, sobre una dependencia
/// que ningún documento nombra.</para>
///
/// <para><b>En qué se diferencia del #132, y por eso el arreglo no es el mismo.</b> Aquél
/// <b>degradaba en silencio</b> —servía la portada en 200, sin import map y muerta—. Éste
/// <b>falla a la vista</b>: Kestrel lanza al arrancar. Así que lo que hay que arreglar no es «que
/// avise», es <b>qué avisa</b>: el mensaje de .NET dice «no se encontró el fichero» y el de acá
/// dice qué teclear.</para>
///
/// <para><b>Por qué no se apaga el HTTPS y punto.</b> Era la salida más corta y deja el
/// <c>UmbracoApplicationUrl</c>, el <c>launchUrl</c> del perfil y el
/// <c>Notifications:PublicBaseUrl</c> hablando de un esquema que el host no sirve — tres sitios
/// mintiendo para no arreglar uno. El precedente del repo es el del #132: lo que reemplaza a la
/// ruta de una máquina es <b>una relativa al repo</b>, no quitar la función.</para>
///
/// <para><b>La llave privada NO entra al repo.</b> La carpeta está en <c>.gitignore</c> y hay gate
/// que lo comprueba: un secreto que depende de que nadie escriba <c>git add -A</c> no está
/// protegido, está de suerte.</para>
/// </remarks>
public static class CertificadoDeDesarrollo
{
    /// <summary>Las dos claves de Kestrel que nombran el par, en orden.</summary>
    /// <remarks>
    /// Van declaradas acá y no en <c>Program.cs</c> porque las leen dos: quien las resuelve al
    /// arrancar y el gate que comprueba que ninguna nombre una máquina.
    /// </remarks>
    public static readonly string[] Claves =
    [
        "Kestrel:Endpoints:Https:Certificate:Path",
        "Kestrel:Endpoints:Https:Certificate:KeyPath",
    ];

    /// <summary>
    /// Exige que el par YA resuelto exista. Lanza <b>al cablear</b>, no en la primera petición.
    /// </summary>
    /// <remarks>
    /// <para>Recibe las rutas <b>ya resueltas</b> —absolutas— y no lo que escribió el operador, por
    /// la misma razón que <see cref="RaizDelCdnLocal.Exigir"/>: con un default relativo, lo que
    /// hace falta saber para arreglarlo es <b>contra qué se resolvió</b>, y eso no se deduce
    /// leyendo el <c>appsettings</c>.</para>
    ///
    /// <para><b>Se comprueban las DOS</b>, y no sólo la primera. Kestrel en modo PEM necesita el
    /// par completo; con el <c>.crt</c> presente y la <c>.key</c> ausente el mensaje de .NET habla
    /// del certificado, que manda a mirar el fichero que sí está.</para>
    /// </remarks>
    /// <param name="certificadoResuelto">La ruta absoluta del <c>.crt</c>.</param>
    /// <param name="llaveResuelta">La ruta absoluta del <c>.key</c>.</param>
    /// <exception cref="InvalidOperationException">Si falta cualquiera de los dos.</exception>
    public static void Exigir(string? certificadoResuelto, string? llaveResuelta)
    {
        var faltan = new[] { ("certificado", certificadoResuelto), ("llave", llaveResuelta) }
            .Where(p => string.IsNullOrWhiteSpace(p.Item2) || !File.Exists(p.Item2))
            .ToList();

        if (faltan.Count == 0) return;

        var detalle = string.Join(" · ", faltan.Select(
            p => $"{p.Item1}: «{(string.IsNullOrWhiteSpace(p.Item2) ? "(vacío)" : p.Item2)}»"));

        throw new InvalidOperationException(
            "El endpoint HTTPS de desarrollo necesita un par certificado + llave que no está en "
            + $"disco. Falta — {detalle}." + ComoSalir);
    }

    /// <summary>Las dos salidas, nombradas. Lanzar sin decir cómo salir es un peaje (#132).</summary>
    private const string ComoSalir =
        "\n  · Crealo una vez: `node tools/cert-dev.mjs` (lo deja en `certs/`, que está en "
        + ".gitignore — la llave privada NO entra al repo).\n"
        + "  · O levantá el CMS sólo por HTTP quitando la sección Kestrel:Endpoints:Https de "
        + "appsettings.Development.json.\n"
        + "El camino completo está en Synergos.CMS.Web/docs/onboarding/arrancar-los-dos-arboles.md.";
}
