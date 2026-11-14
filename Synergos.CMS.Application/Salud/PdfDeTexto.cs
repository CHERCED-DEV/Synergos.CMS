using System.Globalization;
using System.Text;

namespace Synergos.CMS.Application.Salud;

/// <summary>
/// Un PDF de texto, sin dependencias (003-salud-cita, I5 P13: la receta que el paciente descarga). Helvetica 11 pt con
/// WinAnsiEncoding, que tiene las tildes y la eñe; renglones partidos a lo ancho de una carta y tantas páginas como hagan
/// falta. Para documentos de texto: no dibuja ni incrusta fuentes.
/// </summary>
public static class PdfDeTexto
{
    private const int CaracteresPorRenglon = 90;
    private const int RenglonesPorPagina = 58;
    private const int Interlineado = 13;

    /// <summary>
    /// Para WinAnsi: las comillas y los guiones tipográficos a su forma simple, y lo que no cabe en Latin-1 a «?». Sin esto,
    /// Latin-1 los cambiaría en silencio por otra cosa.
    /// </summary>
    private static readonly Dictionary<char, string> Simples = new()
    {
        ['–'] = "-", ['—'] = "-", ['“'] = "\"", ['”'] = "\"", ['‘'] = "'", ['’'] = "'", ['…'] = "...", ['•'] = "-",
    };

    public static byte[] Generar(IEnumerable<string> lineas)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        var renglones = lineas.SelectMany(Partir).ToList();
        if (renglones.Count == 0)
        {
            renglones.Add(string.Empty);
        }
        var paginas = renglones.Chunk(RenglonesPorPagina).ToList();

        // 1 catálogo, 2 páginas, 3 fuente; luego, por página, su objeto y su contenido.
        var objetos = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [" + string.Join(' ', paginas.Select((_, i) => $"{4 + i * 2} 0 R")) + $"] /Count {paginas.Count} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };
        foreach (var (pagina, i) in paginas.Select((p, i) => (p, i)))
        {
            var contenido = new StringBuilder("BT /F1 11 Tf 56 760 Td ")
                .Append(Interlineado.ToString(CultureInfo.InvariantCulture)).Append(" TL ");
            foreach (var renglon in pagina)
            {
                contenido.Append('(').Append(Escapar(renglon)).Append(") Tj T* ");
            }
            contenido.Append("ET");
            var texto = contenido.ToString();
            objetos.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + i * 2} 0 R >>");
            objetos.Add($"<< /Length {Encoding.Latin1.GetByteCount(texto)} >>\nstream\n{texto}\nendstream");
        }

        using var salida = new MemoryStream();
        void Escribir(string s) => salida.Write(Encoding.Latin1.GetBytes(s));
        Escribir("%PDF-1.4\n");
        var desplazamientos = new List<long>();
        for (var n = 0; n < objetos.Count; n++)
        {
            desplazamientos.Add(salida.Position);
            Escribir($"{n + 1} 0 obj\n{objetos[n]}\nendobj\n");
        }
        var xref = salida.Position;
        Escribir($"xref\n0 {objetos.Count + 1}\n0000000000 65535 f \n");
        foreach (var d in desplazamientos)
        {
            Escribir(d.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        }
        Escribir($"trailer\n<< /Size {objetos.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return salida.ToArray();
    }

    private static IEnumerable<string> Partir(string linea)
    {
        var texto = Normalizar(linea ?? string.Empty);
        if (texto.Length <= CaracteresPorRenglon)
        {
            yield return texto;
            yield break;
        }
        var actual = new StringBuilder();
        foreach (var palabra in texto.Split(' '))
        {
            if (actual.Length > 0 && actual.Length + 1 + palabra.Length > CaracteresPorRenglon)
            {
                yield return actual.ToString();
                actual.Clear();
            }
            if (actual.Length > 0)
            {
                actual.Append(' ');
            }
            actual.Append(palabra.Length > CaracteresPorRenglon ? palabra[..CaracteresPorRenglon] : palabra);
        }
        yield return actual.ToString();
    }

    private static string Normalizar(string texto)
    {
        var limpio = new StringBuilder(texto.Length);
        foreach (var c in texto.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '))
        {
            if (Simples.TryGetValue(c, out var simple))
            {
                limpio.Append(simple);
            }
            else
            {
                limpio.Append(c <= 'ÿ' && !char.IsControl(c) ? c : '?');
            }
        }
        return limpio.ToString();
    }

    private static string Escapar(string texto) => texto.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
