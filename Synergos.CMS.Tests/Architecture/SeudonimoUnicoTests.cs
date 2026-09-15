using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El seudónimo de una persona se calcula en UN sitio.
/// </summary>
/// <remarks>
/// <para><b>Qué cierra</b> (#120). Estaba escrito seis veces con tres nombres —<c>Seudonimo</c> en
/// Auditoría, Realty y Pagos; <c>BuyerId</c> en Eventos y Tienda; <c>TravellerId</c> en Viajes—,
/// cinco consumidores por encima del umbral de <c>CLAUDE.md</c> §0.B.17.</para>
///
/// <para><b>Y lo que de verdad vigila no es la duplicación: son las VARIACIONES.</b> Dos de las
/// seis copias no hacían lo mismo que las otras cuatro —la bitácora devuelve el actor del sistema
/// sin correo, la tienda prefiere el <c>MemberKey</c>— y eso no se ve leyendo una copia. La
/// séptima que alguien escriba copia la que tenga más cerca y hereda o pierde una variación sin
/// saberlo.</para>
///
/// <para><b>El algoritmo NO es el criterio, y por eso el gate no cuenta <c>SHA256</c>.</b> En
/// <c>Web/Services/</c> ese mismo hash sirve para al menos cuatro cosas distintas: seudónimo de
/// una persona, huella del cuerpo de un acto (<c>HttpGovActNotificationService.Huella</c>), llave
/// de idempotencia (<c>HttpHotelBookingService.IdempotencyKeyFor</c>) y firma de un webhook
/// (<c>WebhookSigner</c>). Un gate que contara el hash sería ruido, y un helper compartido que se
/// las tragara todas sería peor que las seis copias: el día que una necesite cambiar, cambiarían
/// las otras.</para>
///
/// <para><b>Lo que este gate NO ve, dicho para no mentir sobre su alcance</b>
/// (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>): lee la FUENTE con regex, así
/// que una copia que llame a su parámetro <c>quien</c>, <c>usuario</c> o <c>dueño</c> en vez de
/// <c>correo</c>/<c>email</c> se le escapa al primer diente. Por eso hay un segundo, que va sobre
/// la FORMA del resultado y lleva trinquete.</para>
/// </remarks>
public sealed class SeudonimoUnicoTests
{
    private const string Helper = "SeudonimoDePersona.cs";

    /// <summary>
    /// Lo que queda legítimamente con la forma del seudónimo fuera del helper.
    /// </summary>
    /// <remarks>
    /// <b>Trinquete y no lista de excepciones</b>, la forma de <c>contract-keys.baseline.json</c>
    /// y de <c>PuntosAnterioresAlMolde</c>: hoy es <b>uno</b> —<c>Huella</c>, que resume el cuerpo
    /// de un acto administrativo y no a una persona—. Un segundo rompe el build y obliga a decir
    /// por qué, que es exactamente la conversación que no se tuvo seis veces.
    /// </remarks>
    private const int FormasAjenasAlSeudonimo = 1;

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

    /// <summary>El fichero sin comentarios: la prosa explica el código, no lo es.</summary>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            return t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)
                ? string.Empty
                : l;
        }));

    private static List<(string Nombre, string Codigo)> Servicios()
        => Directory
            .GetFiles(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Services"), "*.cs")
            .Where(f => !Path.GetFileName(f).Equals(Helper, StringComparison.Ordinal))
            .Select(f => (Path.GetFileName(f), SinComentarios(f)))
            .OrderBy(x => x.Item1, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void El_descubrimiento_ve_los_servicios_y_el_helper()
    {
        // Sin esto, los dos dientes de abajo recorrerían una lista vacía y el build quedaría
        // verde sobre nada — el trinquete al revés.
        var servicios = Servicios();
        Assert.True(servicios.Count > 20, $"Sólo se vieron {servicios.Count} ficheros en Web/Services.");

        var helper = Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Services", Helper);
        Assert.True(File.Exists(helper), "No existe SeudonimoDePersona.cs: revisar este gate.");
        Assert.Contains("SHA256.HashData", SinComentarios(helper), StringComparison.Ordinal);

        // Y que los seis consumidores siguen pasando por él.
        var porElHelper = servicios.Count(s => s.Codigo.Contains("SeudonimoDePersona.De(", StringComparison.Ordinal));
        Assert.True(porElHelper >= 6,
            $"Sólo {porElHelper} servicios llaman al helper; eran seis al escribir esto (#120).");
    }

    [Fact]
    public void Nadie_fuera_del_helper_convierte_un_correo_en_su_huella()
    {
        // EL DIENTE QUE IMPORTA. No mira el algoritmo —el mismo SHA256 sirve para cuatro cosas
        // distintas acá dentro— sino el SUJETO: hashear un correo es hacer un seudónimo de una
        // persona, y eso pasa por un solo sitio o vuelven las variaciones.
        var culpables = new List<string>();

        foreach (var (nombre, codigo) in Servicios())
        {
            foreach (Match m in Regex.Matches(codigo, @"SHA256\.HashData\s*\([^;]{0,200}"))
            {
                if (Regex.IsMatch(m.Value, @"\b(correo|email|Email|mail)\b"))
                {
                    culpables.Add($"{nombre} → {m.Value.Split('\n')[0].Trim()}");
                }
            }
        }

        Assert.True(culpables.Count == 0,
            "Estos sitios calculan el seudónimo de una persona por su cuenta en vez de llamar a "
            + "`SeudonimoDePersona.De(...)`. Eso es lo que dejó seis copias con dos variaciones "
            + "distintas —el actor del sistema de la bitácora y el MemberKey de la tienda— que "
            + "nadie podía ver leyendo una sola (#120):"
            + Environment.NewLine + string.Join(Environment.NewLine, culpables));
    }

    [Fact]
    public void La_forma_del_seudonimo_fuera_del_helper_lleva_trinquete()
    {
        // El segundo diente, por lo que el primero no ve: una copia cuyo parámetro se llame
        // `quien` o `usuario` no casa con el regex de arriba, pero sí produce la misma forma
        // —SHA256 truncado a 16 hex—. No se puede exigir cero, porque `Huella` la produce
        // legítimamente sobre el cuerpo de un acto. Así que se cuenta, y el número está fijo.
        // ⚠️ SE BUSCA EL TRUNCADO A SECAS, NO `ToHexString(...)[..16]`, Y ESO COSTÓ UNA MUTACIÓN.
        //
        // La primera versión pedía `Convert\.ToHexString\([^)]*\)\s*\[\.\.16\]` y pasaba en
        // VERDE con el defecto puesto: `[^)]*` no cruza paréntesis anidados, así que veía
        // `ToHexString(hash)[..16]` —el caso bonito, que es el que escribe `Huella`— y NO veía
        // `ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(x)))[..16]`, que es exactamente la
        // forma que tenían cuatro de las seis copias. El gate habría vigilado el único sitio
        // donde no hacía falta.
        //
        // Lo destapó mutar con el caso FEO del repo y no con el bonito, que es lo que dice
        // `feedback_a_gate_that_parses_source_needs_its_own_mutations`.
        var conLaForma = Servicios()
            .Where(s => s.Codigo.Contains("[..16]", StringComparison.Ordinal))
            .Select(s => s.Nombre)
            .ToList();

        Assert.True(conLaForma.Count == FormasAjenasAlSeudonimo,
            $"Con la forma del seudónimo fuera del helper hay {conLaForma.Count} y el trinquete "
            + $"está en {FormasAjenasAlSeudonimo}: {string.Join(", ", conLaForma)}. "
            + "Si es un seudónimo de persona, llamá a `SeudonimoDePersona.De(...)`. Si NO lo es "
            + "—como `Huella`, que resume el cuerpo de un acto— subí el trinquete y escribí acá "
            + "qué resume y por qué no es una persona. Mismo algoritmo no es la misma cosa (#120).");
    }
}
