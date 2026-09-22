namespace Synergos.CMS.Web.Services;

/// <summary>
/// Qué implementación de un elemento del CDN se sirve, cuando el registry trae más de una.
/// </summary>
/// <remarks>
/// <para><b>Vive acá porque estaba escrito DOS veces</b> (#131):
/// <c>HttpBundleRegistryClient.ElegirFramework</c> y
/// <c>FileSystemBundleRegistryClient.ChooseFramework</c>, mismo sujeto —qué bundle recibe el
/// visitante— y misma política, o sea la misma cosa (<c>feedback_the_same_algorithm_is_not_the_same_thing</c>).
/// El ticket nombraba una sola; la otra apareció al buscar.</para>
///
/// <para><b>El defecto era el desempate.</b> Las dos preferían el <c>DefaultFramework</c> y, si no
/// estaba, tomaban <c>Keys.FirstOrDefault()</c> — o sea el orden de inserción del
/// <c>Dictionary</c>, que sale del orden en que <c>System.Text.Json</c> leyó el registry, que sale
/// del orden en que el repo hermano lo escribió al publicar. Republicar cambiando el orden cambia
/// qué bundle recibe el visitante, <b>sin que nada falle y sin que nadie lo haya decidido</b>. Y
/// el síntoma no se lee como «se eligió mal el framework»: se lee como «ese elemento se comporta
/// distinto», que manda a depurar el elemento.</para>
///
/// <para><b>Y una de las dos copias lo afirmaba al revés</b>: <i>«orden no garantizado pero
/// determinista por StringComparer del dict construido»</i>. Es falso — el comparador decide cómo
/// se BUSCA, no cómo se enumera. Un comentario que promete la propiedad que falta es el aviso de
/// que nadie la comprobó (<c>feedback_gethashcode_is_not_a_seed</c>).</para>
///
/// <para><b>Lo que NO se hace, y el ticket tenía razón: no se elige un ORDEN DE PREFERENCIA.</b>
/// Cuál framework es mejor lo decide quien publica, y el repo hermano ya lo decidió — y no como
/// se esperaba: un elemento con dos implementaciones es un <b>escaparate declarado</b>
/// (<c>SHOWCASE_MULTIPLATAFORMA</c>, <c>Synergos.UI#59</c>/#64), no una forma de producto. <i>«Un
/// badge de Preact que fuera producto sería otro elemento, con su nombre y su DocType.»</i> O sea
/// que en producto este desempate no debería ocurrir nunca.</para>
///
/// <para><b>Lo que sí se hace, porque el CMS lo debe pase lo que pase: que sea ESTABLE y que se
/// NOTE.</b> Orden ordinal —arbitrario y dicho, no una preferencia— y el llamador avisa. La misma
/// entrada da la misma salida aunque el registry se republique con las claves en otro orden, que
/// es una propiedad del cliente y no una política del catálogo; y un desempate que ocurre es por
/// definición excepcional, así que se registra en vez de pasar callado.</para>
///
/// <para><b>Medido al escribirlo</b> contra el registry publicado del hermano: de todos los
/// elementos que sirve, <b>uno solo</b> —<c>badge</c>— trae dos implementaciones
/// (<c>angular</c> y <c>preact</c>); el resto trae <c>angular</c> sola. O sea que el disparador
/// que el #131 escribió («el día que un elemento traiga más de una implementación») <b>ya
/// ocurrió</b>; lo que todavía no ocurre es el daño, que necesita además que ninguna de las dos
/// sea la de por defecto. (La cuenta total de elementos del CDN no se copia acá: vive en
/// <c>CLAUDE.md</c> §11 con el comando para medirla, y hay gate.)</para>
/// </remarks>
internal static class EleccionDeImplementacion
{
    /// <summary>
    /// El framework con el que se sirve <paramref name="implementaciones"/>, y si hubo que
    /// desempatar de forma arbitraria.
    /// </summary>
    /// <returns>
    /// <c>Framework</c> es <c>null</c> si no hay ninguna implementación. <c>Desempatado</c> es
    /// <c>true</c> sólo cuando había MÁS DE UNA y ninguna era la de por defecto — que es el único
    /// caso en que la elección no la tomó nadie.
    /// </returns>
    internal static (string? Framework, bool Desempatado) Elegir(
        IReadOnlyDictionary<string, Dictionary<string, string>>? implementaciones,
        string porDefecto)
    {
        if (implementaciones is null || implementaciones.Count == 0) return (null, false);
        if (implementaciones.ContainsKey(porDefecto)) return (porDefecto, false);

        // Ordinal y no el orden del diccionario: lo segundo lo decide quién escribió el JSON.
        var elegido = implementaciones.Keys.OrderBy(k => k, StringComparer.Ordinal).First();
        return (elegido, implementaciones.Count > 1);
    }
}
