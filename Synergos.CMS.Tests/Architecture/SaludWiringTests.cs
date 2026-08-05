namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La cita clínica agenda contra el ORQUESTADOR, y el sustantivo «médico» no baja a la capacidad
/// (HU #25).
/// </summary>
/// <remarks>
/// <para>Dos reglas distintas, y conviene no confundirlas porque protegen cosas distintas:</para>
///
/// <list type="number">
///   <item><b>El CMS no llama a <c>Api.Booking</c> de frente.</b> Agendar es apartar el cupo, cobrar
///   el copago y avisar; si el copago falla hay que soltar el cupo. Eso es una saga, y el CMS no
///   tiene dónde anotar una compensación pendiente. Mismo argumento que la tienda.</item>
///
///   <item><b><c>Api.Booking</c> no sabe que el recurso es un médico</b> (<c>CLAUDE.md</c> §12: la
///   capacidad es dueña del CUÁNDO; el orquestador, del QUÉ). Un <c>if</c> sobre
///   <c>salud.profesional</c> dentro de una <c>Api.*</c> la inutiliza para el siguiente dominio
///   — ese gate ya existe en <c>BackendSegregationTests</c>; acá se vigila el otro extremo, que
///   la traducción médico→recurso viva del lado del CMS.</item>
/// </list>
/// </remarks>
public sealed class SaludWiringTests
{
    /// <summary>
    /// Rutas de <c>Api.Booking</c> que el CMS no puede llamar.
    /// </summary>
    /// <remarks>
    /// <c>v1/holds</c> ya lo cubre <see cref="ShopWiringTests"/>; acá se añaden las propias del
    /// calendario. La lectura de agenda (<c>GetByDateAsync</c>) devuelve vacío a propósito en vez
    /// de abrir esta puerta — está anotado en el ticket y en la clase.
    /// </remarks>
    private static readonly string[] Prohibidas = { "v1/reservations", "v1/resources" };

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

    private static IEnumerable<string> FuentesDelCms()
        => new[] { "Synergos.CMS.Web", "Synergos.CMS.Application", "Synergos.CMS.Interfaces" }
            .Select(p => Path.Combine(RepoRoot(), p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta)
            .Select(l =>
            {
                var t = l.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal))
                {
                    return string.Empty;
                }
                var i = l.IndexOf("//", StringComparison.Ordinal);
                return i >= 0 ? l[..i] : l;
            }));

    /// <summary>
    /// El único fichero al que se le permite hablarle a <c>Api.Booking</c> de frente.
    /// </summary>
    /// <remarks>
    /// <para><b>La excepción tiene una razón y tiene su propio gate.</b> Agendar una <i>visita a
    /// un inmueble</i> (HU #33a) toca UNA sola capacidad: una visita no se cobra y no dispara
    /// avisos, así que no hay orden que respetar ni nada que deshacer a la mitad — que es
    /// exactamente lo que este gate defiende para el camino clínico, donde sí los hay.</para>
    ///
    /// <para><b>Y la excepción no es un hueco:</b> <c>RealtyWiringTests</c> comprueba que ese
    /// cliente siga tocando una sola capacidad. El día que le aparezca un cobro o un aviso, aquel
    /// gate cae y el vertical tiene que pasar por un orquestador, igual que Salud y Tienda.</para>
    /// </remarks>
    private const string ClienteDeUnaSolaCapacidad = "HttpVisitSchedulingService.cs";

    [Fact]
    public void El_CMS_no_llama_a_Api_Booking_de_frente()
    {
        // Lo que tiene que poner esto en rojo: resolver «me falta listar la agenda» con un GET a
        // Api.Booking. Funciona, y deja el CMS a un paso de agendar por su cuenta.
        var infracciones = new List<string>();

        foreach (var f in FuentesDelCms())
        {
            if (Path.GetFileName(f) == ClienteDeUnaSolaCapacidad) continue;

            var codigo = SinComentarios(f);
            foreach (var ruta in Prohibidas)
            {
                if (codigo.Contains($"\"{ruta}", StringComparison.OrdinalIgnoreCase)
                    || codigo.Contains($"/{ruta}", StringComparison.OrdinalIgnoreCase))
                {
                    infracciones.Add($"{Path.GetFileName(f)} → {ruta}");
                }
            }
        }

        Assert.True(infracciones.Count == 0,
            "El CMS está llamando a Api.Booking de frente: "
            + string.Join(", ", infracciones.Distinct(StringComparer.Ordinal))
            + ". Agendar va contra Bff.Salud (POST /v1/appointments): apartar el cupo, cobrar el "
            + "copago y avisar pueden fallar a la mitad.");
    }

    [Fact]
    public void El_cliente_de_salud_apunta_al_orquestador()
    {
        var cliente = File.ReadAllText(
            Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Services", "HttpClinicalSchedulingService.cs"));

        Assert.Contains("v1/appointments", cliente, StringComparison.Ordinal);
    }

    [Fact]
    public void El_id_interno_de_Api_Booking_NO_llega_al_CMS()
    {
        // El `resourceId` lo genera Api.Booking; nadie río arriba puede conocerlo. Que el CMS
        // deje de nombrarlo es lo que impide reinventar una convención que no puede acertar — y
        // lo que mantiene los identificadores internos de una capacidad dentro de su servicio.
        var cliente = File.ReadAllText(
            Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Services", "HttpClinicalSchedulingService.cs"));

        var codigo = SinComentarios(
            Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Services", "HttpClinicalSchedulingService.cs"));

        Assert.DoesNotContain("resourceId", codigo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("professionalId", cliente, StringComparison.Ordinal);
    }

    [Fact]
    public void El_stub_sigue_siendo_el_default()
    {
        // Un clon limpio tiene que arrancar con el portal clínico funcionando, sin levantar el
        // orquestador ni sus capacidades.
        var composer = File.ReadAllText(
            Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.PlatformAndHealthcare.cs"));

        Assert.Contains("StubClinicalSchedulingService", composer, StringComparison.Ordinal);
        Assert.Contains("\"Bff\"", composer, StringComparison.Ordinal);

        var settings = File.ReadAllText(
            Path.Combine(RepoRoot(), "Synergos.CMS.Application", "Configuration", "SaludSettings.cs"));
        Assert.Contains("Mode { get; init; } = \"Stub\"", settings, StringComparison.Ordinal);
    }
}
