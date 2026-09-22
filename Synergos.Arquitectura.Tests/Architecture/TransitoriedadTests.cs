using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Si un rechazo del árbol de servicios se puede volver a intentar lo dice la CAPACIDAD, en la
/// bandera <c>transient</c> — y el CMS no lo deduce del código de estado (#129).
/// </summary>
/// <remarks>
/// <para><b>Qué se midió.</b> Quince clientes <c>Http*</c> en <c>Web/Services/</c>; <b>uno</b>
/// leía <c>transient</c> y los otros catorce ramificaban sobre <c>HttpStatusCode</c> con su propia
/// tabla. Y el que lo hacía bien <b>ya tenía la regla escrita en su propio
/// <c>&lt;remarks&gt;</c></b>: <i>«Se mira esa bandera y no el código de estado: repetir aquí la
/// tabla de códigos sería una segunda verdad que se desincroniza»</i> — o sea
/// <c>feedback_a_fabrication_can_be_a_derivation</c> addendum #123, con la regla correcta viviendo
/// dentro de la única copia que la cumplía.</para>
///
/// <para><b>Y lo que el hallazgo decía no era exacto, medido rama por rama.</b> Los catorce no
/// deciden «si reintentar» —<b>cero</b> de los quince tiene bucle de reintento—: deciden cómo
/// PRESENTAR el fallo, y casi siempre bien. Un 404 es «no existe», un 401 nombra la llave
/// compartida, y un 409/403/400 sube como rechazo de negocio con su motivo. Un 503 ya cae hoy al
/// camino de infraestructura, que es donde debe caer. Lo que falta no es la rama: es que
/// <b>nada distingue un 503 transitorio de uno permanente</b>, así que el día que alguien quiera
/// reintentar tendría que elegir mirando catorce tablas privadas.</para>
///
/// <para><b>Por eso este gate es un trinquete ABSOLUTO y no pide reescribir nada</b>: hoy ya se
/// cumple —cero clientes nombran un código de transitoriedad— así que es gratis, que es la
/// condición que el #134 puso. Lo que impide es que la deuda CREZCA: el día que alguien escriba
/// <c>if (res.StatusCode == HttpStatusCode.ServiceUnavailable)</c> estará escribiendo la
/// quinceava copia de una tabla que es de la capacidad.</para>
/// </remarks>
public sealed class TransitoriedadTests
{
    /// <summary>Los códigos con los que se estaría DEDUCIENDO que algo se puede reintentar.</summary>
    private const string CodigosDeTransitoriedad = @"ServiceUnavailable|TooManyRequests|RequestTimeout|\b503\b|\b429\b|\b408\b";

    private static string Dir(params string[] partes) => Proyectos.Ruta(partes);

    private static IReadOnlyList<(string Nombre, string Fuente)> Clientes()
        => Directory.EnumerateFiles(Dir("Synergos.CMS.Web", "Services"), "Http*.cs")
            .Select(f => (Path.GetFileNameWithoutExtension(f)!, SinComentarios(f)))
            .OrderBy(x => x.Item1, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// El fichero SIN comentarios.
    /// </summary>
    /// <remarks>
    /// Hace falta y no es teoría: el <c>&lt;remarks&gt;</c> de <c>HttpPaymentProvider</c> explica
    /// la regla nombrando <c>Unavailable</c>, y esta misma clase nombra los seis códigos. Un gate
    /// que leyera el texto crudo se dispararía con su propia documentación.
    /// </remarks>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal)
                || t.StartsWith('*')
                || t.StartsWith("/*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));

    [Fact]
    public void Ningun_cliente_DEDUCE_la_transitoriedad_del_codigo_de_estado()
    {
        var clientes = Clientes();

        Assert.True(clientes.Count >= 12,
            "El descubrimiento de clientes no ve nada (" + clientes.Count + "): si se movieron de "
            + "carpeta, este gate pasa en verde sin mirar nada.");

        var deducen = clientes
            .Where(c => Regex.IsMatch(c.Fuente, CodigosDeTransitoriedad))
            .Select(c => c.Nombre)
            .ToList();

        Assert.True(deducen.Count == 0,
            "Estos clientes deducen del código de estado algo que la capacidad ya dice en su "
            + "bandera `transient`: " + string.Join(", ", deducen)
            + ". El mismo 503 puede ser `store_busy` —quince milisegundos de cola, #112— o una "
            + "caída de verdad, y el #112 eligió Unavailable justo para que un orquestador no "
            + "deshiciera una saga sana. Una tabla de códigos acá desharía esa decisión sin "
            + "enterarse: se lee `RechazoDelArbolDeServicios` (#129).");
    }

    [Fact]
    public void La_bandera_transient_se_declara_UNA_vez()
    {
        // La regla vivía dentro del único cliente que la cumplía. Que sólo un fichero nombre la
        // clave es lo que impide que vuelva a haber una segunda verdad — y es el mismo corte que
        // `SeudonimoUnicoTests` hace con el seudónimo (#120).
        var declarantes = Directory
            .EnumerateFiles(Dir("Synergos.CMS.Web", "Services"), "*.cs")
            .Select(f => (Nombre: Path.GetFileNameWithoutExtension(f)!, Fuente: SinComentarios(f)))
            .Where(x => Regex.IsMatch(x.Fuente, @"\btransient\b", RegexOptions.IgnoreCase))
            .Select(x => x.Nombre)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(declarantes.Count == 1 && declarantes[0] == "RechazoDelArbolDeServicios",
            "La bandera `transient` la tiene que leer un solo sitio y hoy la nombran: "
            + string.Join(", ", declarantes)
            + ". Con dos lectores vuelve a haber dos verdades sobre lo mismo (#129).");
    }
}
