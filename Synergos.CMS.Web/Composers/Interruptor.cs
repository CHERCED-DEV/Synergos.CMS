namespace Synergos.CMS.Web.Composers;

/// <summary>
/// La pieza que lee el MODO de un interruptor de despliegue —<c>Synergos:&lt;X&gt;:Mode</c>— y lo
/// valida (#182): sin configurar es el default; una palabra que no reconoce LANZA al cablear y
/// nombra las válidas.
/// </summary>
/// <remarks>
/// <para><b>Por qué lanza en vez de caer al default.</b> Caer era lo que hacían los quince, y es
/// como nació el #177: <c>compose.prod.yml</c> escribía <c>Http</c>, el composer de la analítica
/// entendía <c>Sessions</c>, y la analítica se quedaba en el disco del contenedor del CMS mientras
/// <c>Api.Sessions</c> esperaba eventos que no llegaban. Medido en los otros catorce antes de esta
/// pieza: cualquier palabra que no fuera la que encendía —<c>Htpp</c>, <c>Api</c> en uno que dice
/// <c>Bff</c>, <c>Stub</c> en uno que dice <c>Local</c>, <c>" Api "</c> con espacios— cableaba
/// EXACTAMENTE lo mismo que no configurar nada. El CMS arranca, se ve bien, y los datos van a otro
/// sitio. <b>Degradar vale cuando el OTRO proceso está caído —y así sigue: con el modo bien
/// escrito y el servicio abajo, cada vertical degrada como antes—; no cuando lo que está mal es
/// la palabra</b>, porque ahí no se entera nadie. Es el trato que ya reciben la carpeta del CDN
/// (#132) y la URL del registry (#56).</para>
///
/// <para><b>Una pieza, y no una comparación en cada <c>if</c></b>, para que el interruptor
/// dieciséis nazca validado sin que nadie tenga que acordarse: <c>ModosDelComposeTests</c>
/// descubre toda clave <c>Synergos:…:Mode</c> que lee un composer, la compone con una palabra
/// inventada y exige que el arranque se niegue nombrando las válidas. Un <c>string.Equals</c>
/// a mano pasa el build y se pone rojo ahí.</para>
///
/// <para><b>Mayúsculas y espacios no cuentan</b>: <c>api</c>, <c>API</c> y <c>" Api "</c> son
/// <c>Api</c>, y se devuelve SIEMPRE la palabra canónica — así quien compare el resultado lo hace
/// con <see cref="StringComparison.Ordinal"/>. Antes los catorce ya ignoraban las mayúsculas, así
/// que ningún despliegue que funcionaba deja de hacerlo.</para>
///
/// <para><b>El default lo dice el POCO de la sección</b> (<c>new TiendaSettings().Mode</c>), no
/// un literal al lado: es la misma palabra que el <c>Configure&lt;T&gt;</c> le entrega al servicio
/// cuando la clave no está, y el gate la cruza por reflexión.</para>
/// </remarks>
internal static class Interruptor
{
    /// <summary>
    /// Si el interruptor <paramref name="clave"/> está en <paramref name="encendido"/>. Las dos
    /// palabras válidas son ésa y <paramref name="porDefecto"/> — la forma de catorce de los quince.
    /// </summary>
    /// <exception cref="ModoDesconocidoException">La clave trae otra palabra.</exception>
    internal static bool Encendido(IConfiguration config, string clave, string encendido, string porDefecto)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Encendido(config[clave], clave, encendido, porDefecto);
    }

    /// <summary>
    /// Lo mismo sobre un valor ya leído —el <c>Mode</c> de un POCO enlazado—, para que la fábrica
    /// que vuelve a mirar el modo al resolver decida con LA MISMA regla que el composer.
    /// </summary>
    internal static bool Encendido(string? configurado, string clave, string encendido, string porDefecto)
        => string.Equals(Modo(configurado, clave, porDefecto, encendido), encendido, StringComparison.Ordinal);

    /// <summary>
    /// El modo de <paramref name="clave"/> en su forma canónica: <paramref name="porDefecto"/> si
    /// no está configurada, o una de <paramref name="porDefecto"/> + <paramref name="otros"/>.
    /// </summary>
    /// <exception cref="ModoDesconocidoException">La clave trae otra palabra.</exception>
    internal static string Modo(IConfiguration config, string clave, string porDefecto, params string[] otros)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Modo(config[clave], clave, porDefecto, otros);
    }

    /// <inheritdoc cref="Modo(IConfiguration, string, string, string[])"/>
    internal static string Modo(string? configurado, string clave, string porDefecto, params string[] otros)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clave);
        ArgumentException.ThrowIfNullOrWhiteSpace(porDefecto);

        if (string.IsNullOrWhiteSpace(configurado))
        {
            return porDefecto;
        }

        string[] validos = [porDefecto, .. otros];
        var palabra = configurado.Trim();
        return validos.FirstOrDefault(v => string.Equals(v, palabra, StringComparison.OrdinalIgnoreCase))
               ?? throw new ModoDesconocidoException(clave, configurado, validos);
    }
}

/// <summary>
/// Un interruptor de despliegue trae una palabra que este CMS no reconoce (#182). Es un
/// <see cref="InvalidOperationException"/> —lo que el arranque ya trata como configuración
/// inválida— con la clave, lo que trajo y las palabras válidas a mano, para que el gate las cruce
/// sin leer el texto.
/// </summary>
internal sealed class ModoDesconocidoException : InvalidOperationException
{
    internal ModoDesconocidoException(string clave, string configurado, IReadOnlyList<string> validos)
        : base($"{clave}='{configurado}' no es un modo que este CMS reconozca. Valores válidos: "
               + $"{string.Join(", ", validos)} (sin configurar: {validos[0]}). En el entorno la clave "
               + $"se escribe {clave.Replace(":", "__", StringComparison.Ordinal)}. Un modo desconocido "
               + "no cae en silencio al camino por defecto: el CMS arrancaría bien y los datos irían "
               + "a otro sitio (#177, #182).")
    {
        Clave = clave;
        Configurado = configurado;
        Validos = validos;
    }

    /// <summary>La clave de configuración, p. ej. <c>Synergos:Tienda:Mode</c>.</summary>
    internal string Clave { get; }

    /// <summary>La palabra que trajo, tal cual.</summary>
    internal string Configurado { get; }

    /// <summary>Las que reconoce; la primera es el default.</summary>
    internal IReadOnlyList<string> Validos { get; }
}
