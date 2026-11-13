using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Moderacion;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// La moderación de lo publicado (012-blogs-social, I5, P8 A): cualquiera con sesión denuncia, y quien modera
/// (<c>admin</c>, <c>moderator</c> o <c>editor</c>) decide en la cola mantener o retirar, diciendo por qué. Es genérica:
/// lo denunciable lo declara cada vertical con su <see cref="ITipoModerable"/>.
/// </summary>
/// <remarks>
/// <para>Quien denuncia sale de la sesión, nunca del cuerpo, y no aparece en la cola: se cuenta, no se nombra.</para>
///
/// <para><b>Retirar se ejecuta aquí</b>, tras decidir: es idempotente, así que si falla se vuelve a pedir la misma
/// decisión y se reintenta, sin saga. 403 directo y no <c>Forbid()</c>: con la autenticación de miembros, Forbid()
/// redirige al login.</para>
/// </remarks>
[ApiController]
[Route("api/moderacion")]
public sealed class ModeracionController : ControllerBase
{
    /// <summary>Los roles que moderan, los mismos de la cola de comentarios.</summary>
    public const string RolesQueModeran = "admin,moderator,editor";

    private readonly IModeracion _moderacion;
    private readonly IEnumerable<ITipoModerable> _tipos;
    private readonly IMemberAccessGate _sesion;
    private readonly ILogger<ModeracionController> _log;

    /// <summary>Con la moderación, lo denunciable de cada vertical y la sesión de quien pide.</summary>
    public ModeracionController(
        IModeracion moderacion,
        IEnumerable<ITipoModerable> tipos,
        IMemberAccessGate sesion,
        ILogger<ModeracionController> log)
    {
        _moderacion = moderacion ?? throw new ArgumentNullException(nameof(moderacion));
        _tipos = tipos ?? throw new ArgumentNullException(nameof(tipos));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Denuncia algo que se ve: una publicación, un comentario.</summary>
    [HttpPost("denuncias")]
    public async Task<IActionResult> Denunciar([FromBody] DenunciaPedida? pedida, CancellationToken cancellationToken)
    {
        if (!_sesion.IsAuthenticated || string.IsNullOrWhiteSpace(_sesion.CurrentMemberEmail))
        {
            return Unauthorized(new { error = "Inicia sesión para denunciar." });
        }

        if (pedida is null || string.IsNullOrWhiteSpace(pedida.Tipo) || string.IsNullOrWhiteSpace(pedida.Id))
        {
            return BadRequest(new { error = "Falta qué se denuncia." });
        }

        var motivo = pedida.Motivo?.Trim() ?? string.Empty;
        if (motivo.Length == 0 || motivo.Length > LargoMaximoDelMotivo)
        {
            return BadRequest(new { error = $"Cuéntanos por qué lo denuncias, en hasta {LargoMaximoDelMotivo} caracteres." });
        }

        if (TipoDe(pedida.Tipo) is not { } tipo)
        {
            return NotFound(new { error = "Esto no se puede denunciar." });
        }

        var yo = _sesion.CurrentMemberEmail.Trim();
        var id = pedida.Id.Trim();
        var contenido = await tipo.LeerAsync(id, yo, cancellationToken);
        if (contenido is null)
        {
            return NotFound(new { error = "No encontramos lo que quieres denunciar." });
        }

        var caso = await _moderacion.DenunciarAsync(
            new DenunciaNueva(new ObjetivoModerable(tipo.Tipo, id), contenido.Resumen, contenido.Enlace, yo, motivo),
            cancellationToken);
        return Ok(new DenunciaRecibida(caso.Id));
    }

    /// <summary>La cola: los casos abiertos, del más antiguo al más reciente.</summary>
    [HttpGet("cola")]
    public async Task<IActionResult> Cola([FromQuery] int? cuantos, CancellationToken cancellationToken)
    {
        if (QuienModera() is { } sinPermiso)
        {
            return sinPermiso;
        }

        var casos = await _moderacion.AbiertosAsync(cuantos is > 0 ? cuantos.Value : 50, cancellationToken);
        return Ok(new ColaDeModeracion(casos.Select(CasoEnLaCola.De).ToList()));
    }

    /// <summary>Decide un caso: mantener o retirar, con el motivo. Retirar lo saca para todos.</summary>
    [HttpPost("casos/{id}/decision")]
    public async Task<IActionResult> Decidir(
        string id,
        [FromBody] DecisionPedida? pedida,
        CancellationToken cancellationToken)
    {
        if (QuienModera() is { } sinPermiso)
        {
            return sinPermiso;
        }

        var decision = (pedida?.Decision?.Trim().ToLowerInvariant()) switch
        {
            "mantener" => DecisionDeModeracion.Mantener,
            "retirar" => DecisionDeModeracion.Retirar,
            _ => (DecisionDeModeracion?)null,
        };
        if (decision is null)
        {
            return BadRequest(new { error = "La decisión es «mantener» o «retirar»." });
        }

        var motivo = pedida!.Motivo?.Trim() ?? string.Empty;
        if (motivo.Length == 0 || motivo.Length > LargoMaximoDelMotivo)
        {
            return BadRequest(new { error = $"Di por qué lo decides, en hasta {LargoMaximoDelMotivo} caracteres." });
        }

        var quien = string.IsNullOrWhiteSpace(_sesion.CurrentMemberDisplayName)
            ? "Moderación"
            : _sesion.CurrentMemberDisplayName.Trim();
        var tomada = await _moderacion.DecidirAsync(id, decision.Value, quien, motivo, cancellationToken);
        switch (tomada.Resultado)
        {
            case ResultadoDeDecidir.NoExiste:
                return NotFound(new { error = "El caso no existe." });
            case ResultadoDeDecidir.YaDecididoDistinto:
                return Conflict(new { error = "Ese caso ya se decidió de otra forma.", caso = CasoEnLaCola.De(tomada.Caso!) });
        }

        var caso = tomada.Caso!;
        if (caso.Decision == DecisionDeModeracion.Retirar)
        {
            try
            {
                if (TipoDe(caso.Objetivo.Tipo) is not { } tipo)
                {
                    throw new InvalidOperationException($"Ningún vertical declara el tipo «{caso.Objetivo.Tipo}».");
                }

                await tipo.RetirarAsync(caso.Objetivo.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // La decisión quedó; lo que falta es retirarlo, y retirar es idempotente: se reintenta igual.
                _log.LogError(ex, "Moderación: el caso {Caso} quedó en retirar y no se pudo retirar {Tipo}/{Id}.",
                    caso.Id, caso.Objetivo.Tipo, caso.Objetivo.Id);
                return StatusCode(
                    StatusCodes.Status502BadGateway,
                    new { error = "La decisión quedó, pero no se pudo retirar. Vuelve a intentarlo.", caso = CasoEnLaCola.De(caso) });
            }
        }

        return Ok(CasoEnLaCola.De(caso));
    }

    /// <summary>Lo más largo de un motivo, el de la moderación.</summary>
    private const int LargoMaximoDelMotivo = 500;

    private ITipoModerable? TipoDe(string tipo)
        => _tipos.FirstOrDefault(t => string.Equals(t.Tipo, tipo.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>401 sin sesión, 403 sin un rol que modere; <c>null</c> si puede moderar.</summary>
    private IActionResult? QuienModera()
    {
        if (!_sesion.IsAuthenticated)
        {
            return Unauthorized(new { error = "Inicia sesión para moderar." });
        }

        return _sesion.HasAnyRole(RolesQueModeran)
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, new { error = "No tienes permiso para moderar." });
    }

    /// <summary>Lo que se pide al denunciar.</summary>
    public sealed record DenunciaPedida(string? Tipo, string? Id, string? Motivo);

    /// <summary>La denuncia quedó: el caso al que entró.</summary>
    public sealed record DenunciaRecibida(string Caso);

    /// <summary>Lo que se pide al decidir: <c>mantener</c> o <c>retirar</c>, y por qué.</summary>
    public sealed record DecisionPedida(string? Decision, string? Motivo);

    /// <summary>La cola de moderación.</summary>
    public sealed record ColaDeModeracion(IReadOnlyList<CasoEnLaCola> Casos);

    /// <summary>Un caso como lo ve quien modera: sin quién denunció, que se cuenta y no se nombra.</summary>
    public sealed record CasoEnLaCola(
        string Id,
        string Tipo,
        string ObjetivoId,
        string Resumen,
        string? Enlace,
        string Motivo,
        int Denuncias,
        DateTimeOffset Denunciado,
        string? Decision,
        string? Quien,
        string? PorQue,
        DateTimeOffset? Decidido)
    {
        internal static CasoEnLaCola De(CasoDeModeracion caso) => new(
            caso.Id,
            caso.Objetivo.Tipo,
            caso.Objetivo.Id,
            caso.Resumen,
            caso.Enlace,
            caso.Motivo,
            caso.Denuncias,
            caso.Denunciado,
            caso.Decision switch
            {
                DecisionDeModeracion.Mantener => "mantener",
                DecisionDeModeracion.Retirar => "retirar",
                _ => null,
            },
            caso.Quien,
            caso.PorQue,
            caso.Decidido);
    }
}
