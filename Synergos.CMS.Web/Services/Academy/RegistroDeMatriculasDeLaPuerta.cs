using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Academy;

/// <summary>Lo que contesta el registro de la matrícula: la anotación, o el motivo con su código.</summary>
public sealed record ResultadoDeLaMatricula(
    int Estado, string? Codigo = null, string? Mensaje = null, AnotacionDeLaMatricula? Anotacion = null, bool Transitorio = false)
{
    public bool Bien => Estado is >= 200 and < 300;
}

/// <summary>
/// El registro de las matrículas hechas por la puerta (010-educacion-matricula, molde de la Tienda, 004-P2): lee la
/// matrícula al orquestador de Educación con el sujeto del miembro de la sesión y la anota activa en el libro que lee el
/// aula, «mi aprendizaje», el progreso, el certificado y la consola del instructor.
/// </summary>
/// <remarks>
/// <para><b>Siempre de quien se matriculó.</b> Cada lectura lleva el sujeto del miembro —el mismo que pone la puerta—,
/// así que una matrícula ajena es un 404 del orquestador y nunca se anota.</para>
///
/// <para><b>Lo que se anota sale del lado que cobró</b>: el curso y el total son la <c>salida</c> del flujo, nunca lo que
/// diga el navegador. Sólo una matrícula <c>Completed</c> abre el aula.</para>
///
/// <para><b>Reconciliar corre en la ruta de lectura, con techo</b> (la regla de la F3): como mucho
/// <see cref="TopeDeReconciliacion"/> matrículas y <see cref="PresupuestoDeReconciliacion"/> en total; lo que falte se
/// mira en la próxima lectura. Una matrícula de un curso que el alumno ya tiene activo no se reintenta en cada lectura:
/// la dijo ya quien la anotó al cerrarla.</para>
/// </remarks>
public sealed class RegistroDeMatriculasDeLaPuerta
{
    /// <summary>Cliente nombrado hacia el orquestador de Educación, por la pieza del árbol.</summary>
    public const string ClientName = "synergos-bff-educacion-registro";

    /// <summary>El flujo de la puerta cuyas matrículas anota: de su configuración sale el <c>Kind</c> del sujeto.</summary>
    public const string Flujo = "educacion.matricula";

    /// <summary>Cuántas matrículas anota, como mucho, una reconciliación.</summary>
    public const int TopeDeReconciliacion = 10;

    /// <summary>Cuánto puede tardar, como mucho, una reconciliación entera.</summary>
    public static readonly TimeSpan PresupuestoDeReconciliacion = TimeSpan.FromSeconds(3);

    private const string Cabecera = "X-Synergos-Sujeto";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _clientes;
    private readonly IMatriculasDeLaPuerta _libro;
    private readonly IEnrollmentService _matriculas;
    private readonly IOptionsMonitor<PuertaSettings> _puerta;
    private readonly ILogger<RegistroDeMatriculasDeLaPuerta> _log;
    private readonly bool _hayDestino;
    private readonly TimeSpan _presupuesto;

    /// <param name="hayDestino">Si el despliegue dice dónde vive el orquestador de Educación.</param>
    public RegistroDeMatriculasDeLaPuerta(
        IHttpClientFactory clientes, IMatriculasDeLaPuerta libro, IEnrollmentService matriculas, IOptionsMonitor<PuertaSettings> puerta,
        ILogger<RegistroDeMatriculasDeLaPuerta> log, bool hayDestino, TimeSpan? presupuesto = null)
    {
        _clientes = clientes;
        _libro = libro;
        _matriculas = matriculas;
        _puerta = puerta;
        _log = log;
        _hayDestino = hayDestino;
        _presupuesto = presupuesto ?? PresupuestoDeReconciliacion;
    }

    private string? Kind => _puerta.CurrentValue.Flujos.TryGetValue(Flujo, out var f) ? f.SujetoKind : null;

    /// <summary>Tras cerrar: si la matrícula se completó, la anota activa (una vez); si no, dice por qué.</summary>
    public async Task<ResultadoDeLaMatricula> AnotarAsync(string matriculaId, Guid miembro, string? nombre, string? correo, CancellationToken ct)
    {
        if (NoDisponible() is { } cerrado) return cerrado;
        if (string.IsNullOrWhiteSpace(correo))
        {
            return new ResultadoDeLaMatricula(StatusCodes.Status409Conflict, "academy.enrollment_sin_correo",
                "La cuenta no tiene correo, y el aula reconoce al alumno por él.");
        }

        var (matricula, fallo) = await LeerAsync(matriculaId, miembro, ct).ConfigureAwait(false);
        if (fallo is not null) return fallo;

        return matricula!.Estado switch
        {
            "Completed" => await AnotarLeidaAsync(matricula, nombre, correo, ct).ConfigureAwait(false),
            "Running" => new ResultadoDeLaMatricula(StatusCodes.Status409Conflict, "academy.enrollment_not_closed",
                "La matrícula todavía no se cerró: no hay aula que abrir."),
            "Compensated" => new ResultadoDeLaMatricula(StatusCodes.Status409Conflict, "academy.enrollment_undone",
                matricula.Error ?? "La matrícula se deshizo: no se cobró."),
            // Deshaciéndose o rendida: no se afirma nada.
            _ => new ResultadoDeLaMatricula(StatusCodes.Status409Conflict, "academy.enrollment_undoing",
                "La matrícula se está deshaciendo.", Transitorio: true),
        };
    }

    /// <summary>
    /// Anota las matrículas de la puerta del miembro que el orquestador completó y el libro no tiene (la pestaña se
    /// cerró entre cerrar y anotar). Corre al leer «mi aprendizaje», con techo; nunca falla hacia quien lee.
    /// </summary>
    public async Task ReconciliarAsync(Guid miembro, string? nombre, string? correo, CancellationToken ct)
    {
        if (NoDisponible() is not null || miembro == Guid.Empty || string.IsNullOrWhiteSpace(correo)) return;

        using var presupuesto = CancellationTokenSource.CreateLinkedTokenSource(ct);
        presupuesto.CancelAfter(_presupuesto);
        try
        {
            var completadas = await CompletadasAsync(miembro, presupuesto.Token).ConfigureAwait(false);
            if (completadas.Count == 0) return;

            var anotadas = await _libro.AnotadasAsync(correo, presupuesto.Token).ConfigureAwait(false);
            var activos = (await _matriculas.GetEnrollmentsAsync(correo, presupuesto.Token).ConfigureAwait(false))
                .Select(e => e.CourseId).ToHashSet(StringComparer.Ordinal);
            foreach (var m in completadas
                .Where(m => !anotadas.Contains(m.Id) && m.Datos?.Curso?.Id is { } curso && !activos.Contains(curso))
                .Take(TopeDeReconciliacion))
            {
                await AnotarLeidaAsync(m, nombre, correo, presupuesto.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("La reconciliación de matrículas de la puerta llegó a su techo de {Techo}: lo que falta se mira en la próxima.", _presupuesto);
        }
    }

    /// <summary>
    /// Cuántas matrículas de la puerta del alumno devolvió el personal, por curso (010-P2 B): «mi aprendizaje» se lo
    /// entrega al elemento para la llave de una matrícula nueva. Es del libro: no sale a la red.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, int>> DevueltasAsync(string? correo, CancellationToken ct)
        => string.IsNullOrWhiteSpace(correo)
            ? new Dictionary<string, int>(StringComparer.Ordinal)
            : await _libro.DevueltasAsync(correo, ct).ConfigureAwait(false);

    private async Task<ResultadoDeLaMatricula> AnotarLeidaAsync(MatriculaLeida m, string? nombre, string correo, CancellationToken ct)
    {
        if (m.Datos?.Curso?.Id is not { Length: > 0 } curso || m.Datos.Total is not { } total) return Invalida();

        var anotacion = await _libro.AnotarAsync(
            new MatriculaDeLaPuerta(m.Id, m.Numero, curso, new Student(nombre ?? string.Empty, correo), total.Amount, total.Currency), ct)
            .ConfigureAwait(false);
        if (anotacion.Devuelta)
        {
            // Completada en el orquestador y devuelta por el personal (010-P2 B): no abre el aula, y no se dice «activa».
            return new ResultadoDeLaMatricula(StatusCodes.Status409Conflict, "academy.enrollment_devuelta",
                "Esa matrícula se devolvió: el curso ya no está activo.", anotacion);
        }
        if (anotacion.CobradaDeMas)
        {
            _log.LogWarning(
                "La matrícula {Numero} de la puerta se cobró con el curso {Curso} ya activo para el alumno: no se anotó otra fila; devolverla es del personal.",
                m.Numero, curso);
        }
        return new ResultadoDeLaMatricula(StatusCodes.Status200OK, Anotacion: anotacion);
    }

    private ResultadoDeLaMatricula? NoDisponible()
        => _hayDestino && !string.IsNullOrWhiteSpace(Kind)
            ? null
            : new ResultadoDeLaMatricula(StatusCodes.Status503ServiceUnavailable, "academy.registro_no_disponible",
                "La matrícula por la puerta no está abierta en este sitio.");

    private static ResultadoDeLaMatricula Ajena()
        => new(StatusCodes.Status404NotFound, "educacion.saga_not_found", "La matrícula no existe.");

    private static ResultadoDeLaMatricula Invalida()
        => new(StatusCodes.Status502BadGateway, "academy.respuesta_invalida", "El orquestador no pudo contestar por la matrícula.");

    /// <summary>La matrícula tal como la ve su alumno, o el motivo por el que no se pudo leer.</summary>
    private async Task<(MatriculaLeida? Matricula, ResultadoDeLaMatricula? Fallo)> LeerAsync(string matriculaId, Guid miembro, CancellationToken ct)
    {
        // Un segmento de punto no es una matrícula: escaparlo no lo cambia y movería la lectura a otra ruta.
        if (string.IsNullOrWhiteSpace(matriculaId) || Puerta.ReenvioDeLaPuerta.EsSegmentoDePunto(matriculaId)) return (null, Ajena());

        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/flujos/{Flujo}/{Uri.EscapeDataString(matriculaId)}");
        req.Headers.TryAddWithoutValidation(Cabecera, $"{Kind}:{miembro:n}");
        try
        {
            using var res = await _clientes.CreateClient(ClientName).SendAsync(req, ct).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                var matricula = await res.Content.ReadFromJsonAsync<MatriculaLeida>(Json, ct).ConfigureAwait(false);
                return matricula is null ? (null, Invalida()) : (matricula, null);
            }
            if (res.StatusCode == HttpStatusCode.NotFound) return (null, Ajena());

            var rechazo = await RechazoDelArbolDeServicios.LeerAsync(res, Json, ct).ConfigureAwait(false);
            _log.LogWarning("Educación contestó {Estado} ({Codigo}) al leer una matrícula para su registro.", (int)res.StatusCode, rechazo?.Codigo ?? "-");
            return (null, Invalida());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            _log.LogWarning("Educación no respondió al leer una matrícula para su registro: {Motivo}", ex.Message);
            return (null, new ResultadoDeLaMatricula(StatusCodes.Status503ServiceUnavailable, "academy.orquestador_no_disponible",
                "El orquestador de Educación no está disponible.", Transitorio: true));
        }
    }

    /// <summary>Las matrículas completadas del miembro («listar» de la puerta); vacío si no se pudo leer.</summary>
    private async Task<IReadOnlyList<MatriculaLeida>> CompletadasAsync(Guid miembro, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"v1/flujos/{Flujo}?estado=Completed&limit=50");
        req.Headers.TryAddWithoutValidation(Cabecera, $"{Kind}:{miembro:n}");
        try
        {
            using var res = await _clientes.CreateClient(ClientName).SendAsync(req, ct).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                _log.LogWarning("Educación contestó {Estado} al listar las matrículas de un alumno para reconciliar.", (int)res.StatusCode);
                return [];
            }
            var pagina = await res.Content.ReadFromJsonAsync<PaginaLeida>(Json, ct).ConfigureAwait(false);
            return pagina?.Items?.Where(m => m.Estado == "Completed").ToList() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _log.LogWarning("Educación no respondió al listar las matrículas de un alumno para reconciliar: {Motivo}", ex.Message);
            return [];
        }
    }

    /// <summary>Lo que el registro lee de <c>EducacionMatriculaRespuesta</c> (el contrato de <c>Synergos.Bff.Educacion.json</c>).</summary>
    internal sealed record MatriculaLeida(string Id, string Numero, string Estado, DatosDeLaMatricula? Datos, string? Error);

    internal sealed record DatosDeLaMatricula(CursoLeido? Curso, string? Plan, DineroLeido? Total);

    internal sealed record CursoLeido(string Kind, string Id);

    internal sealed record DineroLeido(decimal Amount, string Currency);

    internal sealed record PaginaLeida(IReadOnlyList<MatriculaLeida>? Items);
}
