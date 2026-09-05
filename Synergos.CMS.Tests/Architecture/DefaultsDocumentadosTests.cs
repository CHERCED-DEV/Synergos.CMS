using System.Reflection;
using System.Text.RegularExpressions;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El default que un setting NOMBRA en su documentación es el default que tiene.
/// </summary>
/// <remarks>
/// <para><b>Por qué existe</b> (#95). <c>HostBridgeSettings.IncludeMemberContext</c> se
/// documentaba como «False por privacy/compliance default» y valía <c>true</c>. No había
/// override en ningún <c>appsettings*</c>, así que todo despliegue emitía el bloque
/// <c>member</c> —clave, correo y roles— mientras el único texto del repo que describía la
/// postura de privacidad de ese bloque afirmaba la contraria.</para>
///
/// <para><b>Y no era un descuido puntual: era una superficie sin vigilar.</b> El default de
/// un setting es prosa en un <c>&lt;summary&gt;</c>, se edita por separado del valor, y es la
/// interfaz que lee quien configura un despliegue. Al barrer los POCOs de
/// <c>Configuration/</c> —medido, no recordado, que es el error de #81 y #83— salen
/// <b>24</b> bools públicos, <b>23</b> nombraban su default en la prosa y <b>exactamente
/// uno</b> lo nombraba mal. Un cruce que cubre veintitrés de veinticuatro no es una regla
/// escrita para un caso.</para>

/// <para><b>Y nombrarlo es obligatorio, no opcional.</b> Que la frase falte no puede ser una
/// salida: la primera versión de este gate se saltaba las propiedades cuyo summary no hablaba
/// de defaults, así que <b>borrar la frase</b> era una forma silenciosa de dejar de vigilar el
/// valor — se comprobó mutando, y el gate se quedó verde con una propiedad menos. Es el hueco
/// de #88 otra vez, esta vez dentro del gate que lo cita. De los veinticuatro sólo
/// <c>HostBridgeSettings.IncludePageMetadata</c> no la tenía; ahora la tiene, y la regla no
/// tiene excepciones que negociar.</para>
///
/// <para><b>La segunda mitad del defecto es que nada fijaba el valor.</b> Las dos únicas
/// apariciones de <c>IncludeMemberContext</c> en el repo eran su declaración y su uso;
/// ningún test lo mencionaba. Ponerlo en <c>false</c> dejaba
/// <c>window.synergos.member</c> en <c>null</c> en todo el sitio con la suite entera en
/// verde. <b>Este gate es el pin, y lo es para los veinticuatro</b>: cambiar un valor sin tocar
/// su prosa pone rojo, y tocar la prosa es lo que hace que alguien mire la consecuencia. Un
/// assert por propiedad habría fijado una y dejado veinte sueltas.</para>
///
/// <para><b>Sólo se lee el <c>&lt;summary&gt;</c>, a propósito.</b> Los <c>&lt;remarks&gt;</c>
/// son narrativa —cuentan la historia de un valor, y citar la prosa vieja de #95 dentro de
/// uno haría que el gate leyera la afirmación equivocada como si fuera vigente—. El default
/// se declara donde se declara lo que la propiedad hace. Un default que se mude a los
/// remarks deja de vigilarse, y por eso el otro gate de acá exige que el summary lo
/// nombre.</para>
///
/// <para><b>Y el gate se defiende de quedarse ciego</b>, que es la lección de #88: allá un
/// parser dejó de ver los campos con valor por defecto y <b>siguió verde</b>. Acá el valor
/// sale por <b>reflexión</b> y la prosa del <b>fichero</b>, así que son dos fuentes de
/// verdad distintas; toda propiedad que la reflexión encuentre tiene que aparecer también en
/// el barrido del fichero, y toda prosa que hable de un default tiene que ser legible por la
/// gramática. Callarse porque no se entiende una frase es la forma de fallo que este repo
/// paga más cara.</para>
/// </remarks>
public sealed class DefaultsDocumentadosTests
{
    /// <summary>
    /// Las formas con las que este repo declara un default, medidas contra el corpus real.
    /// </summary>
    /// <remarks>
    /// No se inventaron: salen de leer las veinticuatro declaraciones que ya existían. La segunda es
    /// la misma afirmación escrita al revés —«False por … default»— y es la que llevaba el
    /// defecto, así que quitarla dejaría pasar exactamente el caso que originó el gate.
    /// </remarks>
    private static readonly Regex[] Formas =
    {
        // «Default true», «Default: false», «por defecto true», «Default apagado»
        new(@"\b(?:default|por\s+defecto)\s*:?\s*\b(true|false|apagado|encendido)\b",
            RegexOptions.IgnoreCase),
        // «False por privacy/compliance default» — la afirmación al revés
        new(@"\b(true|false)\b\s+por\s+[^.]{0,60}?\b(?:default|defecto)\b", RegexOptions.IgnoreCase),
        // «Si false (default), …» · «When false (default), …» — el valor es el del condicional
        new(@"\b(?:si|cuando|when)\s+(true|false)\s*\(\s*(?:default|por\s+defecto)\s*\)",
            RegexOptions.IgnoreCase),
    };

    private static readonly Regex Declaracion = new(
        @"((?:^[ \t]*///.*\r?\n)*)[ \t]*public\s+bool\s+(\w+)\s*\{\s*get;\s*(?:init|set);\s*\}\s*(?:=\s*(?:true|false)\s*;)?",
        RegexOptions.Multiline);

    private static readonly Regex Clase = new(@"\b(?:sealed\s+)?class\s+(\w+)");

    private static readonly Regex Sumario = new(@"<summary>(.*?)</summary>", RegexOptions.Singleline);

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

    private static string Carpeta() =>
        Path.Combine(RepoRoot(), "Synergos.CMS.Application", "Configuration");

    /// <summary>
    /// El <c>&lt;summary&gt;</c> de cada bool documentado, por (clase, propiedad).
    /// </summary>
    /// <remarks>
    /// La clase se resuelve por posición y no por nombre de fichero: <c>AdminSettings.cs</c> y
    /// <c>PaymentsSettings.cs</c> declaran tres tipos cada uno, así que «un tipo por fichero»
    /// habría dejado cuatro tipos fuera del barrido sin decirlo.
    /// </remarks>
    private static Dictionary<(string Clase, string Propiedad), string> Prosa()
    {
        var salida = new Dictionary<(string, string), string>();

        foreach (var ruta in Directory.EnumerateFiles(Carpeta(), "*.cs", SearchOption.AllDirectories))
        {
            var texto = File.ReadAllText(ruta);
            var clases = Clase.Matches(texto).Select(m => (m.Index, Nombre: m.Groups[1].Value)).ToList();

            foreach (Match m in Declaracion.Matches(texto))
            {
                var duena = clases.LastOrDefault(c => c.Index < m.Index).Nombre;
                if (string.IsNullOrEmpty(duena)) continue;

                var bloque = Regex.Replace(m.Groups[1].Value, @"^[ \t]*///\s?", string.Empty,
                    RegexOptions.Multiline);
                var sumario = Sumario.Match(bloque);
                var doc = sumario.Success ? sumario.Groups[1].Value : string.Empty;

                salida[(duena, m.Groups[2].Value)] = Regex.Replace(
                    Regex.Replace(doc, @"<[^>]+>", " "), @"\s+", " ").Trim();
            }
        }

        return salida;
    }

    /// <summary>
    /// Cada bool de un POCO de <c>Configuration/</c> con el valor que tiene de verdad.
    /// </summary>
    private static List<(string Clase, string Propiedad, bool Valor)> Reales()
    {
        var salida = new List<(string, string, bool)>();

        foreach (var tipo in typeof(HostBridgeSettings).Assembly.GetTypes())
        {
            if (tipo.Namespace is null
                || !tipo.Namespace.StartsWith("Synergos.CMS.Application.Configuration", StringComparison.Ordinal)
                || tipo.IsAbstract || tipo.IsInterface || tipo.IsGenericTypeDefinition
                || tipo.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }

            var instancia = Activator.CreateInstance(tipo);
            if (instancia is null) continue;

            foreach (var prop in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.PropertyType != typeof(bool) || prop.GetMethod is null) continue;
                salida.Add((tipo.Name, prop.Name, (bool)prop.GetValue(instancia)!));
            }
        }

        return salida;
    }

    /// <summary>
    /// Lo que la prosa nombra como default, o <c>null</c> si no nombra ninguno.
    /// </summary>
    private static bool[] Lecturas(string doc) =>
        Formas
            .SelectMany(f => f.Matches(doc).Select(m => Valor(m.Groups[1].Value)))
            .Distinct()
            .ToArray();

    /// <summary>
    /// Las palabras con las que el repo nombra los dos valores.
    /// </summary>
    /// <remarks>
    /// <c>apagado</c> y <c>encendido</c> no se añadieron por si acaso: <c>Default apagado a
    /// propósito</c> es como <c>PaymentRoutingSettings.Enabled</c> declara el suyo, y sin
    /// leerlo el gate se quedaba sin vigilarlo. Inventar vocabulario que nadie usa —«on»,
    /// «off»— sólo abre falsos positivos.
    /// </remarks>
    private static bool Valor(string palabra) => palabra.ToLowerInvariant() switch
    {
        "true" or "encendido" => true,
        "false" or "apagado" => false,
        _ => throw new InvalidOperationException($"Valor no previsto en la gramática: {palabra}"),
    };

    /// <summary>
    /// La prosa dice el valor que hay.
    /// </summary>
    [Fact]
    public void Todo_default_documentado_en_Configuration_nombra_el_valor_real()
    {
        var prosa = Prosa();
        var reales = Reales();

        // Red de seguridad: si el descubrimiento se rompe, esto recorrería una lista vacía y
        // quedaría verde vigilando nada. Es lo que hace SuiteCountTests con su `> 2000`.
        Assert.True(reales.Count >= 15,
            $"Sólo se descubrieron {reales.Count} bools en Configuration/: revisar este gate "
            + "antes que los settings.");

        var fallos = new List<string>();
        var vigiladas = 0;

        foreach (var (clase, propiedad, valor) in reales)
        {
            if (!prosa.TryGetValue((clase, propiedad), out var doc)) continue;

            if (!Regex.IsMatch(doc, @"\b(default|defecto)\b", RegexOptions.IgnoreCase))
            {
                fallos.Add($"{clase}.{propiedad}: el summary no nombra su default. Es una frase, "
                    + "y sin ella la propiedad sale del cruce SIN QUE NADA SE PONGA ROJO — que es "
                    + "la forma de fallo que este gate viene a cerrar.");
                continue;
            }

            var lecturas = Lecturas(doc);
            if (lecturas.Length == 0)
            {
                fallos.Add($"{clase}.{propiedad}: el summary habla de un default y la gramática de "
                    + "este gate no lo lee. Usá una de las formas que el repo ya usa («Default X», "
                    + "«Default apagado», «Si X (default)») o añadí la tuya a Formas — dejarlo sin "
                    + "leer sería dejar de vigilarlo en silencio.");
                continue;
            }

            if (lecturas.Length > 1)
            {
                fallos.Add($"{clase}.{propiedad}: el summary se contradice solo, nombra "
                    + $"{string.Join(" y ", lecturas.Select(l => l.ToString().ToLowerInvariant()))} "
                    + "como default.");
                continue;
            }

            vigiladas++;
            if (lecturas[0] != valor)
            {
                fallos.Add($"{clase}.{propiedad}: el summary dice que el default es "
                    + $"{lecturas[0].ToString().ToLowerInvariant()} y vale "
                    + $"{valor.ToString().ToLowerInvariant()}.");
            }
        }

        Assert.True(fallos.Count == 0,
            "El default que un setting documenta es el que lee quien configura un despliegue, y "
            + "se edita por separado del valor:\n  " + string.Join("\n  ", fallos));

        // Último cinturón, para el caso en que el barrido del fichero devuelva NADA: ahí no hay
        // fallos que reportar —cada propiedad se salta por no tener prosa— y el gate quedaría
        // verde sin haber cruzado una sola.
        Assert.True(vigiladas >= 15,
            $"Sólo {vigiladas} defaults quedaron cruzados. Eran veinticuatro: si el barrido del "
            + "fichero deja de encontrar la prosa, este gate no vigila nada.");
    }

    /// <summary>
    /// El barrido del fichero ve todo lo que ve la reflexión.
    /// </summary>
    /// <remarks>
    /// Sin esto, un cambio de sintaxis —una propiedad con cuerpo, un default movido al
    /// constructor, un <c>required</c> por delante— haría que el otro gate dejara de mirar esa
    /// propiedad <b>sin ponerse rojo</b>. Es exactamente lo que le pasó al parser de #88: dejó
    /// de ver los campos con valor por defecto y siguió verde. Cubre las dos formas —con
    /// inicializador literal y sin él, que en C# significa <c>false</c>— porque
    /// <c>PaymentsSettings.SimulateRequiresAction</c> documenta «Default false» y no lleva
    /// inicializador: exigirlo habría dejado tres propiedades fuera del cruce.
    /// </remarks>
    [Fact]
    public void El_barrido_de_Configuration_no_se_puede_quedar_ciego_en_silencio()
    {
        var prosa = Prosa();
        var reales = Reales();

        var fuentes = string.Join("\n",
            Directory.EnumerateFiles(Carpeta(), "*.cs", SearchOption.AllDirectories)
                .Select(File.ReadAllText));

        var perdidas = reales
            .Where(r => !prosa.ContainsKey((r.Clase, r.Propiedad)))
            .Where(r => Regex.IsMatch(fuentes,
                $@"public\s+bool\s+{Regex.Escape(r.Propiedad)}\s*\{{[^}}]*\}}\s*(?:=\s*(?:true|false)\s*;)?"))
            .Select(r => $"{r.Clase}.{r.Propiedad}")
            .ToList();

        Assert.True(perdidas.Count == 0,
            "Estas propiedades están declaradas en el fichero y el barrido no las ve, "
            + "así que su documentación dejó de vigilarse sin que nada se pusiera rojo: "
            + string.Join(" · ", perdidas));

        // Y al revés: un tipo que la reflexión no puede instanciar —porque perdió su
        // constructor sin parámetros— desaparecería del gate de arriba con su prosa intacta.
        var invisibles = prosa.Keys
            .Where(k => !reales.Any(r => r.Clase == k.Clase && r.Propiedad == k.Propiedad))
            .Select(k => $"{k.Clase}.{k.Propiedad}")
            .ToList();

        Assert.True(invisibles.Count == 0,
            "Estas propiedades están documentadas en el fichero y la reflexión no las alcanza, "
            + "así que su default no se cruza contra nada: " + string.Join(" · ", invisibles));
    }
}
