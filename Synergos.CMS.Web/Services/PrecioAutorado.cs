using System.Globalization;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El precio que un editor tecleó en un <c>Umbraco.TextBox</c>: o es INEQUÍVOCO, o no es un
/// precio. <b>Solo dígitos.</b>
/// </summary>
/// <remarks>
/// <para><b>Por qué vive acá y no en cinco sitios</b> (#123). La regla estaba escrita tres veces
/// —<c>UmbracoProductCatalogSource.TryParsePrice</c>, <c>UmbracoEventCatalogSource.TryParsePriceFrom</c>
/// y <c>CourseContentRules.TryParsePrice</c>— y <b>rota otras cinco</b>: el carrito
/// (<c>DefaultCartService</c>), el listado (<c>DefaultShopQuery</c>), la ficha de producto, el
/// carrito editorial y el <c>ld+json</c> de la ficha seguían con
/// <c>decimal.TryParse(raw, NumberStyles.Number, InvariantCulture) ? price : 0m</c>. La regla del
/// repo promueve al SEGUNDO consumidor (<c>CLAUDE.md</c> §0.B.17); con ocho llevaba seis de
/// retraso.</para>
///
/// <para><b>Los DOS agujeros del <c>TryParse</c> de siempre</b>, y el segundo es el caro:</para>
/// <list type="number">
///   <item><c>"$89000"</c> no parsea → fallback SILENCIOSO a <c>0m</c> → mercancía comprable a
///   precio CERO.</item>
///   <item><b>El que de verdad ocurre en Colombia:</b> <c>"49.000"</c> SÍ parsea — en
///   <c>InvariantCulture</c> el punto es separador DECIMAL, así que da <b>49</b>. El editor
///   escribe 49 mil pesos y la tienda lo vende por 49. No es un fallo de parseo: es un precio
///   plausible equivocado por 1000×, y ninguna guarda de «&gt; 0» lo ve. <i>Verificado en vivo:
///   el Tote salió a 49.</i></item>
/// </list>
///
/// <para>Por eso no basta con parsear: el formato tiene que ser <b>inequívoco</b>. COP no usa
/// centavos en la práctica, así que la regla es <b>solo dígitos ASCII</b> — cualquier punto, coma,
/// símbolo, signo o espacio se rechaza. Y el editor está rodeado de la tentación:
/// <c>EsCoPriceFormatter</c> pinta <c>"$ 49.000"</c> en toda la tienda, así que copiar de la
/// pantalla al campo es exactamente cómo llega el <c>"49.000"</c>.</para>
///
/// <para><b>Qué NO decide esta función, y es la mitad que importa</b> (#120). Decide si el texto
/// es un precio; <b>no</b> decide qué significa que no lo sea, ni qué significa el vacío, y esas
/// dos cosas <b>no son iguales en los ocho sitios</b>: el catálogo de Tienda exige
/// <c>&gt; 0</c> y omite el producto; Eventos y Educación tratan el vacío como «gratis o sin
/// precio publicado» y sólo omiten cuando hay texto ambiguo; el carrito omite la línea; la ficha
/// no pinta precio ni botón de comprar. Un helper con banderas para eso sería ocho copias con más
/// pasos, y encima escondería las políticas donde nadie las lee. Por eso el <b>vacío devuelve
/// <c>false</c></b>: es el llamador quien sabe si «sin precio» es legítimo.</para>
///
/// <para><b>Lo que se le parece y NO es esto</b>, porque el algoritmo no es el criterio:
/// <c>CatalogFilter</c> parsea <c>"1000-5000"</c> y <c>"4.5"</c> con <c>NumberStyles.Float</c> e
/// <c>InvariantCulture</c>, y ahí el punto DECIMAL es correcto — ese texto no lo teclea un editor,
/// lo genera el cliente en la query string, y lo que se hace con la basura es <b>no aplicar el
/// filtro</b>, no omitir nada. Mismo <c>TryParse</c> y otro sujeto: meterlo acá dejaría un helper
/// que en un sitio lee dinero autorado y en otro lee un parámetro de URL, y el día que uno
/// necesitara cambiar, cambiaría el otro.</para>
/// </remarks>
public static class PrecioAutorado
{
    /// <summary>
    /// <c>true</c> y el monto si <paramref name="raw"/> es inequívocamente un precio;
    /// <c>false</c> —con <paramref name="precio"/> en cero— si no lo es o si está vacío.
    /// </summary>
    /// <remarks>
    /// <b>El cero de salida no es un precio: es «no hay dato».</b> Va en cero y no se deja
    /// indefinido para que un llamador descuidado no lea basura de la pila, pero leerlo sin mirar
    /// el <c>bool</c> es exactamente el defecto que esto viene a cerrar
    /// (<c>feedback_an_omitted_key_can_be_an_assertion</c>: un fallback silencioso deja de ser un
    /// hueco y pasa a AFIRMAR «gratis»).
    /// </remarks>
    public static bool EsInequivoco(string? raw, out decimal precio)
    {
        precio = 0m;
        var valor = raw?.Trim();

        // Solo dígitos: ni "49.000" (que parsearía a 49) ni "$49000" ni "49,000" ni "49000 COP".
        // El guardado de longitud lo hace el propio TryParse: una ristra de dígitos que no cabe
        // en un decimal devuelve false, y aquí eso es «no es un precio», que es lo correcto.
        if (string.IsNullOrEmpty(valor) || !valor.All(char.IsAsciiDigit))
        {
            return false;
        }

        if (decimal.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out precio))
        {
            return true;
        }

        precio = 0m;
        return false;
    }
}
