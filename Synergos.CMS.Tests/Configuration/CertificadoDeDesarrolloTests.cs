using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Tests.Configuration;

/// <summary>
/// El guardián del par certificado + llave del HTTPS de desarrollo (#137).
/// </summary>
/// <remarks>
/// <para><b>Lo que hay que probar no es «lanza si falta», que es trivial: es que el mensaje sirva.</b>
/// El defecto que #137 cierra no era que Kestrel no fallara —falla— sino que su mensaje dice «no se
/// encontró el fichero» sobre una dependencia que ningún documento nombraba y que nada creaba. Un
/// guardián que lanzara igual de mudo no arreglaría nada, así que los tests afirman que el texto
/// lleva <b>la ruta resuelta</b> y <b>las dos salidas</b>.</para>
///
/// <para><b>Y se comprueban los CUATRO estados del par</b>, no sólo «falta todo»: con el
/// <c>.crt</c> presente y la <c>.key</c> ausente, el mensaje de .NET habla del certificado y manda
/// a mirar el fichero que sí está — que es media hora perdida en el sitio equivocado.</para>
/// </remarks>
public sealed class CertificadoDeDesarrolloTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("synergos-cert-").FullName;

    private string Crear(string nombre)
    {
        var ruta = Path.Combine(_dir, nombre);
        File.WriteAllText(ruta, "no importa el contenido: el guardián sólo mira que exista");
        return ruta;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Con_el_par_completo_no_dice_nada()
    {
        var crt = Crear("synergos-dev.crt");
        var key = Crear("synergos-dev.key");

        CertificadoDeDesarrollo.Exigir(crt, key);
    }

    [Fact]
    public void Sin_el_certificado_lanza_nombrando_la_ruta_RESUELTA()
    {
        var ausente = Path.Combine(_dir, "no-existe.crt");
        var key = Crear("synergos-dev.key");

        var ex = Assert.Throws<InvalidOperationException>(
            () => CertificadoDeDesarrollo.Exigir(ausente, key));

        // La ruta RESUELTA y no la que escribió el operador: con un default relativo
        // (`../certs/…`), lo que hace falta saber para arreglarlo es contra qué se resolvió.
        Assert.Contains(ausente, ex.Message, StringComparison.Ordinal);
        Assert.Contains("certificado", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// El caso que el mensaje de .NET cuenta mal: el certificado está y la llave no.
    /// </summary>
    [Fact]
    public void Con_el_certificado_presente_y_la_llave_ausente_nombra_LA_LLAVE()
    {
        var crt = Crear("synergos-dev.crt");
        var ausente = Path.Combine(_dir, "no-existe.key");

        var ex = Assert.Throws<InvalidOperationException>(
            () => CertificadoDeDesarrollo.Exigir(crt, ausente));

        Assert.Contains("llave", ex.Message, StringComparison.Ordinal);
        Assert.Contains(ausente, ex.Message, StringComparison.Ordinal);

        // Y NO nombra al certificado como problema, que es lo que mandaría a mirar el que sí está.
        Assert.DoesNotContain($"certificado: «{crt}»", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    public void Una_ruta_vacia_cuenta_como_ausente(string? crt, string? key)
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CertificadoDeDesarrollo.Exigir(crt, key));

        // «(vacío)» y no una cadena vacía dentro de las comillas: un mensaje que dijera
        // «falta — certificado: «»» se lee como un fallo del mensaje, no de la configuración.
        Assert.Contains("(vacío)", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lanzar sin decir cómo salir es un peaje (#132), así que el mensaje nombra las DOS salidas.
    /// </summary>
    [Fact]
    public void El_mensaje_nombra_la_herramienta_que_lo_crea_y_como_levantar_sin_HTTPS()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => CertificadoDeDesarrollo.Exigir(null, null));

        Assert.Contains("tools/cert-dev.mjs", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Kestrel:Endpoints:Https", ex.Message, StringComparison.Ordinal);
        Assert.Contains(".gitignore", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Las dos claves, en el orden que <c>Program.cs</c> da por hecho al indexarlas.
    /// </summary>
    /// <remarks>
    /// <c>Program.cs</c> escribe <c>Claves[0]</c> para el certificado y <c>Claves[1]</c> para la
    /// llave. Invertirlas no rompería el build y dejaría el guardián comprobando la llave contra el
    /// mensaje del certificado — el defecto que este seam existe para cerrar, cometido por su
    /// propio cableado.
    /// </remarks>
    [Fact]
    public void Las_claves_van_en_el_orden_que_Program_da_por_hecho()
    {
        Assert.Equal(2, CertificadoDeDesarrollo.Claves.Length);
        Assert.EndsWith(":Certificate:Path", CertificadoDeDesarrollo.Claves[0], StringComparison.Ordinal);
        Assert.EndsWith(":Certificate:KeyPath", CertificadoDeDesarrollo.Claves[1], StringComparison.Ordinal);
    }
}
