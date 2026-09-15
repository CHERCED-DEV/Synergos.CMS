using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Dev-only endpoints para content seeding y smoke-test. Todos gated
/// por <c>Synergos:DevSeed:Enabled=true</c>: retornan 404 con el flag off.
/// </summary>
/// <remarks>
/// <b>Esto decía «no-op en prod» y no era verdad</b> (#113). La frase es cierta
/// condicionalmente —con el flag off no pasa nada— y describía un perfil de
/// producción que <b>no existía</b>: <c>appsettings.Docker.json</c> trae el flag
/// encendido y <c>ASPNETCORE_ENVIRONMENT: Docker</c> es justo con lo que corre el
/// despliegue. Como estos catorce endpoints son <c>[AllowAnonymous]</c>, eso dejaba
/// <c>POST /dev/clear-all-content</c> alcanzable desde internet, sin autenticar.
/// <para>Hoy lo apaga el despliegue —<c>compose.prod.yml</c>, generado— y hay gate
/// (<c>ComposeStackTests.El_perfil_de_produccion_NO_trae_la_siembra_encendida</c>).
/// El perfil sigue trayéndolo encendido a propósito, para que un
/// <c>docker compose</c> de desarrollo siga sirviendo la siembra: quien sabe que
/// esto es producción es el despliegue, no el perfil.</para>
/// <para><b>Y la salvaguarda NO es la autenticación</b>: es el flag. Un arreglo que
/// dejara el flag encendido y tapara los endpoints con auth deja la siembra viva en
/// producción, que es la otra mitad del problema.</para>
/// </remarks>
[ApiController]
[Route("dev")]
[AllowAnonymous]
public sealed class DevController : ControllerBase
{
    private readonly DevSeedSettings _settings;
    private readonly DevTestContentSeeder _seeder;
    private readonly SynergosIdentitySeeder _identitySeeder;
    private readonly DevContentFiller _filler;
    private readonly DevMemberRoleSeeder _roleSeeder;
    private readonly DevProductReviewSeeder _reviewSeeder;
    private readonly DevPaidOrderSeeder _paidOrderSeeder;
    private readonly StarterPortadaSeeder _portadaSeeder;
    private readonly ILogger<DevController> _logger;

    public DevController(
        IOptions<DevSeedSettings> settings,
        DevTestContentSeeder seeder,
        SynergosIdentitySeeder identitySeeder,
        DevContentFiller filler,
        DevMemberRoleSeeder roleSeeder,
        DevProductReviewSeeder reviewSeeder,
        DevPaidOrderSeeder paidOrderSeeder,
        StarterPortadaSeeder portadaSeeder,
        ILogger<DevController> logger)
    {
        _settings = settings.Value;
        _seeder = seeder;
        _identitySeeder = identitySeeder;
        _filler = filler;
        _roleSeeder = roleSeeder;
        _reviewSeeder = reviewSeeder;
        _paidOrderSeeder = paidOrderSeeder;
        _portadaSeeder = portadaSeeder;
        _logger = logger;
    }

    /// <summary>
    /// Siembra reseñas de demo sobre el catálogo VIVO para que la prueba social de T10
    /// (ADR 0114) tenga qué enseñar. Idempotente: re-ejecutar sustituye, no acumula.
    /// </summary>
    [HttpPost("seed-product-reviews")]
    public async Task<IActionResult> SeedProductReviews(
        [FromQuery] int maxProducts,
        CancellationToken cancellationToken)
    {
        if (!_settings.Enabled) return NotFound();

        _logger.LogInformation("DevSeed endpoint invocado (seed-product-reviews).");
        var (products, reviews) = await _reviewSeeder
            .SeedAsync(maxProducts <= 0 ? 50 : maxProducts, cancellationToken)
            .ConfigureAwait(false);

        // Sin productos no hay siembra posible: se dice, en vez de devolver un OK vacío que
        // parezca éxito.
        return products == 0
            ? Conflict(new { error = "El catálogo no devolvió productos; no se sembró nada." })
            : Ok(new { products, reviews });
    }

    /// <summary>
    /// Crea la <b>portada de arranque</b>: el <c>siteRoot</c> publicado que hace que
    /// <c>GET /</c> deje de servir el cartel «No published content».
    /// <c>POST /dev/seed-portada</c>
    /// </summary>
    /// <remarks>
    /// <para>Es el último hueco del camino de un clon limpio a un sitio visible (#119). El
    /// schema lo trae <c>tools/importar-schema.sh</c> y el estado de las capacidades
    /// <c>tools/provisionar.sh</c>; el contenido no lo traía nadie, porque
    /// <c>uSync/v9/Content/</c> está vacía y sembrar en boot está prohibido (ADR 0013).</para>
    ///
    /// <para><b>Se siembra en DESARROLLO, no en el servidor.</b> El flag de DevSeed viene
    /// apagado en producción (#113, hay gate), así que acá esto contesta 404 y eso es lo
    /// correcto: el camino es sembrar en local → revisar → exportar con uSync → commitear
    /// <c>uSync/v9/Content/</c> → importar en el servidor. Ver
    /// <c>docs/despliegue/00-montar-el-entorno.md</c> §5.bis.2.</para>
    ///
    /// <para><b>Idempotente y no destructivo</b>: si el <c>siteRoot</c> ya tiene cuerpo,
    /// contesta <c>already-authored</c> y no toca nada — sembrar encima se llevaría por
    /// delante lo que el arquitecto acaba de autorar, justo antes de exportarlo.</para>
    /// </remarks>
    [HttpPost("seed-portada")]
    public IActionResult SeedPortada()
    {
        if (!_settings.Enabled) return NotFound();

        _logger.LogInformation("DevSeed endpoint invocado (seed-portada).");
        var result = _portadaSeeder.Seed();

        // Un fallo NO sale con 200. El seeder de identidad contestaba 200 con
        // success:false y por eso nadie vio que llevaba roto: «no se pudo» y «ya estaba»
        // se leen igual desde un script.
        return result.Success
            ? Ok(new { result.SiteRootId, outcome = result.Outcome.ToString(), result.Detail })
            : Conflict(new { outcome = result.Outcome.ToString(), error = result.Detail });
    }

    [HttpPost("seed-test-site")]
    public IActionResult SeedTestSite()
    {
        if (!_settings.Enabled) return NotFound();
        _logger.LogInformation("DevSeed endpoint invocado.");
        var result = _seeder.Seed();
        return Ok(result);
    }

    [HttpDelete("clear-test-site")]
    public IActionResult ClearTestSite()
    {
        if (!_settings.Enabled) return NotFound();
        _logger.LogInformation("DevClear endpoint invocado.");
        var result = _seeder.Clear();
        return Ok(result);
    }

    [HttpPost("clear-all-content")]
    public IActionResult ClearAllContent()
    {
        if (!_settings.Enabled) return NotFound();
        _logger.LogWarning("DevClearAll endpoint invocado — borra TODO el content tree.");
        var result = _identitySeeder.ClearAll();
        return Ok(result);
    }

    /// <summary>
    /// Crea el ANDAMIO de la vitrina SynergosLabs (platformRoot → siteRoot → 3 páginas
    /// vacías) que <c>POST /dev/fill-synergos-pages</c> puebla después. Idempotente.
    /// </summary>
    /// <remarks>
    /// <b>No es la portada de arranque</b> — ésa es <c>POST /dev/seed-portada</c>, la que
    /// nombra <c>docs/despliegue/00-montar-el-entorno.md</c> §5.bis.2.
    /// <para>Devolvía <c>200</c> con <c>success:false</c> adentro, y encima llevaba roto:
    /// el publish del <c>platformRoot</c> se caía por dos obligatorias sin poner (#119). Un
    /// fallo dentro de un 200 no se lee como fallo desde un script ni desde un navegador,
    /// así que ahora sale con <c>409</c>.</para>
    /// </remarks>
    [HttpPost("seed-synergos-identity")]
    public IActionResult SeedSynergosIdentity()
    {
        if (!_settings.Enabled) return NotFound();
        _logger.LogInformation("SynergosSeed endpoint invocado.");
        var result = _identitySeeder.Seed();
        return result.Success ? Ok(result) : Conflict(result);
    }

    [HttpPost("fill-synergos-pages")]
    public IActionResult FillSynergosPages()
    {
        if (!_settings.Enabled) return NotFound();
        _logger.LogInformation("DevContentFiller endpoint invocado (fill-synergos-pages).");
        var result = _filler.FillSynergosPages();
        return Ok(result);
    }

    /// <summary>
    /// Coloca UNA tarjeta de producto (<c>elementShopProductCard</c>) en el BlockGrid
    /// <c>sections</c> de una página. <c>POST /dev/place-product-card?pageId=&amp;sku=</c>
    /// </summary>
    /// <remarks>
    /// Existe porque el renderer del elemento (<c>Elements/Shop/ProductCard.cshtml</c>) no
    /// se podía ejercitar: ningún contenido colocaba el elemento en un BlockGrid, así que
    /// esa rama no la pintaba ninguna página y no había forma de verla en vivo.
    ///
    /// Es DESTRUCTIVO por naturaleza (escribe la propiedad <c>sections</c>), así que por
    /// defecto se NIEGA a pisar una página que ya tenga secciones: hay que pasar
    /// <c>force=true</c> a propósito. Sin ese freno, un pageId mal tecleado borraría el
    /// contenido de una página real.
    /// </remarks>
    [HttpPost("place-product-card")]
    public IActionResult PlaceProductCard(
        [FromQuery] int pageId,
        [FromQuery] string? sku,
        [FromQuery] bool force = false,
        [FromQuery] int parentId = 0,
        [FromQuery] string? name = null)
    {
        if (!_settings.Enabled) return NotFound();
        if (string.IsNullOrWhiteSpace(sku) || (pageId <= 0 && parentId <= 0))
        {
            return BadRequest(new { error = "sku es requerido, y pageId (existente) o parentId (crear)." });
        }

        _logger.LogInformation("Dev endpoint place-product-card invocado (pageId={PageId}, parentId={ParentId}, sku={Sku}).", pageId, parentId, sku);
        var result = _filler.PlaceProductCard(pageId, sku!, force, parentId, name);
        return result.Success ? Ok(result) : Conflict(result);
    }

    /// <summary>
    /// Siembra un formulario de prueba (1 campo obligatorio + 1 opcional) en una página nueva.
    /// <c>POST /dev/seed-test-form?parentId=&amp;formKey=</c>
    /// </summary>
    /// <remarks>
    /// Existe para poder EJERCITAR la validación de obligatorios del servidor: sin un
    /// `formInternalKey` publicado, esa rama no la recorre nadie y no se sabe si funciona.
    /// </remarks>
    [HttpPost("seed-test-form")]
    public IActionResult SeedTestForm(
        [FromQuery] int parentId,
        [FromQuery] string? formKey,
        [FromQuery] string? name = null)
    {
        if (!_settings.Enabled) return NotFound();
        if (parentId <= 0 || string.IsNullOrWhiteSpace(formKey))
        {
            return BadRequest(new { error = "parentId y formKey son requeridos." });
        }

        _logger.LogInformation("Dev endpoint seed-test-form invocado (parentId={ParentId}, formKey={FormKey}).", parentId, formKey);
        var result = _filler.SeedTestForm(parentId, formKey!, name);
        return result.Success ? Ok(result) : Conflict(result);
    }

    /// <summary>
    /// Borra una página de dev. <c>POST /dev/delete-page?pageId=&amp;expectedName=</c>
    /// </summary>
    /// <remarks>
    /// Contrapartida de <c>place-product-card</c>: lo que se crea para probar hay que
    /// poder quitarlo. Exige el nombre esperado a propósito — borrar no tiene deshacer
    /// barato y un id mal copiado apunta a contenido real.
    /// </remarks>
    [HttpPost("delete-page")]
    public IActionResult DeletePage([FromQuery] int pageId, [FromQuery] string? expectedName)
    {
        if (!_settings.Enabled) return NotFound();
        if (pageId <= 0 || string.IsNullOrWhiteSpace(expectedName))
        {
            return BadRequest(new { error = "pageId y expectedName son requeridos." });
        }

        _logger.LogInformation("Dev endpoint delete-page invocado (pageId={PageId}).", pageId);
        var result = _filler.DeletePage(pageId, expectedName!);
        return result.Success ? Ok(result) : Conflict(result);
    }

    /// <summary>
    /// Siembra una orden PAGADA para un miembro existente, por el flujo real de checkout.
    /// Sirve para ejercitar el gate de comprador verificado de T10 con una sesión de verdad.
    /// No crea miembros ni toca credenciales.
    /// </summary>
    [HttpPost("seed-paid-order")]
    public async Task<IActionResult> SeedPaidOrder(
        [FromQuery] string? email,
        [FromQuery] string? sku,
        CancellationToken cancellationToken)
    {
        if (!_settings.Enabled) return NotFound();

        _logger.LogInformation("DevSeed endpoint invocado (seed-paid-order).");
        var result = await _paidOrderSeeder
            .SeedAsync(email ?? string.Empty, sku ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        return result.Ok
            ? Ok(new { result.OrderRef, result.Sku, memberKey = result.MemberKey })
            : Conflict(new { error = result.Error });
    }

    /// <summary>
    /// Crea los member groups de dominio y (opcional) se los asigna a un member.
    /// <c>POST /dev/seed-member-roles?email=&amp;roles=funcionario,organizador</c>
    /// </summary>
    /// <remarks>
    /// Existe porque cuatro olas de seguridad dejaron consolas cerradas por rol
    /// (<c>funcionario</c>, <c>organizador</c>, <c>doctor</c>) que <b>no se podían
    /// demostrar</b> sin crear los grupos a mano en el backoffice. NO crea members ni
    /// toca contraseñas: solo reparte permisos de demo sobre identidades que ya existen.
    /// </remarks>
    [HttpPost("seed-member-roles")]
    public IActionResult SeedMemberRoles([FromQuery] string? email, [FromQuery] string? roles)
    {
        if (!_settings.Enabled) return NotFound();

        var requested = (roles ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _logger.LogInformation("DevSeed endpoint invocado (seed-member-roles).");
        var result = _roleSeeder.Seed(email, requested);
        return Ok(result);
    }

    [HttpGet("ping")]
    public IActionResult Ping()
    {
        return Ok(new { ok = true, devSeedEnabled = _settings.Enabled, timestamp = DateTime.UtcNow });
    }
}
