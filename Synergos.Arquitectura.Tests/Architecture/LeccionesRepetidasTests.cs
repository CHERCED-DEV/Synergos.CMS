using System.Text;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La herramienta de una sola vez que deja una publicación por lección repetida en el stream (tools/lecciones-repetidas.mjs,
/// 012-blogs-social, el catálogo lento). En la real son 1358 lecciones, 14 distintas, 97 copias de cada una: el stub de
/// Educación las volvía a sembrar en cada arranque. Aquí, el mismo defecto en pequeño.
/// </summary>
public sealed class LeccionesRepetidasTests : IDisposable
{
    private static readonly UTF8Encoding ConBom = new(encoderShouldEmitUTF8Identifier: true);

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "lecciones-repetidas-" + Guid.NewGuid().ToString("N"));

    private string Almacen => Path.Combine(_raiz, "App_Data");
    private string Stream => Path.Combine(Almacen, "syn-social-stream");
    private string Respaldo => Path.Combine(_raiz, "respaldo");
    private string Movidas => Path.Combine(Respaldo, "syn-social-stream");

    public LeccionesRepetidasTests()
    {
        // La lección A, tres veces; la B, dos, y la más vieja la nombra un comentario; la C, una vez.
        Publicacion("post-a1", "lesson", "ins-elena", "Cuerpo A", 1);
        Publicacion("post-a2", "lesson", "ins-elena", "Cuerpo A", 2);
        Publicacion("post-a3", "lesson", "ins-elena", "Cuerpo A", 3);
        Publicacion("post-b1", "lesson", "ins-sofia", "Cuerpo B", 1);
        Publicacion("post-b2", "lesson", "ins-sofia", "Cuerpo B", 2);
        Publicacion("post-c1", "lesson", "ins-mateo", "Cuerpo C", 1);
        // Un artículo con el cuerpo de A no es una lección, y un fichero roto no se toca.
        Publicacion("post-x1", "article", "ins-elena", "Cuerpo A", 0);
        Fichero("syn-social-stream", "roto.json", "{");
        Fichero("syn-comments", "c1.json", """{"Id":"c1","ContentItemId":"post-b1","Body":"Muy clara"}""");
    }

    public void Dispose()
    {
        if (Directory.Exists(_raiz)) { Directory.Delete(_raiz, recursive: true); }
    }

    private void Publicacion(string id, string tipo, string autor, string cuerpo, int minuto)
        => Fichero("syn-social-stream", id + ".json",
            $$"""{"Id":"{{id}}","Kind":"{{tipo}}","Author":{"Id":"{{autor}}","Handle":"{{autor}}","DisplayName":"{{autor}}","AvatarUrl":null,"Verified":false},"Body":"{{cuerpo}}","MediaUrl":null,"CreatedUtc":"2026-11-15T10:{{minuto:00}}:00Z","Metrics":{"Reactions":0,"Comments":0,"Reposts":0},"MediaAlt":null}""");

    private void Fichero(string familia, string nombre, string texto)
    {
        Directory.CreateDirectory(Path.Combine(Almacen, familia));
        File.WriteAllText(Path.Combine(Almacen, familia, nombre), texto, ConBom);
    }

    private static string[] Nombres(string dir)
        => Directory.Exists(dir) ? Directory.GetFiles(dir).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray() : [];

    private static (int Codigo, string Salida) Correr(params string[] argumentos)
    {
        var inicio = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = Proyectos.Raiz(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        inicio.ArgumentList.Add(Path.Combine(Proyectos.Raiz(), "tools", "lecciones-repetidas.mjs"));
        foreach (var a in argumentos) { inicio.ArgumentList.Add(a); }
        using var p = System.Diagnostics.Process.Start(inicio)!;
        var salida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, salida);
    }

    [Fact]
    public void El_ensayo_cuenta_lo_que_haria_y_no_mueve_nada()
    {
        var antes = Nombres(Stream);

        var (codigo, salida) = Correr("--almacen", Almacen, "--respaldo", Respaldo, "--ensayo");

        Assert.True(codigo == 0, salida);
        Assert.Contains("8 publicaciones, 6 lecciones, 3 distintas, 1 ilegible (no se toca)", salida, StringComparison.Ordinal);
        Assert.Contains("quedan 2 (1 porque otro fichero del almacén las nombra, 1 por ser las más recientes)", salida, StringComparison.Ordinal);
        Assert.Contains("movería 3", salida, StringComparison.Ordinal);
        Assert.Equal(antes, Nombres(Stream));
        Assert.False(Directory.Exists(Respaldo));
    }

    [Fact]
    public void Mueve_al_respaldo_las_repetidas_y_deja_la_nombrada_o_la_mas_reciente()
    {
        var originales = new[] { "post-a1.json", "post-a2.json", "post-b2.json" }
            .ToDictionary(n => n, n => File.ReadAllBytes(Path.Combine(Stream, n)));

        var (codigo, salida) = Correr("--almacen", Almacen, "--respaldo", Respaldo);

        Assert.True(codigo == 0, salida);
        Assert.Equal(new[] { "post-a3.json", "post-b1.json", "post-c1.json", "post-x1.json", "roto.json" }, Nombres(Stream));
        Assert.Equal(originales.Keys.Order(StringComparer.Ordinal), Nombres(Movidas));
        Assert.All(originales, o => Assert.Equal(o.Value, File.ReadAllBytes(Path.Combine(Movidas, o.Key))));
        Assert.Contains("\"movidas\": 3", File.ReadAllText(Path.Combine(Respaldo, "lecciones-repetidas.json")), StringComparison.Ordinal);

        var (otraVez, salidaOtraVez) = Correr("--almacen", Almacen, "--respaldo", Respaldo);
        Assert.True(otraVez == 0, salidaOtraVez);
        Assert.Contains("Ninguna lección repetida", salidaOtraVez, StringComparison.Ordinal);
    }

    [Fact]
    public void Con_un_argumento_mal_o_un_nombre_ya_en_el_respaldo_no_mueve_ninguna()
    {
        var antes = Nombres(Stream);
        Directory.CreateDirectory(Movidas);
        File.WriteAllText(Path.Combine(Movidas, "post-a1.json"), "de otra corrida");

        Assert.Equal(1, Correr("--almacen", Almacen).Codigo);
        Assert.Equal(1, Correr("--almacen", Almacen, "--respaldo", Path.Combine(Almacen, "syn-respaldo")).Codigo);
        Assert.Equal(2, Correr("--almacen", Path.Combine(_raiz, "otro"), "--ensayo").Codigo);
        Assert.Equal(3, Correr("--almacen", Almacen, "--respaldo", Respaldo).Codigo);
        Assert.Equal(antes, Nombres(Stream));
        Assert.Equal(new[] { "post-a1.json" }, Nombres(Movidas));
    }
}
