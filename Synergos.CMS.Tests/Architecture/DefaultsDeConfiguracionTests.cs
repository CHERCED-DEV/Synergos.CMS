using System.Reflection;
using System.Text.RegularExpressions;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El default que un setting DICE es el que tiene (defecto #95).
/// </summary>
/// <remarks>
/// <para><b>El <c>&lt;summary&gt;</c> de un POCO de configuración es la interfaz que lee quien
/// configura un despliegue</b>, y se edita por separado del inicializador. Veinte de los
/// veintiún <c>bool</c> de <c>Configuration/</c> nombran su default en la prosa; uno lo nombraba
/// al revés —«False por privacy/compliance default» con el default en <c>true</c>— y llevaba así
/// sin que nada lo cruzara.</para>
///
/// <para><b>Es la forma de las cifras de <c>CLAUDE.md</c> antes de <c>CifrasDeClaudeMdTests</c></b>:
/// un dato afirmado en prosa, con su fuente de verdad a dos líneas, y nada que los enfrente. Y
/// duele más acá, porque quien lee ese comentario lo está leyendo <b>para decidir si tiene que
/// configurar algo</b> — el de privacidad concluía que ya estaba apagado.</para>
///
/// <para><b>Nombrar el default es OBLIGATORIO</b>, y esto cambió en #97. La primera versión no
/// lo exigía —«un setting puede no decirlo y estar bien; lo que no puede es decirlo mal»— y el
/// razonamiento es bueno salvo por su consecuencia: convierte <b>borrar la frase</b> en la forma
/// de dejar de vigilar un valor. Se comprobó mutando: quitarle « Default true.» a
/// <c>DashboardSettings.Enabled</c> dejaba el gate <b>verde</b> con un vigilado menos, y el único
/// cinturón —<c>conClaim &gt;= 15</c>— sólo salta a la sexta frase borrada. Es la forma de #88
/// dentro del gate que la cita.</para>
///
/// <para><b>Y veía 21 de 24</b>, también hasta #97. El patrón exigía <c>= true;</c>, y en C# un
/// <c>public bool X { get; init; }</c> <b>no lleva <c>;</c></b>: su default es <c>false</c>
/// implícito. Quedaban fuera tres, y dos documentan el suyo — entre ellos
/// <c>NotificationsSettings.Enabled</c>, el interruptor maestro, cuyo comentario explica que sin
/// config «nadie notifica». Invertir esa frase pasaba en verde. El <c>total &gt;= 20</c> tampoco
/// protegía: contaba 21.</para>
///
/// <para><b>El valor sale por REFLEXIÓN y la prosa del FICHERO</b>, que es lo que hace del gate un
/// cruce de verdad: dos fuentes distintas en vez de dos regex sobre el mismo texto. De paso
/// resuelve lo de arriba solo — el default implícito de un <c>bool</c> sin inicializador es
/// <c>false</c> y la reflexión lo dice sin que haya que deducirlo del literal.</para>
///
/// <para><b>Y una prosa que hable de un default y no se pueda leer pone ROJO</b>, en vez de
/// saltarse. Callarse porque no se entiende una frase es exactamente cómo un gate deja de vigilar
/// sin que nadie se entere.</para>
///
/// <para><b>Sólo se lee el <c>&lt;summary&gt;</c>, y esto tiene un caso concreto detrás.</b> Antes
/// se leía el bloque de documentación entero, y el <c>&lt;remarks&gt;</c> de
/// <c>IncludeMemberContext</c> <b>cita la prosa vieja</b> —«False por privacy/compliance
/// default»— para explicar qué estaba mal. Hoy no molesta sólo porque el summary afirma su default
/// en una forma que se comprueba antes; en cuanto el summary dejaba de afirmarlo, el gate leía la
/// cita del remarks y denunciaba una mentira <b>que no existe</b>, señalando un comentario que
/// dice la verdad sobre su propia historia. Comprobado mutando. El default se declara donde se
/// declara lo que la propiedad hace; los remarks son narrativa.</para>
/// </remarks>
public sealed class DefaultsDeConfiguracionTests
{
    /// <summary>
    /// Un <c>bool</c> de configuración con su documentación, lleve inicializador o no.
    /// </summary>
    /// <remarks>
    /// El valor NO se captura acá a propósito (#97): lo pone la reflexión. Este patrón sólo
    /// localiza la propiedad y su prosa.
    /// </remarks>
    private static readonly Regex Declaracion = new(
        @"(?<doc>(?:[ \t]*///[^\n]*\n)+)[ \t]*public\s+bool\s+(?<nombre>\w+)\s*"
        + @"\{\s*get;\s*(?:init|set);\s*\}\s*(?:=\s*(?:true|false)\s*;)?",
        RegexOptions.Compiled);

    /// <summary>
    /// La clase que envuelve cada propiedad, resuelta por posición.
    /// </summary>
    /// <remarks>
    /// Por posición y no por nombre de fichero: <c>AdminSettings.cs</c> y
    /// <c>PaymentsSettings.cs</c> declaran tres tipos cada uno.
    /// </remarks>
    private static readonly Regex Clase = new(@"\b(?:sealed\s+)?class\s+(\w+)", RegexOptions.Compiled);

    [Fact]
    public void Lo_que_el_comentario_dice_del_default_es_lo_que_el_default_es()
    {
        var prosaPorPropiedad = Prosa();
        var reales = Reales();
        var malas = new List<string>();
        var conClaim = 0;

        foreach (var (clase, propiedad, valor) in reales)
        {
            if (!prosaPorPropiedad.TryGetValue((clase, propiedad), out var prosa)) continue;

            var afirmado = DefaultAfirmado(prosa);
            if (afirmado is null)
            {
                // Dos motivos distintos y los dos van en rojo (#97): o no lo nombra —y entonces
                // borrar la frase sería la forma de dejar de vigilarlo—, o lo nombra en un idioma
                // que este gate no lee, que es dejar de vigilarlo callándose.
                malas.Add(Regex.IsMatch(prosa, @"\b(?:defaults?|defecto)\b", RegexOptions.IgnoreCase)
                    ? $"{clase} · {propiedad}: el summary habla de un default y la gramática de "
                      + "este gate no lo lee. Usá una de las formas que el repo ya usa, o añadí la "
                      + "tuya a DefaultAfirmado."
                      + Environment.NewLine + $"      «{Recorta(prosa)}»"
                    : $"{clase} · {propiedad}: el summary no nombra su default. Es una frase, y sin "
                      + "ella la propiedad sale del cruce SIN QUE NADA SE PONGA ROJO.");
                continue;
            }

            conClaim++;
            var real = valor ? "true" : "false";

            if (!string.Equals(afirmado, real, StringComparison.Ordinal))
            {
                malas.Add($"{clase} · {propiedad}: el comentario dice que el default es "
                    + $"`{afirmado}` y es `{real}`."
                    + Environment.NewLine + $"      «{Recorta(prosa)}»");
            }
        }

        Assert.True(malas.Count == 0,
            "Un setting dice en su comentario un default distinto del que tiene, o dejó de "
            + "decirlo (#95, #97). Ese `<summary>` es la interfaz que lee quien configura un "
            + "despliegue, y se edita por separado del inicializador — el que estaba mal afirmaba "
            + "una postura de PRIVACIDAD contraria a la real, así que se leía como «ya está "
            + "apagado»."
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", malas));

        // Cinturones, para el caso en que el descubrimiento se rompa entero: ahí no hay malas que
        // reportar —cada propiedad se salta por no tener prosa— y el gate quedaría verde vigilando
        // cero. Es el modo de fallo que ya costó una corrección en el gate del bridge (#89).
        Assert.True(reales.Count >= 20,
            $"Sólo se descubrieron {reales.Count} bools en Configuration/: el descubrimiento está roto.");
        Assert.True(conClaim >= 20,
            $"Sólo {conClaim} de {reales.Count} bools quedaron cruzados. Eran veinticuatro: si el "
            + "barrido del fichero deja de encontrar la prosa, este gate no vigila nada.");
    }

    /// <summary>
    /// El <c>&lt;summary&gt;</c> de cada <c>bool</c> de configuración, por (clase, propiedad).
    /// </summary>
    /// <remarks>
    /// Sólo el <c>&lt;summary&gt;</c>: los <c>&lt;remarks&gt;</c> son narrativa, y cuentan la
    /// historia de un valor —incluida la prosa vieja de #95, citada para explicarla—. Leerlos haría
    /// que el gate tomara una afirmación ya corregida como si fuera vigente.
    /// </remarks>
    private static Dictionary<(string Clase, string Propiedad), string> Prosa()
    {
        var carpeta = Path.Combine(RepoRoot(), "Synergos.CMS.Application", "Configuration");
        var salida = new Dictionary<(string, string), string>();

        foreach (var fichero in Directory.EnumerateFiles(carpeta, "*.cs", SearchOption.AllDirectories))
        {
            var texto = File.ReadAllText(fichero);
            var clases = Clase.Matches(texto).Select(m => (m.Index, Nombre: m.Groups[1].Value)).ToList();

            foreach (Match m in Declaracion.Matches(texto))
            {
                var duena = clases.LastOrDefault(c => c.Index < m.Index).Nombre;
                if (string.IsNullOrEmpty(duena)) continue;

                var bloque = Regex.Replace(m.Groups["doc"].Value, @"^[ \t]*///\s?", string.Empty,
                    RegexOptions.Multiline);
                var sumario = Regex.Match(bloque, "<summary>(.*?)</summary>", RegexOptions.Singleline);

                salida[(duena, m.Groups["nombre"].Value)] =
                    Limpia(sumario.Success ? sumario.Groups[1].Value : string.Empty);
            }
        }

        return salida;
    }

    /// <summary>
    /// Cada <c>bool</c> de un POCO de <c>Configuration/</c> con el valor que tiene de verdad.
    /// </summary>
    /// <remarks>
    /// Por reflexión y no leyendo el literal (#97): así el default implícito de un
    /// <c>public bool X { get; init; }</c> —que es <c>false</c>— se cruza igual de bien, y el gate
    /// pasa a enfrentar dos fuentes distintas en vez de dos regex sobre el mismo texto.
    /// </remarks>
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
    /// El default que la prosa afirma, o <c>null</c> si no afirma ninguno.
    /// </summary>
    /// <remarks>
    /// Las formas son las que hay escritas de verdad, no las que uno imaginaría: <c>Default true.</c>,
    /// <c>Si true (default false)</c>, <c>Si false (default),</c> y la que estaba mal,
    /// <c>False por … default</c>. El orden importa — <c>Si true (default false)</c> tiene los dos
    /// valores, y el que manda es el del paréntesis.
    ///
    /// <para><c>apagado</c> y <c>encendido</c> se añadieron en #97 y tampoco se inventaron:
    /// <c>PaymentRoutingSettings.Enabled</c> declara el suyo como «Default apagado a propósito» y
    /// sin leerlo quedaba fuera del cruce — flipar esa palabra pasaba en verde. No se añadieron
    /// <c>on</c>/<c>off</c>: no aparecen en el corpus, y vocabulario que nadie usa sólo abre falsos
    /// positivos.</para>
    /// </remarks>
    private static string? DefaultAfirmado(string prosa)
    {
        // 1) «(default false)» o «(default true)» — el paréntesis gana sobre el «Si X» de delante.
        var m = Regex.Match(prosa, @"\(\s*default\s+(true|false|apagado|encendido)\s*\)",
            RegexOptions.IgnoreCase);
        if (m.Success) return Palabra(m.Groups[1].Value);

        // 2) «Si false (default),» — el valor va ANTES y el paréntesis sólo marca cuál es.
        m = Regex.Match(prosa, @"\b(?:Si|Cuando|When|If)\s+(true|false)\s*\(\s*default\s*\)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToLowerInvariant();

        // 3) «Default true.» / «default false» / «Default apagado»
        m = Regex.Match(prosa, @"\bdefaults?\s+(?:es\s+|is\s+|a\s+)?(true|false|apagado|encendido)\b",
            RegexOptions.IgnoreCase);
        if (m.Success) return Palabra(m.Groups[1].Value);

        // 4) «False por privacy/compliance default» — el valor delante y «default» detrás. Es la
        //    forma del defecto #95, y se reconoce a propósito: si el gate no la entendiera, el
        //    único caso que existía no lo habría cazado.
        //
        //    Exige el «por» pegado al valor desde #97. Sin él —«(true|false) … default» a 60
        //    caracteres— se lleva por delante el «Si true» del condicional en vez del default
        //    declarado, y devuelve el valor equivocado: comprobado mutando, el defecto de #95
        //    reescrito en una sola frase pasaba EN VERDE porque el patrón leía el «true» de
        //    «Si true» y no el «False» de «False por … default».
        m = Regex.Match(prosa, @"\b(true|false|apagado|encendido)\s+por\s+[^.]{0,60}?\bdefault\b",
            RegexOptions.IgnoreCase);
        if (m.Success) return Palabra(m.Groups[1].Value);

        return null;
    }

    /// <summary>
    /// Las dos palabras con las que el repo nombra cada valor, normalizadas.
    /// </summary>
    private static string Palabra(string token) => token.ToLowerInvariant() switch
    {
        "true" or "encendido" => "true",
        _ => "false",
    };

    private static string Limpia(string doc)
    {
        var texto = string.Join(' ', doc.Split('\n')
            .Select(l => l.Trim().TrimStart('/').Trim())
            .Where(l => l.Length > 0));
        return Regex.Replace(texto, "<[^>]+>", " ").Replace("  ", " ").Trim();
    }

    private static string Recorta(string s) => s.Length <= 130 ? s : s[..130] + "…";

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
}
