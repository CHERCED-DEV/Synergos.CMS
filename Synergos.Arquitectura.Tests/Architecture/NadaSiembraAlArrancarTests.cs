using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Synergos.CMS.Web.Composers;
using Umbraco.Cms.Core.Composing;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Lo que corre SOLO al arrancar el CMS —sus <see cref="IHostedService"/>— y la regla de que
/// nada de eso siembra datos de demo (ADR 0013, #176).
/// </summary>
/// <remarks>
/// <para><b>El defecto.</b> <c>BlogsDemoSeedHostedService</c> y
/// <c>GovCorrespondenceSeedHostedService</c> se registraban sin condición y escribían, en CADA
/// arranque y en todo entorno, conversaciones y guardados de demo en la mensajería y las
/// colecciones GENÉRICAS —las que comparten Blogs, Salud, Gobierno y Tienda—, durables desde la
/// ADR 0105. Su <c>remarks</c> decía que «solo hidrataban stubs en memoria»: era verdad el día
/// que se escribió y dejó de serlo sin que nadie lo releyera. Y la de Gobierno ni siquiera era
/// idempotente sobre el almacén durable: cada reinicio le sumaba tres mensajes a la carpeta de
/// dos ciudadanos.</para>
///
/// <para><b>Por qué no se arregló «registrándolos sólo con el flag».</b> Es lo que pedía el
/// ticket, y la ADR 0013 lo descarta por escrito: la siembra de desarrollo «se dispara por
/// invocación explícita» y «nunca auto-ejecuta aunque el flag esté en <c>true</c>» —la
/// alternativa «flag + auto-run si está activo» figura como RECHAZADA—. Así que la siembra pasó
/// a <c>POST /dev/seed-blogs-demo</c> y <c>POST /dev/seed-gov-correspondence</c>, detrás del
/// flag como las demás herramientas de <c>DevController</c>, y lo que este gate exige es más
/// fuerte que «detrás del flag»: <b>ningún hosted service siembra, con el flag como sea</b>.</para>
///
/// <para><b>Se mide de TRES maneras que tienen que coincidir</b> (regla 3 del contexto de las
/// olas: una cifra que decide sale de dos métodos, y un grep es una hipótesis): la FUENTE de
/// los composers —ve todo registro, también los condicionales—, la REFLEXIÓN sobre el
/// ensamblado —ve todo tipo que PUEDE ser hosted service— y la COMPOSICIÓN DE VERDAD —corre los
/// composers contra una configuración y le pregunta al contenedor, que es lo único que no se
/// engaña con un registro comentado (la lección de <c>BarridoSegregationTests</c>)—.</para>
/// </remarks>
public sealed class NadaSiembraAlArrancarTests
{
    /// <summary>
    /// Los hosted services legítimos: lo que hacen al arrancar, y por qué eso NO es sembrar.
    /// </summary>
    /// <remarks>
    /// <b>Es un censo vigilado en los dos sentidos</b>: uno nuevo que no esté acá rompe el build
    /// —alguien tiene que decidir si siembra— y uno de acá que ya no se registre, también, para
    /// que la lista no acabe afirmando lo que ya no existe (#137). Y entrar al censo no exime:
    /// <see cref="Ningun_hosted_service_siembra_datos"/> los mira a todos, del censo o no.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Legitimos = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["BundleRegistryWarmupHostedService"] =
            "construye el cliente del registry al arrancar para que su estado salga en el log; no escribe nada",
        ["CartAbandonmentScannerHostedService"] =
            "barre los carritos que dejaron los compradores y emite su evento y su aviso; lee lo que pasó",
        ["CartSecretKeyValidationHostedService"] =
            "avisa en el log si la llave del carrito sigue siendo la de desarrollo; no escribe datos",
        ["DashboardSnapshotFlushHostedService"] =
            "vuelca a JSONL la proyección de métricas que ya está en memoria",
        ["HoldExpirationScannerHostedService"] =
            "libera los apartados vencidos del motor de reservas: cambia estado real, no inventa",
        ["HttpSearchAnalyticsStore"] =
            "drena hacia Api.Sessions la cola de búsquedas de los visitantes (sólo con Mode=Api)",
        ["RetentionSweepHostedService"] =
            "purga lo vencido según cada IRetentionPolicy: borra, no siembra",
        ["SqliteMaintenanceHostedService"] =
            "checkpoint del WAL y optimize de SQLite: mantenimiento, sin datos",
        ["WebhookTelemetryAlertHostedService"] =
            "avisa cuando un canal de webhooks falla por encima del umbral",
    };

    /// <summary>La huella de sembrar: nombrar un sembrador o la semilla de una demo.</summary>
    /// <remarks>
    /// Tosca a propósito, como los demás gates del repo: no atrapa a un adversario, atrapa el
    /// atajo de un martes. Un hosted service que escribiera demo a mano sin nombrar nada de esto
    /// se escaparía de este diente — y no del censo, que es la defensa de verdad.
    /// </remarks>
    private static readonly Regex HuellaDeSiembra = new(
        @"\b\w+Seeder\b|\b\w*DemoSeed\b|\bSeed(?:Async)?\s*\(", RegexOptions.Compiled);

    // ── Lectura ─────────────────────────────────────────────────────────────

    /// <summary>El fichero SIN comentarios: un registro comentado no registra nada.</summary>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal)
                || t.StartsWith("///", StringComparison.Ordinal)
                || t.StartsWith('*')
                || t.StartsWith("/*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));

    private static IEnumerable<string> FuentesDeWeb()
        => Directory.EnumerateFiles(Proyectos.Ruta("Synergos.CMS.Web"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>Los ficheros donde se cablea: los composers y el <c>Program.cs</c>.</summary>
    private static IReadOnlyList<(string Nombre, string Codigo)> Cableado()
        => Directory.EnumerateFiles(Proyectos.Ruta("Synergos.CMS.Web", "Composers"), "*.cs")
            .Append(Proyectos.Ruta("Synergos.CMS.Web", "Program.cs"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), SinComentarios(f)))
            .ToList();

    private static readonly Regex[] FormasDeRegistro =
    [
        new(@"AddHostedService\s*<\s*(?:[\w.]+\.)?(?<tipo>\w+)\s*>"),
        new(@"AddHostedService\s*\(\s*\w+\s*=>\s*\w+\.GetRequiredService\s*<\s*(?:[\w.]+\.)?(?<tipo>\w+)\s*>"),
        new(@"<\s*IHostedService\s*,\s*(?:[\w.]+\.)?(?<tipo>\w+)\s*>"),
    ];

    /// <summary>Método 1 — la FUENTE: todo tipo que un composer registra como hosted service.</summary>
    private static IReadOnlyDictionary<string, string> RegistradosEnLaFuente()
    {
        var registrados = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (nombre, codigo) in Cableado())
        {
            foreach (var forma in FormasDeRegistro)
            {
                foreach (Match m in forma.Matches(codigo))
                {
                    registrados.TryAdd(m.Groups["tipo"].Value, nombre);
                }
            }
        }
        return registrados;
    }

    /// <summary>Método 2 — la REFLEXIÓN: todo tipo del producto web que PUEDE ser hosted service.</summary>
    private static IReadOnlyList<string> ImplementacionesEnElEnsamblado()
        => typeof(SeamComposer).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IHostedService).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Método 3 — la COMPOSICIÓN: corre de verdad TODOS los composers del producto web contra
    /// <paramref name="configuracion"/> y devuelve los hosted services que quedaron registrados.
    /// </summary>
    /// <remarks>
    /// Un registro por fábrica (<c>AddHostedService(sp =&gt; sp.GetRequiredService&lt;T&gt;())</c>)
    /// no trae <c>ImplementationType</c>: se lee el tipo que devuelve la fábrica.
    /// </remarks>
    private static IReadOnlyList<string> HostedServicesCompuestos(IReadOnlyDictionary<string, string?> configuracion)
    {
        var services = new ServiceCollection();
        var builder = Substitute.For<IUmbracoBuilder>();
        builder.Services.Returns(services);
        builder.Config.Returns(new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build());

        foreach (var composer in typeof(SeamComposer).Assembly.GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IComposer).IsAssignableFrom(t))
                     .OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            ((IComposer)Activator.CreateInstance(composer)!).Compose(builder);
        }

        return services
            .Where(d => !d.IsKeyedService && d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType?.Name
                         ?? d.ImplementationFactory?.Method.ReturnType.Name
                         ?? d.ImplementationInstance?.GetType().Name
                         ?? "¿?")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>El fichero donde se declara <paramref name="tipo"/>, sin comentarios.</summary>
    private static string? FuenteDe(string tipo)
    {
        var declaracion = new Regex(@"\b(?:class|record)\s+" + Regex.Escape(tipo) + @"\b");
        return FuentesDeWeb()
            .Where(f => declaracion.IsMatch(File.ReadAllText(f)))
            .Select(SinComentarios)
            .FirstOrDefault();
    }

    /// <summary>Las configuraciones que se componen: el clon limpio y el flag de siembra ENCENDIDO.</summary>
    /// <remarks>
    /// La segunda es la que importa: es la forma exacta de «registrarlo sólo con el flag», que la
    /// ADR 0013 rechaza. Con el flag encendido tampoco puede aparecer un sembrador.
    /// </remarks>
    private static IEnumerable<(string Nombre, Dictionary<string, string?> Config)> Configuraciones()
    {
        yield return ("clon limpio", new Dictionary<string, string?>());
        yield return ("Synergos:DevSeed:Enabled=true", new Dictionary<string, string?>
        {
            ["Synergos:DevSeed:Enabled"] = "true",
        });
    }

    // ── Que el gate VE ──────────────────────────────────────────────────────

    [Fact]
    public void El_descubrimiento_ve_todos_los_hosted_services_por_los_tres_caminos()
    {
        // Va primero, como en MoldeDelVerticalTests: sin él, un cambio de forma en el registro
        // (un TryAddEnumerable, un helper) deja a los demás facts en verde sobre una lista vacía.
        var fuente = RegistradosEnLaFuente();
        var reflexion = ImplementacionesEnElEnsamblado();

        Assert.True(fuente.Count >= 8,
            $"La fuente de los composers dio {fuente.Count} hosted service(s): el descubrimiento está "
            + "roto y el resto de este gate pasaría en verde sin mirar nada.");

        // Fuente ↔ reflexión, en los dos sentidos. Un tipo que implementa IHostedService y
        // ningún composer registra es código muerto — o un registro que este gate no sabe leer,
        // que es peor porque se escaparía del censo.
        var sinRegistro = reflexion.Where(t => !fuente.ContainsKey(t)).ToList();
        var sinTipo = fuente.Keys.Where(t => !reflexion.Contains(t, StringComparer.Ordinal)).ToList();

        Assert.True(sinRegistro.Count == 0,
            "Estos tipos implementan IHostedService y ningún composer los registra (o los registra de "
            + $"una forma que este gate no lee): {string.Join(", ", sinRegistro)}.");
        Assert.True(sinTipo.Count == 0,
            "La fuente registra como hosted service tipos que no lo son en el ensamblado: "
            + string.Join(", ", sinTipo) + ". Revisar las FormasDeRegistro de este gate.");

        // Y la composición de verdad no puede registrar nada que la fuente no haya visto.
        foreach (var (nombre, config) in Configuraciones())
        {
            var fuera = HostedServicesCompuestos(config).Where(t => !fuente.ContainsKey(t)).ToList();
            Assert.True(fuera.Count == 0,
                $"Con {nombre}, los composers registran hosted services que la lectura de la fuente "
                + $"no ve: {string.Join(", ", fuera)}.");
        }
    }

    // ── La regla ────────────────────────────────────────────────────────────

    [Fact]
    public void Todo_hosted_service_esta_en_el_censo_y_el_censo_no_sobra()
    {
        var registrados = RegistradosEnLaFuente();

        var nuevos = registrados
            .Where(r => !Legitimos.ContainsKey(r.Key))
            .Select(r => $"{r.Key} ({r.Value})")
            .ToList();

        Assert.True(nuevos.Count == 0,
            "Estos hosted services no están en el censo de NadaSiembraAlArrancarTests: "
            + string.Join(", ", nuevos) + ". Algo que corre SOLO al arrancar tiene que decidirse: "
            + "si siembra datos de demo, la ADR 0013 no lo deja correr al arrancar NI CON EL FLAG "
            + "ENCENDIDO —va a un endpoint de DevController detrás de Synergos:DevSeed:Enabled, como "
            + "/dev/seed-blogs-demo—; si no siembra, anotalo en el censo con lo que hace.");

        var sobran = Legitimos.Keys.Where(l => !registrados.ContainsKey(l)).ToList();

        Assert.True(sobran.Count == 0,
            "El censo nombra hosted services que ya no se registran: " + string.Join(", ", sobran)
            + ". Un censo vigilado en un solo sentido acaba afirmando lo que ya no existe (#137).");
    }

    [Fact]
    public void Ningun_hosted_service_siembra_datos()
    {
        // Incluye a los del censo a propósito: si no, la salida barata de un rojo en el fact de
        // arriba sería copiar el sembrador a la lista con una razón bonita.
        var sinFuente = new List<string>();
        var siembran = new List<string>();

        foreach (var tipo in RegistradosEnLaFuente().Keys)
        {
            var fuente = FuenteDe(tipo);
            if (fuente is null)
            {
                sinFuente.Add(tipo);
                continue;
            }

            var huella = HuellaDeSiembra.Match(fuente);
            if (huella.Success)
            {
                siembran.Add($"{tipo} (nombra «{huella.Value.Trim()}»)");
            }
        }

        Assert.True(sinFuente.Count == 0,
            "No se encontró la declaración de: " + string.Join(", ", sinFuente)
            + ". Sin su fuente este fact no puede mirar qué hace, y pasaría en verde sin mirar.");

        Assert.True(siembran.Count == 0,
            "Estos hosted services siembran al arrancar: " + string.Join("; ", siembran)
            + ". ADR 0013: cero siembra en el arranque, y el tooling de desarrollo «nunca "
            + "auto-ejecuta aunque el flag esté en true». La siembra va a un endpoint de "
            + "DevController detrás de Synergos:DevSeed:Enabled (#176).");
    }

    [Fact]
    public void Con_el_flag_de_siembra_encendido_tampoco_arranca_un_sembrador()
    {
        // La composición de verdad, con el flag como lo trae el perfil de desarrollo. Es el
        // diente contra «registrarlo sólo si Synergos:DevSeed:Enabled»: la fuente lo vería como
        // un registro más, y esto lo ve arrancar.
        foreach (var (nombre, config) in Configuraciones())
        {
            var mal = HostedServicesCompuestos(config)
                .Where(t => !Legitimos.ContainsKey(t)
                         || (FuenteDe(t) is { } f && HuellaDeSiembra.IsMatch(f)))
                .ToList();

            Assert.True(mal.Count == 0,
                $"Con {nombre} arrancan hosted services fuera del censo o que siembran: "
                + string.Join(", ", mal) + " (ADR 0013, #176).");
        }
    }
}
