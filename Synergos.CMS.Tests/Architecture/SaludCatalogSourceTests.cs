using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El CABLEADO del directorio de profesionales servido desde el contenido del CMS (#118), no sus
/// reglas: qué elige el flag, qué NO puede llevar el schema y qué campos cruzan de verdad.
/// </summary>
/// <remarks>
/// <b>Mira el composer, el XML y el borde, a propósito.</b> Es la lección de
/// <c>AcademyCatalogSourceTests</c> y antes de <c>IdentityGateTests</c>: las reglas ya las cubren
/// <c>ProfessionalContentRulesTests</c> y <c>CatalogDoctorDirectoryTests</c>, y lo que decide es
/// lo que hay ENTRE las piezas — qué se registra, qué declara el schema y qué llega al JSON.
/// </remarks>
public sealed class SaludCatalogSourceTests
{
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

    private static string Composer()
        => File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Composers", "SeamComposer.PlatformAndHealthcare.cs"));

    private static string Fuente()
        => File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Services", "Catalog", "UmbracoProfessionalDirectorySource.cs"));

    private static string DocType()
        => File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "uSync", "v9", "ContentTypes", "professionalpage.config"));

    [Fact]
    public void El_staff_sembrado_sigue_siendo_el_default()
    {
        // Un clon limpio tiene que arrancar con directorio sin que nadie haya autorado un solo
        // professionalPage. El modo `cms` es OPT-IN: se entra por IsCmsSource, que devuelve demo
        // cuando la clave no está.
        var composer = Composer();

        Assert.Contains("StubDoctorDirectory", composer, StringComparison.Ordinal);
        Assert.Contains(
            "IsCmsSource(sp, UmbracoProfessionalDirectorySource.Vertical)",
            composer,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Salud declara su scope, y sin él la fuente no sirve nada.
    /// </summary>
    /// <remarks>
    /// <c>professionalPage</c> vive bajo el siteRoot de Salud, y la fuente falla CERRADO si falta
    /// el scope — servir sin acotar mezclaría los profesionales de todos los siteRoots, y el de
    /// al lado puede ser un abogado. Que el flag se pueda mover a <c>cms</c> sin que el scope
    /// exista sería un directorio vacío con los logs en rojo y nadie mirándolos, así que el scope
    /// se declara desde el primer día aunque el flag siga en <c>demo</c>.
    /// </remarks>
    [Fact]
    public void El_scope_de_Salud_esta_declarado_en_los_dos_entornos()
    {
        foreach (var archivo in new[] { "appsettings.Development.json", "appsettings.Docker.json" })
        {
            var settings = File.ReadAllText(Path.Combine(RepoRoot(), "Synergos.CMS.Web", archivo));
            Assert.Contains("\"Salud\"", settings, StringComparison.Ordinal);
            Assert.Contains("\"salud\"", settings, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// La fuente de contenido NO siembra: sembrar es del catálogo.
    /// </summary>
    /// <remarks>
    /// Salud no tiene hoy nada que sembrar —un profesional no referencia cuerpos en otro
    /// almacén, que es lo que obligó a Educación (#100)— y esto es lo que impide que el día que
    /// lo tenga se resuelva por el camino corto. <c>ICatalogSource.GetAllAsync</c> se llama en
    /// CADA búsqueda —el catálogo no cachea, a propósito, para que el read-your-writes salga
    /// gratis—, así que una fuente que sembrara haría crecer el feed un ítem por búsqueda: sin
    /// tope y sin que nada se vea mal.
    /// </remarks>
    [Fact]
    public void La_fuente_de_contenido_no_toca_el_feed()
    {
        var fuente = Fuente();

        Assert.DoesNotContain("IContentStream", fuente, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAsync", fuente, StringComparison.Ordinal);
    }

    /// <summary>
    /// El schema NO lleva el identificador del recurso de <c>Api.Booking</c>.
    /// </summary>
    /// <remarks>
    /// <b>Es el punto fino de este vertical y ya costó una vuelta entera.</b> Lo que viaja al
    /// orquestador es el slug como <c>professionalId</c>; el identificador interno del recurso lo
    /// GENERA la capacidad al registrarlo, así que <b>ninguna convención de este lado puede
    /// acertarlo</b> — se resuelve preguntando por el sujeto
    /// (<c>GET /v1/resources?subjectKind=&amp;subjectId=</c>, HU #25). <c>SaludSettings</c> tuvo
    /// un <c>ResourceIdPrefix</c> por exactamente esta razón y se fue.
    ///
    /// <para>Un campo en el DocType para escribirlo a mano sería lo mismo con otra cara, y peor:
    /// lo teclearía un editor. Por eso el gate va sobre el XML y no sobre la prosa — la prosa ya
    /// lo decía en <c>SaludSettings</c> y el campo habría entrado igual. Y el mensaje de fallo
    /// recita la salida, que es lo único que evita que alguien lo vuelva a intentar.</para>
    /// </remarks>
    [Fact]
    public void El_schema_del_profesional_NO_lleva_el_identificador_del_recurso()
    {
        var alias = Regex.Matches(DocType(), @"<Alias>([A-Za-z0-9]+)</Alias>")
            .Select(m => m.Groups[1].Value)
            .ToList();

        Assert.NotEmpty(alias);

        var sospechosos = alias
            .Where(a => a.Contains("resource", StringComparison.OrdinalIgnoreCase)
                || a.Contains("booking", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(sospechosos.Count == 0,
            "professionalPage declara " + string.Join(", ", sospechosos)
            + ". El identificador del recurso lo genera Api.Booking al registrarlo: ninguna "
            + "convención del CMS puede acertarlo, y escribirlo a mano en el backoffice es la "
            + "misma trampa que el ResourceIdPrefix que ya se quitó de SaludSettings. Lo que "
            + "viaja es el SUJETO (professionalSlug), y el recurso se resuelve con "
            + "GET /v1/resources?subjectKind=&subjectId= (HU #25).");
    }

    /// <summary>
    /// Todo campo que el editor llena lo LEE alguien, y todo campo que se lee lo declara el schema.
    /// </summary>
    /// <remarks>
    /// <b>Las dos direcciones, y cada una tapa un defecto distinto.</b> Un campo del DocType que
    /// nadie lee es mobiliario: el editor escribe el teléfono del consultorio y no sale en ningún
    /// lado — y no falla, sólo no aparece
    /// (<c>feedback_no_read_without_a_write_path</c> por el otro lado). Un alias que la fuente lee
    /// y el schema no declara sale SIEMPRE vacío, porque <c>IPublishedContent.Value</c> devuelve
    /// el default de la propiedad que no existe: un profesional entero sin especialidad ni
    /// horario, en silencio.
    ///
    /// <para>Se cruza <b>por nombre de alias</b> y sólo los <c>professional*</c>: los heredados de
    /// las compositions no están en <c>GenericProperties</c>, y la fuente también lee
    /// <c>brandKey</c>, que es del siteRoot y no de este DocType.</para>
    /// </remarks>
    [Fact]
    public void El_schema_y_la_fuente_declaran_los_MISMOS_campos()
    {
        var declarados = Regex.Matches(DocType(), @"<Alias>(professional[A-Za-z0-9]*)</Alias>")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var leidos = Regex.Matches(Fuente(), @"Value<[^""]*?>\(""(professional[A-Za-z0-9]*)""\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        // Red de seguridad: si alguno de los dos descubrimientos deja de ver, las dos listas
        // salen vacías y el cruce pasa en verde sin mirar nada.
        Assert.True(declarados.Count >= 10,
            $"El XML de professionalPage sólo declara {declarados.Count} campos professional*: "
            + "revisar este gate antes que el schema.");

        var mobiliario = declarados.Except(leidos).OrderBy(a => a, StringComparer.Ordinal).ToList();
        var fantasmas = leidos.Except(declarados).OrderBy(a => a, StringComparer.Ordinal).ToList();

        Assert.True(mobiliario.Count == 0,
            "professionalPage declara campos que la fuente no lee: " + string.Join(", ", mobiliario)
            + ". El editor los llenaría y no saldrían en ninguna parte, sin que nada fallara.");

        Assert.True(fantasmas.Count == 0,
            "La fuente lee campos que professionalPage no declara: " + string.Join(", ", fantasmas)
            + ". Value<T> devuelve el default de una propiedad que no existe, así que saldrían "
            + "vacíos SIEMPRE y en silencio.");
    }

    /// <summary>
    /// El contacto del profesional sale del seam, no se vuelve a escribir en el borde.
    /// </summary>
    /// <remarks>
    /// Hasta el #118 <c>EhrController</c> fabricaba <c>acceptingPatients: null</c> y dos cadenas
    /// vacías porque <see cref="Synergos.CMS.Interfaces.MedicalDoctor"/> no traía contacto. El
    /// <c>&lt;remarks&gt;</c> de la HU #111 dejaba escrito el disparador, y la regla del repo es
    /// que un comentario que nombra un defecto vivo o lo arregla o abre el ticket — dejarlo
    /// escrito lo BLINDA, porque la siguiente auditoría lo lee como algo ya identificado y pasa
    /// de largo (<c>feedback_a_fabrication_can_be_a_derivation</c>).
    ///
    /// <para>Va sobre el MAPEO y no sobre el DTO: la clave siempre cruzó —el defecto era el
    /// valor—, así que un gate que mirara la forma del JSON pasaría en verde con la constante
    /// puesta.</para>
    /// </remarks>
    [Fact]
    public void El_contacto_del_medico_sale_del_seam_y_no_del_borde()
    {
        var controller = File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Controllers", "EhrController.cs"));

        var mapeo = Regex.Match(
            controller,
            @"private static DoctorDto ToDoctorDto\(MedicalDoctor d\)[\s\S]{0,700}?;");

        Assert.True(mapeo.Success, "No se encontró ToDoctorDto: revisar este gate antes que el borde.");

        var fabricado = new[] { "AcceptingPatients: null", "Phone: string.Empty", "Email: string.Empty" }
            .Where(c => mapeo.Value.Contains(c, StringComparison.Ordinal))
            .ToList();

        Assert.True(fabricado.Count == 0,
            "ToDoctorDto vuelve a escribir a mano: " + string.Join(", ", fabricado)
            + ". Esos tres campos los sabe MedicalDoctor desde el #118 cuando el directorio sale "
            + "del contenido; escribirlos aquí los deja constantes para todo profesional "
            + "autorado, y `null` no es «no admite», es «no consta».");
    }
}
