using System.Text.Json;
using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El contenido de demostración que vive en <c>uSync/v9/Content</c> y <c>Media</c> (ADR 0129) no
/// se importa SOLO en ningún perfil ni despliegue: cargarlo es siempre un paso explícito (ADR 0013).
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta ahora.</b> Mientras el contenido no estaba en el repo, encender
/// <c>ImportAtStartup</c> sólo reimportaba el esquema. Con las páginas y la media versionadas, la
/// misma línea convierte cada arranque en una siembra: en producción, el contenido de demo pisaría
/// lo que escribieron los editores. Es exactamente lo que la ADR 0013 prohíbe («nunca
/// auto-ejecuta»), por otro camino que el de los hosted services que vigila
/// <see cref="NadaSiembraAlArrancarTests"/>.</para>
///
/// <para><b>Lo que se mira.</b> Los <c>appsettings*.json</c> del proyecto web (cada perfil), y
/// los ficheros que fijan entorno en un despliegue: <c>compose*.yml</c>, <c>.env.example</c>,
/// <c>Dockerfile*</c> y los <c>tools/*.sh</c>. uSync 13 trae <c>ImportAtStartup = None</c> e
/// <c>ImportOnFirstBoot = false</c> de fábrica (lo dice su propio esquema,
/// <c>appsettings-schema.usync.json</c>), así que la regla es que nadie los cambie.</para>
///
/// <para><b>Lo que NO se mira, a propósito:</b> <c>tools/usync-rebuild-check.mjs</c>. Ése SÍ
/// importa al arrancar, contra una SQLite temporal y en un proceso propio: es el gate que prueba
/// que la base es derivable (ADR 0128). No es configuración del producto.</para>
/// </remarks>
public sealed class ElContenidoVersionadoNoSeImportaSoloTests
{
    private static readonly string[] ClavesDeImportacion = ["ImportAtStartup", "ImportOnFirstBoot"];

    /// <summary>
    /// Los que SÍ encienden la importación, porque son una invocación explícita y de una vez
    /// —lo que la ADR 0013 permite—, cada uno con su razón.
    /// </summary>
    /// <remarks>
    /// Censo vigilado en los dos sentidos: uno que deje de encenderla sobra acá y pone rojo el gate,
    /// para que la lista no acabe eximiendo lo que ya no existe.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Explicitos = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["tools/importar-schema.sh"] =
            "el import de un servidor NUEVO: a mano, una vez, en un contenedor efímero (compose run --rm) que importa y desaparece (#114)",
    };

    [Fact]
    public void Ningun_perfil_de_appsettings_importa_uSync_al_arrancar()
    {
        var web = Path.Combine(RepoRoot(), "Synergos.CMS.Web");
        var perfiles = Directory.GetFiles(web, "appsettings*.json")
            .Where(f => !Path.GetFileName(f).StartsWith("appsettings-schema", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Suelo contra el vacío: sin el base y el del despliegue, «ninguno importa» no diría nada.
        var nombres = perfiles.Select(Path.GetFileName).ToList();
        Assert.Contains("appsettings.json", nombres);
        Assert.Contains("appsettings.Docker.json", nombres);

        var malos = new List<string>();
        foreach (var perfil in perfiles)
        {
            using var doc = JsonDocument.Parse(
                File.ReadAllText(perfil).TrimStart('﻿'),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            Recorrer(doc.RootElement, Path.GetFileName(perfil), string.Empty, malos);
        }

        Assert.True(malos.Count == 0,
            "uSync importaría el contenido versionado al arrancar (ADR 0013, 0129): "
            + string.Join("; ", malos));
    }

    [Fact]
    public void Ningun_despliegue_enciende_la_importacion_de_uSync_por_entorno()
    {
        var raiz = RepoRoot();
        var ficheros = Directory.GetFiles(raiz, "compose*.yml")
            .Concat(Directory.GetFiles(raiz, "Dockerfile*", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(raiz, "tools"), "*.sh"))
            .Concat(new[] { Path.Combine(raiz, ".env.example") }.Where(File.Exists))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.Contains(ficheros, f => Path.GetFileName(f).StartsWith("compose", StringComparison.Ordinal));

        // `uSync__Settings__ImportAtStartup=All`, con o sin comillas y en forma de lista de YAML.
        var encendido = new Regex(
            @"uSync__Settings__(ImportAtStartup\s*[:=]\s*[""']?(?!None\b)[A-Za-z]|ImportOnFirstBoot\s*[:=]\s*[""']?true)",
            RegexOptions.IgnoreCase);
        var hallados = ficheros
            .SelectMany(f => File.ReadAllLines(f).Select((l, i) => (f, l, i)))
            .Where(x => !x.l.TrimStart().StartsWith('#') && encendido.IsMatch(x.l))
            .Select(x => (Fichero: Path.GetRelativePath(raiz, x.f).Replace('\\', '/'), Linea: x.i + 1))
            .ToList();

        var malos = hallados.Where(h => !Explicitos.ContainsKey(h.Fichero)).Select(h => $"{h.Fichero}:{h.Linea}").ToList();
        Assert.True(malos.Count == 0,
            "un despliegue enciende la importación de uSync al arrancar (ADR 0013, 0129): "
            + string.Join(", ", malos));

        var sobran = Explicitos.Keys.Where(k => !hallados.Any(h => h.Fichero == k)).ToList();
        Assert.True(sobran.Count == 0,
            "el censo exime a ficheros que ya no encienden la importación — sacalos de Explicitos: "
            + string.Join(", ", sobran));
    }

    private static void Recorrer(JsonElement e, string fichero, string ruta, List<string> malos)
    {
        if (e.ValueKind != JsonValueKind.Object) return;

        foreach (var p in e.EnumerateObject())
        {
            var aqui = ruta.Length == 0 ? p.Name : $"{ruta}:{p.Name}";
            if (ClavesDeImportacion.Contains(p.Name, StringComparer.OrdinalIgnoreCase)
                && aqui.StartsWith("uSync:", StringComparison.OrdinalIgnoreCase)
                && Enciende(p.Name, p.Value))
            {
                malos.Add($"{fichero} → {aqui} = {p.Value.GetRawText()}");
            }

            Recorrer(p.Value, fichero, aqui, malos);
        }
    }

    private static bool Enciende(string clave, JsonElement valor) => clave.Equals("ImportOnFirstBoot", StringComparison.OrdinalIgnoreCase)
        ? valor.ValueKind == JsonValueKind.True
            || (valor.ValueKind == JsonValueKind.String && bool.TryParse(valor.GetString(), out var b) && b)
        : valor.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(valor.GetString())
            && !valor.GetString()!.Trim().Equals("None", StringComparison.OrdinalIgnoreCase);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
