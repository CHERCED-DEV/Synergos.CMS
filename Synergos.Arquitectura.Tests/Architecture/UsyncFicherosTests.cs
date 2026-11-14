namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Los ficheros de uSync se llaman como uSync los lee, y cada medio tiene su binario (tools/usync-ficheros.mjs, checks
/// 14 y 15 de tools/usync-audit.mjs).
/// </summary>
/// <remarks>
/// <para><b>Qué pasó (016, 35a1b88b).</b> 30 .config de Media entraron con un retorno de carro al final del nombre;
/// en Windows se ve como U+F00D. uSync sólo lee *.config y no los importó, y los de nombre bueno apuntaban a 28 .png
/// que el mismo commit borró. El XML de dentro estaba bien, así que ninguna compuerta lo vio. Sobre el árbol de
/// a992776d el chequeo da 30 nombres y 28 medios sin binario; aquí se reproduce el mismo defecto en pequeño.</para>
/// </remarks>
public sealed class UsyncFicherosTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "usync-ficheros-" + Guid.NewGuid().ToString("N"));

    private string Usync => Path.Combine(_raiz, "uSync", "v9");
    private string Wwwroot => Path.Combine(_raiz, "wwwroot");

    public void Dispose()
    {
        if (Directory.Exists(_raiz)) { Directory.Delete(_raiz, recursive: true); }
    }

    private void Medio(string fichero, string src)
    {
        Directory.CreateDirectory(Path.Combine(Usync, "Media"));
        File.WriteAllText(Path.Combine(Usync, "Media", fichero),
            $"<Media Key=\"{Guid.NewGuid()}\" Alias=\"x\" Level=\"1\"><Properties><umbracoFile><Value><![CDATA[{{ \"src\": \"{src}\" }}]]></Value></umbracoFile></Properties></Media>");
    }

    private void Binario(string relativo)
    {
        var ruta = Path.Combine(Wwwroot, relativo.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllBytes(ruta, [1]);
    }

    private (int Codigo, string Salida) Correr()
    {
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "node",
            ArgumentList = { Path.Combine(Proyectos.Raiz(), "tools", "usync-ficheros.mjs"), "--usync", Usync, "--wwwroot", Wwwroot },
            WorkingDirectory = Proyectos.Raiz(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        })!;
        var salida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, salida);
    }

    [Fact]
    public void El_defecto_de_35a1b88b_sale_rojo_un_nombre_con_U_F00D_y_un_medio_sin_binario()
    {
        Binario("media/abc/producto-taza.jpg");
        Medio("producto-taza.config", "/media/abc/producto-taza.jpg");
        Medio("producto-funda.config", "/media/def/producto-funda.png");
        Medio("producto-funda.config", "/media/abc/producto-taza.jpg");

        var (codigo, salida) = Correr();

        Assert.Equal(1, codigo);
        Assert.Contains("producto-funda.config\\u{F00D}", salida, StringComparison.Ordinal);
        Assert.Contains("[medio-sin-binario]", salida, StringComparison.Ordinal);
        Assert.Contains("/media/def/producto-funda.png", salida, StringComparison.Ordinal);
        Assert.DoesNotContain("producto-taza.config:", salida, StringComparison.Ordinal);
    }

    [Fact]
    public void Con_los_nombres_buenos_y_sus_binarios_sale_verde()
    {
        Binario("media/abc/producto-taza.jpg");
        Binario("media/def/producto-funda.jpg");
        Medio("producto-taza.config", "/media/abc/producto-taza.jpg");
        Medio("producto-funda.config", "/media/def/producto-funda.jpg");
        File.WriteAllText(Path.Combine(Usync, "Media", ".gitkeep"), string.Empty);

        var (codigo, salida) = Correr();

        Assert.True(codigo == 0, salida);
    }
}
