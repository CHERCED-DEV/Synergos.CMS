using QRCoder;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Helper para renderizar QR codes como SVG inline string. Usado por
/// la vista de 2FA enrollment para que el Member escanee con su app
/// autenticadora en lugar de copiar/pegar el secret manual.
/// (Olas 223-224, ADR 0084)
/// </summary>
/// <remarks>
/// Singleton — QRCoder es stateless. SVG output preferred over PNG por:
/// 1. No bitmap/dimensions lock — escalable.
/// 2. Tamaño string razonable (~5KB para URI típica).
/// 3. Inline en <c>data:image/svg+xml;base64,...</c> sin extra endpoint.
/// </remarks>
public sealed class QrCodeRenderer
{
    /// <summary>
    /// Genera un SVG string para el contenido indicado. Pixel size es
    /// el "module" del QR — 4 da un QR ~~120px en 21x21 modules estándar.
    /// </summary>
    public string RenderSvg(string content, int pixelSize = 4)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data);
        return svg.GetGraphic(pixelSize);
    }

    /// <summary>
    /// Wraps <see cref="RenderSvg"/> en data URI para src directo del
    /// <c>&lt;img&gt;</c> sin extra endpoint server-side.
    /// </summary>
    public string RenderSvgDataUri(string content, int pixelSize = 4)
    {
        var svg = RenderSvg(content, pixelSize);
        if (string.IsNullOrEmpty(svg)) return string.Empty;
        var bytes = System.Text.Encoding.UTF8.GetBytes(svg);
        var b64 = Convert.ToBase64String(bytes);
        return "data:image/svg+xml;base64," + b64;
    }
}
