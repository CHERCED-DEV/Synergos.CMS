using System.Text.Json;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Cuánto vale UNA unidad de una línea del carrito: el precio base del producto más el delta de
/// la variante elegida. <c>false</c> cuando no se puede saber.
/// </summary>
/// <remarks>
/// <para><b>Por qué existe como clase y no como dos líneas dentro del carrito</b> (#123). La
/// decisión que vive aquí es la que costaba dinero —<c>DefaultCartService</c> leía
/// <c>productPriceBase</c> con <c>decimal.TryParse(…, NumberStyles.Number, InvariantCulture)</c> y
/// caía a <c>0m</c>, así que <c>"49.000"</c> se cobraba a 49 y <c>"$89000"</c> a cero— y no tenía
/// un solo test: <c>Hydrate</c> necesita un <c>IUmbracoContextAccessor</c> y un árbol de
/// <c>IPublishedContent</c>, y simular eso cuesta más que el código que verifica. Es el mismo
/// movimiento que hicieron <c>EventContentRules</c> y <c>CourseContentRules</c>: sacar la regla a
/// lógica pura es lo que la hace verificable, y es justo la que decide cuánto se cobra.</para>
///
/// <para><b>La política: un precio que no se puede leer NO cae a cero, y la línea se OMITE.</b>
/// Las tres opciones eran omitir, rechazar el carrito entero o propagar el problema, y las tres
/// se miraron:</para>
/// <list type="bullet">
///   <item><b>Cero queda descartado de plano.</b> Cero es un precio VÁLIDO: no falla en ninguna
///   parte, no enciende ninguna guarda de «&gt; 0», y la primera vez que se nota es cuando
///   alguien ya compró. Es <c>feedback_an_omitted_key_can_be_an_assertion</c> sobre un
///   <c>TryParse</c>: el fallback deja de ser un hueco y pasa a AFIRMAR «gratis».</item>
///   <item><b>Rechazar el carrito entero</b> castiga las líneas sanas por la errata de un
///   editor en un producto ajeno: quien tiene tres cosas en el carrito se queda sin poder
///   comprar ninguna, y no hay nada que pueda hacer al respecto.</item>
///   <item><b>Omitir la línea</b> es además lo que el carrito YA hace con un SKU cuyo producto se
///   borró, y desde el lado de quien compra es el mismo hecho: un producto cuyo precio nadie
///   puede leer tampoco está a la venta. Y mantiene el SUBTOTAL cierto — con la línea a cero el
///   subtotal es un número equivocado exactamente por el precio del artículo, y es
///   <c>Cart.Subtotal</c> lo que el checkout cobra.</item>
/// </list>
///
/// <para><b>Omitir en silencio sí sería un defecto, y por eso no se omite en silencio:</b> el
/// carrito loguea a nivel Error con el SKU y el texto que tecleó el editor, igual que las tres
/// fuentes de catálogo. La línea sigue en la cookie, así que el día que el editor arregle el
/// precio vuelve sola — omitirla no destruye lo que alguien eligió.</para>
///
/// <para><b>El delta de variante NO se valida con la misma regla</b>, y es a propósito:
/// <c>productVariantsJson</c> es JSON y su <c>priceDelta</c> es un número JSON, no texto que
/// alguien teclea en un TextBox. No hay ambigüedad de separador que resolver, así que meterlo en
/// <see cref="PrecioAutorado"/> sería confundir el sujeto (#120). Un JSON roto ya vale 0 delta
/// desde siempre — es un ajuste sobre un precio que sí se leyó, no el precio.</para>
/// </remarks>
public static class ShopLinePricing
{
    /// <summary>
    /// El precio unitario de la línea, o <c>false</c> si <paramref name="priceBaseRaw"/> no es
    /// inequívocamente un precio.
    /// </summary>
    /// <remarks>
    /// Devuelve <c>bool</c> y no un <c>decimal</c> con respaldo a propósito: <b>que «no se pudo»
    /// sea una propiedad del TIPO y no una convención</b> es lo que impide que el próximo
    /// llamador lea el número sin mirar. La firma vieja —<c>ParsePrice(...) → decimal</c>— no
    /// dejaba decir «no se sabe», así que devolvía cero.
    /// </remarks>
    public static bool TryUnitPrice(
        string? priceBaseRaw,
        string? variantsJson,
        string? variantSku,
        out decimal unitPrice)
    {
        if (!PrecioAutorado.EsInequivoco(priceBaseRaw, out unitPrice))
        {
            unitPrice = 0m;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(variantSku))
        {
            unitPrice += VariantPriceDelta(variantsJson, variantSku);
        }

        return true;
    }

    /// <summary>El ajuste de la variante elegida; 0 si no está declarada o el JSON está roto.</summary>
    internal static decimal VariantPriceDelta(string? variantsJson, string variantSku)
    {
        if (string.IsNullOrWhiteSpace(variantsJson)) return 0m;
        try
        {
            using var doc = JsonDocument.Parse(variantsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return 0m;
            foreach (var v in doc.RootElement.EnumerateArray())
            {
                if (!v.TryGetProperty("sku", out var skuEl) || skuEl.ValueKind != JsonValueKind.String) continue;
                if (!string.Equals(skuEl.GetString(), variantSku, StringComparison.OrdinalIgnoreCase)) continue;
                if (v.TryGetProperty("priceDelta", out var deltaEl) && deltaEl.TryGetDecimal(out var delta))
                {
                    return delta;
                }
                return 0m;
            }
        }
        catch { /* malformed JSON → ignore */ }
        return 0m;
    }
}
