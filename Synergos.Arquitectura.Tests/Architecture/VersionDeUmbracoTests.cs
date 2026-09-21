using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La versión de Umbraco está clavada en la rama 13 LTS, y los sitios que la AFIRMAN dicen
/// la misma (#149).
/// </summary>
/// <remarks>
/// <para><b>Por qué existe.</b> ADR 0001 prohíbe una cosa —subir a 14+— y hasta el #149 eso
/// era <b>sólo prosa</b>: nada del repo impedía que un <c>Version="14.0.0"</c> entrara en
/// <c>Directory.Packages.props</c> y compilara. El primer test de acá es esa prohibición
/// convertida en build rojo, que es la forma que este repo ya usa para todo lo demás.</para>
///
/// <para><b>Y el segundo cierra la deriva que el propio #149 destapó.</b> La versión estaba
/// escrita a mano en <b>siete</b> sitios y <b>ninguno los cruzaba</b>: las tres suites pasaban
/// en verde con <c>Directory.Packages.props</c> ya en 13.16.2 y <c>CLAUDE.md</c> §1, la skill
/// de guardrails y la tabla de la ADR todavía diciendo 13.13.1. Es
/// <c>feedback_a_named_list_beats_a_count</c> aplicado a una versión: una cifra que vive en
/// varios ficheros y no la deriva nadie se desvía, y acá lo que se desvía es lo primero que
/// lee quien entra al proyecto.</para>
///
/// <para><b>Se distingue AFIRMAR de NARRAR, y es el corte que hace que esto no sea ruido.</b>
/// <c>CHANGELOG.md</c> y la ADR 0093 nombran <c>13.13.1</c> porque describen mediciones hechas
/// <i>contra esa versión</i>; reescribirlas para que cuadren con el pin de hoy sería peor que
/// una cifra vieja — sería falsear el registro de lo que alguien midió. Por eso el censo de
/// abajo sólo lleva a los que dicen «la versión clavada ES», no a los que cuentan su historia.
/// </para>
///
/// <para><b>Se ancla a la FRASE y no al fichero, y eso lo destapó mutarlo.</b> La primera
/// versión de este gate hacía <c>File.ReadAllText(ruta).Contains(version)</c>. Con el pin
/// mutado a <c>14.0.0</c> —la mutación que prueba el primer diente— <b>dos de los cinco tests
/// pasaron en VERDE</b>: tanto <c>CLAUDE.md</c> como esta ADR contienen la cadena
/// <c>Version="14.0.0"</c> <i>dentro de la prosa que explica la prohibición</i>. O sea que el
/// gate se engañaba con su propia explicación, que es exactamente lo que
/// <c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c> avisa y lo que el #136 ya
/// había pagado con los <c>&lt;remarks&gt;</c> que citan las formas prohibidas. Hoy cada
/// entrada del censo lleva su <b>plantilla</b>: la versión tiene que aparecer <i>en la frase
/// que la afirma</i>. Y la frase es además el ancla —si alguien la reescribe, esto se cae y le
/// obliga a mirar el número, que es justo cuando conviene mirarlo—, el mismo movimiento que
/// <see cref="CifrasDeClaudeMdTests"/>.</para>
///
/// <para><b>Lo que este gate NO hace, dicho para no mentir sobre su alcance.</b> No encuentra
/// un fichero <b>nuevo</b> que empiece a escribir la versión a mano. Hacerlo exigiría barrer la
/// prosa del repo entero con un regex que distinga «Umbraco.Cms 13.16.2» de «uSync 13.3.2», que
/// está dos líneas más abajo en el mismo fichero — y un barrido frágil sobre prosa es
/// exactamente lo que sale con un número plausible que nadie cruza. Se prefiere un gate corto
/// que sí cumple a uno ancho del que alguien deje de desconfiar.</para>
///
/// <para><b>Red de seguridad</b>: si el censo se vacía o la versión no se puede derivar, el
/// gate <b>falla</b> en vez de pasar sobre una lista vacía — el modo de fallo que el #136 midió
/// (12/12 en verde sin mirar un solo proyecto).</para>
/// </remarks>
public sealed class VersionDeUmbracoTests
{
    /// <summary>
    /// Los sitios que afirman <b>cuál es el pin de hoy</b>: su fichero, la <b>frase</b> que lo
    /// afirma —con <c>{0}</c> donde va la versión— y la razón por la que está en el censo.
    /// No van los que narran el pasado; ver el <c>remarks</c> de la clase.
    /// </summary>
    public static TheoryData<string, string, string> AfirmanLaVigente() => new()
    {
        {
            "CLAUDE.md",
            "Umbraco {0} — **no upgrade** a 14+ sin ADR nuevo.",
            "§1 es lo primero que lee un agente; decía 13.13.1 desde el andamiaje"
        },
        {
            "Synergos.CMS.Web/docs/adr/0001-umbraco-13-lts-pin.md",
            "the pin is now `{0}`",
            "la nota que enmienda la Decision tiene que nombrar el pin vigente"
        },
        {
            ".claude/skills/synergos-guardrails/SKILL.md",
            "**{0}, NO upgrade a 14+**",
            "§7 la afirma, y es la skill que se lee ANTES de proponer cualquier cambio"
        },
        {
            ".claude/skills/synergos-guardrails/SKILL.md",
            "Pinned {0} (ADR 0001)",
            "la tabla de prohibiciones la repite, y puede desviarse sola de la §7 de arriba"
        },
    };

    private static readonly Regex Pin = new(
        @"<PackageVersion\s+Include=""Umbraco\.Cms""\s+Version=""(?<v>[^""]+)""",
        RegexOptions.Compiled);

    /// <summary>
    /// La versión clavada, leída de la ÚNICA fuente de verdad (ADR 0004, gestión central).
    /// </summary>
    private static string VersionClavada()
    {
        var props = Path.Combine(Proyectos.Raiz(), "Directory.Packages.props");
        Assert.True(File.Exists(props), $"No existe {props} — la fuente de la versión.");

        var m = Pin.Match(File.ReadAllText(props));
        Assert.True(
            m.Success,
            "Directory.Packages.props no declara un <PackageVersion> para Umbraco.Cms. " +
            "Sin eso este gate no puede derivar nada, y pasar en verde sería el verde sobre " +
            "el vacío que el #136 midió.");

        return m.Groups["v"].Value;
    }

    /// <summary>
    /// ADR 0001 prohíbe 14+. Hasta el #149 eso era prosa y nada lo impedía.
    /// </summary>
    [Fact]
    public void El_pin_sigue_en_la_rama_13_LTS()
    {
        var v = VersionClavada();

        Assert.True(
            v.StartsWith("13.", StringComparison.Ordinal),
            $"Umbraco.Cms está clavado en {v}, fuera de la rama 13 LTS. ADR 0001 prohíbe subir " +
            "a 14+ sin un ADR que lo suceda: 14+ descontinuó Macros, cambió el editor de Block " +
            "Grid a Lit/TS y pide .NET 9+, y este árbol apunta entero a net8.0. Si el ADR nuevo " +
            "existe y esto es deliberado, este gate se mueve en el MISMO commit.");
    }

    /// <summary>
    /// Cada frase que dice «la versión clavada es N» dice la misma N que el fichero que la clava.
    /// </summary>
    [Theory]
    [MemberData(nameof(AfirmanLaVigente))]
    public void Quien_afirma_la_version_vigente_dice_la_que_esta_clavada(
        string relativa, string plantilla, string razon)
    {
        var v = VersionClavada();
        var ruta = Path.Combine(Proyectos.Raiz(), relativa);

        Assert.True(
            File.Exists(ruta),
            $"El censo declara «{relativa}» ({razon}) y ese fichero no existe. Una entrada " +
            "muerta deja el censo afirmando que alguien vigila algo que ya no está: o se " +
            "corrige la ruta, o sale del censo.");

        var esperada = string.Format(System.Globalization.CultureInfo.InvariantCulture, plantilla, v);

        Assert.True(
            File.ReadAllText(ruta).Contains(esperada, StringComparison.Ordinal),
            $"«{relativa}» no dice «{esperada}», y ahí es donde afirma cuál es la versión " +
            $"clavada ({razon}). Directory.Packages.props declara {v}. O el pin se movió sin " +
            "mover esta frase —y entonces la guía está diciendo una versión que no se " +
            "despliega—, o la frase se reescribió: en los dos casos hay que mirar el número " +
            "antes de seguir. Ojo: se compara la FRASE entera y no sólo la versión, porque " +
            "estos ficheros citan versiones dentro de la prosa que explica la regla y un " +
            "`Contains` de la versión sola pasa en verde con el defecto puesto (medido).");
    }
}
