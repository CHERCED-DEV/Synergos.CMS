namespace Synergos.CMS.Web.Services;

/// <summary>
/// Qué implementación de un elemento se le sirve al visitante cuando el registry declara varias
/// (#131).
/// </summary>
/// <remarks>
/// <para><b>El defecto.</b> Los dos clientes del registry elegían
/// <c>Implementations.Keys.FirstOrDefault()</c> cuando el <c>DefaultFramework</c> no estaba
/// entre las implementaciones — o sea el orden de inserción del <c>Dictionary</c>, que sale del
/// orden en que <c>System.Text.Json</c> leyó el JSON, que sale del orden en que el repo hermano
/// escribió el registry al publicar. <b>Republicar cambiaba qué bundle recibe el visitante, sin
/// que nada fallara y sin que nadie hubiera decidido nada</b> — y el síntoma no se lee como «se
/// eligió mal el framework», se lee como «ese elemento se comporta distinto», que manda a
/// depurar el elemento.</para>
///
/// <para><b>Estaba escrito DOS veces y el ticket nombraba una</b>
/// (<c>HttpBundleRegistryClient.ElegirFramework</c> y
/// <c>FileSystemBundleRegistryClient.ChooseFramework</c>): mismo sujeto —qué bundle recibe el
/// visitante— y misma política, o sea la misma cosa. Y la segunda copia <b>afirmaba justo la
/// propiedad que le faltaba</b>: «orden no garantizado pero determinista por StringComparer del
/// dict construido». El comparador decide cómo se BUSCA, no cómo se ENUMERA. Es
/// <c>feedback_gethashcode_is_not_a_seed</c>: un comentario que promete la propiedad que falta
/// es el aviso de que nadie la comprobó.</para>
///
/// <para><b>Lo que esto NO hace: elegir cuál framework es mejor.</b> Esa decisión es del repo
/// hermano —es quien publica— y ya la tomó, y no en ninguna de las dos formas que el ticket
/// planteaba: no fue «un elemento, un framework» ni «N implementaciones con política», fue
/// <b>un elemento un framework para PRODUCTO, más un censo declarado para escaparates</b>
/// (<c>SHOWCASE_MULTIPLATAFORMA</c>: «un <c>badge</c> de Preact que fuera producto sería otro
/// elemento, con su nombre y su DocType»). Eso cambia qué le toca a este lado: con la primera
/// forma lo correcto era <b>fallar</b> al ver dos; con un escaparate declarado, fallar rompería
/// justo lo que el otro árbol decidió permitir.</para>
///
/// <para><b>Así que se separan las dos preguntas</b>: «¿cuál es mejor?» es del hermano y no se
/// toca; <b>«¿la misma entrada da la misma salida?» es de este cliente, pase lo que pase</b>. El
/// desempate es <b>ORDINAL</b> —arbitrario, y dicho como arbitrario en vez de disfrazado de
/// preferencia— y quien lo usa <b>avisa</b>, porque un desempate que ocurre es por definición
/// excepcional: en producto no debería pasar nunca.</para>
///
/// <para><b>El disparador ya llegó, medido contra el artefacto y no contra la guía.</b> El
/// registry CONSTRUIDO del hermano trae hoy <b>un</b> elemento con dos implementaciones
/// —<c>badge</c>, en <c>angular</c> y <c>preact</c>— y el resto con una. (Cuántos son en total
/// no se escribe acá: es la única cifra que este repo no puede cruzar contra su disco, vive en
/// <c>CLAUDE.md</c> §11 con su comando al lado, y hay gate que impide copiarla.) Lo que todavía
/// no ocurre es el daño, que necesita una condición más que el disparador no capturaba: que
/// <b>ninguna</b> de las implementaciones sea la de por defecto. Con <c>angular</c> entre las
/// dos gana el default y este desempate no se ejecuta. Vale la pena anotarlo — el disparador
/// estaba escrito un poco más ancho que el daño, y eso es lo que permite arreglarlo sin
/// prisa.</para>
/// </remarks>
public static class EleccionDeImplementacion
{
    /// <summary>
    /// El framework cuyo bundle se sirve, o <c>null</c> si el elemento no declara ninguno.
    /// </summary>
    /// <remarks>
    /// <para><paramref name="desempatado"/> dice si hizo falta romper un empate, o sea si el
    /// <paramref name="porDefecto"/> no estaba entre las implementaciones. <b>Es información del
    /// llamador y no un detalle interno</b>: es lo único que distingue «serví lo que el
    /// despliegue pidió» de «serví lo que quedaba», y sin ella un cambio de bundle no deja
    /// rastro en ningún log.</para>
    ///
    /// <para><b>El orden es ordinal y eso NO es una preferencia.</b> Ordenar por el nombre del
    /// framework no dice que <c>angular</c> valga más que <c>preact</c>: dice que el mismo
    /// registry, leído dos veces, da el mismo bundle. La preferencia, si algún día hace falta,
    /// la declara quien publica y llega por configuración — escribirla acá sería congelar en
    /// este repo una decisión del otro.</para>
    /// </remarks>
    public static string? Elegir(
        IEnumerable<string>? frameworks, string porDefecto, out bool desempatado)
    {
        desempatado = false;
        if (frameworks is null) return null;

        var disponibles = frameworks.ToList();
        if (disponibles.Count == 0) return null;

        if (disponibles.Contains(porDefecto, StringComparer.Ordinal)) return porDefecto;

        desempatado = disponibles.Count > 1;
        return disponibles.OrderBy(f => f, StringComparer.Ordinal).First();
    }
}
