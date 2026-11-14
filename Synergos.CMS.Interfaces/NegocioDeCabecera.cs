namespace Synergos.CMS.Interfaces;

/// <summary>Cómo pinta su cabecera una app dentro de un sitio, ya fusionado para un sitio (ADR 0137, D-016-V2).</summary>
/// <param name="Modo">Lo que el sitio DECLARA (<see cref="ModoDeCabecera.Secundaria"/> o <see cref="ModoDeCabecera.Completa"/>),
/// ya normalizado.</param>
public sealed record NegocioDeCabecera(string Modo);

/// <summary>
/// La cabecera de una app (la Tienda, Eventos, Educación) cuando la página ya pinta la del sitio (D-016-V2).
/// </summary>
/// <remarks>
/// <para><b>Secundaria</b>: la app no repite su marca; su menú (sus secciones, la cuenta, el carrito) queda como barra
/// secundaria bajo la cabecera del sitio. <b>Completa</b>: la barra de siempre, con su marca. Sin la cabecera del sitio
/// en la página, o servida sola, la app pinta la completa: no hay otra que la nombre.</para>
/// </remarks>
public static class ModoDeCabecera
{
    public const string Secundaria = "secundaria";
    public const string Completa = "completa";

    /// <summary>Si <paramref name="valor"/> es una de las dos palabras que se pueden declarar.</summary>
    public static bool EsValido(string? valor) => Normalizar(valor) is Secundaria or Completa;

    /// <summary>La palabra en minúsculas y sin espacios; sin valor, secundaria (el valor por defecto).</summary>
    public static string Normalizar(string? valor)
        => string.IsNullOrWhiteSpace(valor) ? Secundaria : valor.Trim().ToLowerInvariant();

    /// <summary>Lo que llega a la app: la completa si la página no pinta la cabecera del sitio; si no, lo declarado.</summary>
    public static string Decidir(bool laPaginaPintaLaCabeceraDelSitio, string declarado)
        => laPaginaPintaLaCabeceraDelSitio ? Normalizar(declarado) : Completa;
}
