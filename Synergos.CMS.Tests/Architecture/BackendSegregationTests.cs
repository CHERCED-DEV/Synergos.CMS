using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Las reglas de la segregación del backend (docs/product/06-arquitectura-backend.md) como
/// invariante ejecutable.
/// </summary>
/// <remarks>
/// <para><b>Por qué este gate es la condición para que <c>Synergos.Shared</c> exista.</b>
/// CLAUDE.md §6 prohíbe los proyectos <c>Shared/</c>, <c>Common/</c>, <c>Utils/</c>, y la
/// prohibición es buena: lo que describe es el proyecto cuyo criterio de admisión es "lo que no
/// cupo en otro lado". Ese proyecto siempre crece, nunca encoge, y termina acoplando todo con
/// todo. Un <c>Shared</c> con regla de admisión positiva es otra cosa — <b>pero solo si la
/// regla la verifica un test</b>. Si la verifica el criterio del que abre el PR, en seis meses
/// es el <c>Utils</c> que §6 prohibía, con otro nombre.</para>
///
/// <para><b>Por qué se leen los <c>.csproj</c> y no los ensamblados.</b> Los otros dos gates de
/// arquitectura (<see cref="LayerRuleTests"/>, <see cref="ArchitectureTests"/>) usan
/// <c>GetReferencedAssemblies</c>, que solo ve lo que el código TOCA. Aquí interesa lo
/// declarado: una referencia de proyecto entre una API y el CMS ya es el acople, la use o no
/// todavía. Y leyendo del disco el gate cubre <b>los proyectos que aún no existen</b>: el día
/// que aparezca <c>Synergos.Api.Commerce</c>, estas reglas ya lo están vigilando sin que nadie
/// tenga que acordarse de agregarlo.</para>
/// </remarks>
public sealed class BackendSegregationTests
{
    /// <summary>Un proyecto del repo: su nombre y las referencias que declara.</summary>
    private sealed record Csproj(string Name, string Path, IReadOnlyList<string> ProjectRefs);

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IReadOnlyList<Csproj> Projects()
    {
        var root = RepoRoot();
        var found = new List<Csproj>();

        foreach (var path in Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
        {
            // obj/ y bin/ guardan copias generadas; contarlas duplicaría proyectos.
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                path.Contains($"{Path.DirectorySeparatorChar}_archive{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var refs = XDocument.Load(path)
                .Descendants("ProjectReference")
                .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
                .Select(v => Path.GetFileNameWithoutExtension(v.Replace('\\', Path.DirectorySeparatorChar)))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            found.Add(new Csproj(Path.GetFileNameWithoutExtension(path), path, refs!));
        }

        Assert.NotEmpty(found);
        return found;
    }

    private static IReadOnlyList<Csproj> Named(string prefix) =>
        Projects().Where(p => p.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList();

    /// <summary>
    /// Si el proyecto pertenece al árbol de servicios —capacidades y orquestadores— y no al
    /// del CMS. El prefijo dice la capa, y por eso el nombre importa: sin él, la regla habría
    /// que mantenerla a mano proyecto por proyecto.
    ///
    /// <c>Synergos.Bff.*</c> y no <c>Synergos.Domain.*</c>: el arquitecto llamó BFF a la capa
    /// media, y el nombre del proyecto tiene que decir lo mismo que se dice al hablar.
    /// </summary>
    private static bool EsDelOtroArbol(string name) =>
        name.StartsWith("Synergos.Api.", StringComparison.Ordinal)
        || name.StartsWith("Synergos.Bff.", StringComparison.Ordinal);

    // ── La frontera Core ⊥ Shared ───────────────────────────────────────────
    // "Core no sabe qué es un host. Shared no sabe qué es un pedido."
    // Un tipo que parece pertenecer a los dos NO existe: está mal cortado.

    [Fact]
    public void Shared_solo_puede_referenciar_Core()
    {
        // UNA flecha, no dos. La justifica RejectionResults: el mapeo Rejection→HTTP lo
        // necesitan las dieciséis capacidades, y con la regla simétrica original había que
        // copiarlo dieciséis veces o meter dominio en Shared. Cualquier OTRA referencia —el
        // CMS, una API— sí convierte a Shared en el nudo que venía a deshacer.
        var shared = Named("Synergos.Shared").SingleOrDefault();
        if (shared is null) return;   // aún no existe: nada que vigilar

        var extra = shared.ProjectRefs.Where(r => r != "Synergos.Core").ToList();

        Assert.True(extra.Count == 0,
            $"Synergos.Shared solo puede referenciar Synergos.Core. Encontrado además: " +
            $"{string.Join(", ", extra)}.");
    }

    [Fact]
    public void Core_no_referencia_ningun_otro_proyecto_del_repo()
    {
        // La flecha va en un solo sentido. Si Core referenciara Shared, el vocabulario del
        // negocio pasaría a depender de ASP.NET por transitividad — y ahí se acabó la capa
        // pura sin que ningún csproj lo delate a simple vista.
        var core = Named("Synergos.Core").SingleOrDefault();
        if (core is null) return;

        Assert.True(core.ProjectRefs.Count == 0,
            $"Synergos.Core no puede referenciar proyectos del repo. Encontrado: " +
            $"{string.Join(", ", core.ProjectRefs)}.");
    }

    [Fact]
    public void Core_no_conoce_AspNetCore_ni_Umbraco()
    {
        var core = Named("Synergos.Core").SingleOrDefault();
        if (core is null) return;

        // Se miran las REFERENCIAS declaradas, no el texto del fichero. La primera versión
        // buscaba las cadenas sueltas y se disparó con un comentario que explicaba justamente
        // que esas referencias no debían estar: un gate que no distingue el código de la prosa
        // que lo documenta castiga documentar.
        //
        // Un FrameworkReference a Microsoft.AspNetCore.App es exactamente cómo una capa "pura"
        // deja de serlo sin que ninguna referencia de PAQUETE lo delate — por eso se miran los
        // tres tipos de referencia y no solo PackageReference.
        var referencias = XDocument.Load(core.Path)
            .Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "FrameworkReference" or "Reference")
            .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
            .ToList();

        var prohibidas = referencias
            .Where(r => r.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase)
                     || r.Contains("Umbraco", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(prohibidas.Count == 0,
            $"Synergos.Core no puede conocer ASP.NET ni Umbraco. Encontrado: {string.Join(", ", prohibidas)}.");
    }

    /// <summary>
    /// Sustantivos del negocio. La lista es corta y tosca A PROPÓSITO: un test que se lee en
    /// diez segundos es un test que nadie desactiva "un momentito".
    /// </summary>
    private static readonly string[] DomainNouns =
    {
        "Order", "Payment", "Cart", "Reservation", "Booking", "Patient", "Clinical",
        "Enrollment", "Course", "Property", "Event", "Ticket", "Tramite", "Case",
        "Member", "Comment", "Blog", "Product", "Invoice", "Refund",
    };

    /// <summary>
    /// Sustantivos del NEGOCIO. Distintos de <see cref="DomainNouns"/> a propósito: en una
    /// capacidad agnóstica, <c>Reservation</c> y <c>Order</c> son vocabulario propio y legítimo;
    /// <c>Patient</c> y <c>Tramite</c> no lo son nunca.
    /// </summary>
    private static readonly string[] BusinessNouns =
    {
        "Patient", "Clinical", "Doctor", "Prescription", "Diagnosis",
        "Tramite", "Expediente", "Course", "Student", "Mortgage",
        "Flight", "Hotel", "Stay", "Trip", "Blog",
    };

    /// <summary>
    /// Busca declaraciones de tipo cuyo nombre contenga uno de <paramref name="nouns"/>.
    /// </summary>
    /// <remarks>
    /// Solo mira DECLARACIONES. El cuerpo y los comentarios pueden decir "pedido" al explicar
    /// por qué algo existe — eso es documentación, no acople. Lo que no puede haber es un tipo
    /// llamado así.
    /// </remarks>
    private static List<string> NounLeaks(string projectPath, string[] nouns)
    {
        var dir = Path.GetDirectoryName(projectPath)!;
        var leaks = new List<string>();

        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

            foreach (var line in File.ReadLines(file))
            {
                var m = Regex.Match(line,
                    @"^\s*(?:public|internal)\s[\w<>\[\],\s\?]*\b(?:class|record|struct|interface|enum)\s+(\w+)");
                if (!m.Success) continue;

                var name = m.Groups[1].Value;
                var hit = nouns.FirstOrDefault(n => name.Contains(n, StringComparison.Ordinal));
                if (hit is not null)
                {
                    leaks.Add($"{Path.GetFileName(file)}: '{name}' contiene '{hit}'");
                }
            }
        }

        return leaks;
    }

    [Fact]
    public void Ningun_tipo_de_Shared_menciona_un_sustantivo_del_negocio()
    {
        var shared = Named("Synergos.Shared").SingleOrDefault();
        if (shared is null) return;

        var leaks = NounLeaks(shared.Path, DomainNouns);

        Assert.True(leaks.Count == 0,
            "Synergos.Shared no puede nombrar el dominio — eso va en Synergos.Core o en su API. " +
            $"Encontrado:{Environment.NewLine}{string.Join(Environment.NewLine, leaks)}");
    }

    // ── La frontera capacidad ⊥ dominio ─────────────────────────────────────
    // "La capacidad es dueña del CUÁNDO; el orquestador, del QUÉ." Booking sabe recurso +
    // ventana + cupo; no sabe que el recurso es un médico. Ver docs/product/06 §3.

    [Fact]
    public void Ninguna_capacidad_nombra_un_negocio_concreto()
    {
        // El modo de fallo típico: aparece 'SpecialtyCode' "solo por ahora" y la capacidad
        // deja de ser agnóstica sin que nadie lo haya decidido. Cuando eso pasa, el octavo
        // dominio ya no puede reutilizarla y se copia — que es de donde veníamos.
        var leaks = Named("Synergos.Api.")
            .SelectMany(api => NounLeaks(api.Path, BusinessNouns).Select(l => $"{api.Name} → {l}"))
            .ToList();

        Assert.True(leaks.Count == 0,
            "Una capacidad no puede nombrar un negocio concreto: eso va en su Synergos.Bff.*. " +
            $"Encontrado:{Environment.NewLine}{string.Join(Environment.NewLine, leaks)}");
    }

    [Fact]
    public void Ninguna_capacidad_ramifica_sobre_Ref_Kind()
    {
        // LA regla que sostiene la agnosticidad (docs/product/07 §3). Una capacidad guarda y
        // devuelve un Ref; no lo interpreta. El `if (kind == "salud.profesional")` entra un
        // martes como atajo y para cuando se nota, el octavo dominio ya no puede reutilizar la
        // capacidad y se copió el código — que es de donde veníamos.
        //
        // Es tosco: no atrapa a un adversario, atrapa el atajo. Es lo que hace falta.
        //
        // Lo que se busca es la comparación contra una CADENA LITERAL, y no cualquier propiedad
        // llamada Kind. La primera versión marcaba las dos cosas y se disparó con
        // `promo.Kind == DiscountKind.Fixed` de Api.Pricing — un enum de dominio propio y
        // perfectamente legítimo. Un gate que obliga a renombrar el dominio para callarlo no
        // sobrevive: alguien lo desactiva. Y la distinción es precisa, no un apaño: Ref.Kind es
        // un string, así que ramificar sobre él SIEMPRE compara contra texto.
        var patrones = new[]
        {
            @"\.Kind\s*(==|!=)\s*""",                          //  x.Kind == "salud.profesional"
            @"\.Kind\s+is\s+""",                                //  x.Kind is "salud.profesional"
            @"\.Kind\.(StartsWith|Contains|EndsWith)\s*\(\s*""", //  x.Kind.StartsWith("salud.")
        };

        // Un switch sobre .Kind solo delata si sus casos son cadenas: `switch (promo.Kind)` con
        // casos de enum es tan legítimo como la comparación de arriba.
        var switchSobreKind = new Regex(@"switch\s*\(\s*\w+\.Kind\s*\)");
        var casoDeTexto = new Regex(@"case\s+""");

        var leaks = new List<string>();
        foreach (var api in Named("Synergos.Api."))
        {
            var dir = Path.GetDirectoryName(api.Path)!;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

                // Los comentarios pueden hablar de Ref.Kind al explicar por qué NO se ramifica
                // sobre él — justamente lo que hace este archivo.
                var lineas = File.ReadLines(file)
                    .Select((texto, i) => (Texto: texto, Numero: i + 1))
                    .Where(l => !l.Texto.TrimStart().StartsWith("//", StringComparison.Ordinal))
                    .ToList();

                var hayCasosDeTexto = lineas.Any(l => casoDeTexto.IsMatch(l.Texto));

                foreach (var (texto, n) in lineas)
                {
                    if (patrones.Any(p => Regex.IsMatch(texto, p))
                        || (switchSobreKind.IsMatch(texto) && hayCasosDeTexto))
                    {
                        leaks.Add($"{api.Name}/{Path.GetFileName(file)}:{n} → {texto.Trim()}");
                    }
                }
            }
        }

        Assert.True(leaks.Count == 0,
            "Una capacidad guarda y devuelve un Ref; NUNCA ramifica sobre su Kind — el día que " +
            "lo haga deja de servirle al siguiente dominio. " +
            $"Encontrado:{Environment.NewLine}{string.Join(Environment.NewLine, leaks)}");
    }

    [Fact]
    public void Ninguna_capacidad_referencia_un_orquestador()
    {
        // Es la flecha que se invierte sola si nadie mira, y la que vuelve inútil toda la
        // capa: una capacidad que conoce un dominio ya no sirve al siguiente.
        var offenders = Named("Synergos.Api.")
            .Select(p => (p.Name, Bad: p.ProjectRefs
                .Where(r => r.StartsWith("Synergos.Bff.", StringComparison.Ordinal)).ToList()))
            .Where(x => x.Bad.Count > 0)
            .Select(x => $"{x.Name} → {string.Join(", ", x.Bad)}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Una capacidad no puede referenciar un orquestador — la flecha va al revés. " +
            $"Encontrado: {string.Join(" | ", offenders)}.");
    }

    // ── La frontera CMS ⊥ API ───────────────────────────────────────────────
    // Es la regla que hizo REAL la separación de Synergos.Api.Sessions (ADR 0130): sin
    // referencia de ensamblado, mudar el servicio a su repo no tiene nada que desenredar.

    [Fact]
    public void Ninguna_API_referencia_el_CMS()
    {
        var offenders = Projects()
            .Where(p => EsDelOtroArbol(p.Name))
            .Select(p => (p.Name, Bad: p.ProjectRefs.Where(r => r.StartsWith("Synergos.CMS.", StringComparison.Ordinal)).ToList()))
            .Where(x => x.Bad.Count > 0)
            .Select(x => $"{x.Name} → {string.Join(", ", x.Bad)}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "Una API no puede referenciar el CMS: el acople es el contrato HTTP. " +
            $"Encontrado: {string.Join(" | ", offenders)}.");
    }

    [Fact]
    public void El_CMS_no_referencia_ninguna_API()
    {
        // La simétrica, y la que más fácil se rompe: llamar en proceso "solo esta vez" es
        // gratis y convierte la API en una carpeta con ínfulas. El CMS habla HTTP a través
        // de un adapter en Web/Services — la forma de HttpSearchAnalyticsStore.
        var offenders = Projects()
            .Where(p => p.Name.StartsWith("Synergos.CMS.", StringComparison.Ordinal)
                     && p.Name != "Synergos.CMS.Tests")   // los tests sí pueden levantarlas
            .Select(p => (p.Name, Bad: p.ProjectRefs.Where(EsDelOtroArbol).ToList()))
            .Where(x => x.Bad.Count > 0)
            .Select(x => $"{x.Name} → {string.Join(", ", x.Bad)}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "El CMS no puede referenciar una API: si pudiera llamar en proceso, la API sería " +
            $"una carpeta con ínfulas. Encontrado: {string.Join(" | ", offenders)}.");
    }

    [Fact]
    public void El_gate_ve_de_verdad_los_proyectos_del_repo()
    {
        // Sin esto, un fallo del descubrimiento (ruta mal resuelta, filtro de más) dejaría
        // TODOS los asserts de arriba en verde sobre una lista vacía. Un gate que no puede
        // fallar es peor que no tener gate: da la señal de que se está vigilando.
        var names = Projects().Select(p => p.Name).ToList();

        Assert.Contains("Synergos.CMS.Web", names);
        Assert.Contains("Synergos.CMS.Interfaces", names);
        Assert.Contains("Synergos.Api.Sessions", names);
        Assert.Contains("Synergos.Shared", names);
    }
}
