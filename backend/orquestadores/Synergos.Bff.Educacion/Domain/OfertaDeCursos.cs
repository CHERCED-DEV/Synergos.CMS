using System.Security.Cryptography;
using System.Text;
using Synergos.Bff.Core;
using Synergos.Bff.Educacion.Clients;
using Synergos.Bff.Educacion.Contracts;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Educacion.Domain;

/// <summary>El precio de un plan, como lo publicó la oferta.</summary>
public sealed record PlanOfertado(string Codigo, decimal Total);

/// <summary>Lo que este orquestador publicó de un curso: el rastro de sus propios pasos.</summary>
/// <param name="Retirada">Si la oferta se retiró: sus precios dejaron de valer.</param>
public sealed record CursoOfertado(string Curso, decimal Precio, string Moneda, IReadOnlyList<PlanOfertado> Planes, bool Retirada);

/// <summary>El rastro de las ofertas publicadas, en el almacén del orquestador (ADR 0105).</summary>
/// <remarks>
/// No es una copia de los precios —esos son de la capacidad de precios—: es lo que el propio orquestador publicó,
/// para saber qué plan retirar cuando una publicación nueva ya no lo trae.
/// </remarks>
public sealed class RastroDeCursos(string raiz)
{
    private readonly JsonCollectionStore<CursoOfertado> _cursos = new(raiz, "cursos", c => c.Curso);

    /// <summary>La carpeta del rastro dentro del almacén del orquestador.</summary>
    public string Raiz { get; } = raiz;

    /// <summary>El rastro en la carpeta <c>ofertas/</c> del almacén de <c>educacion</c> (el de sus sagas).</summary>
    public static RastroDeCursos De(IConfiguration configuracion)
        => new(Path.Combine(configuracion["educacion:Storage:Root"] ?? new SagaStorageOptions().Root, "ofertas"));

    public CursoOfertado? Buscar(string curso) => _cursos.Find(curso);

    public void Guardar(CursoOfertado oferta) => _cursos.Put(oferta);
}

/// <summary>
/// Publica la oferta de un curso: su precio y el de cada plan en la capacidad de precios (010-educacion-matricula,
/// <c>contracts/oferta.md</c>).
/// </summary>
/// <remarks>
/// <para><b>Es código del vertical</b> y no del caso común: el molde de <c>OfertaDeReservas</c>, sin agenda. La oferta
/// genérica del núcleo (P12, NM-1) la dejará lista para retirar. Se mide aparte del flujo.</para>
///
/// <para><b>Un curso gratis publica su cero</b>: así la AUSENCIA de precio sigue diciendo «no disponible», y la
/// matrícula, que cotiza sin caer a cero, nunca deja gratis un curso de pago cuya oferta no llegó.</para>
///
/// <para><b>Publicar reemplaza</b>: el plan que ya no viene deja de valer desde ahora. <b>Retirar no borra</b>: los
/// precios dejan de valer, y cotizar —que va primero en el flujo— rechaza sin autorizar nada.</para>
/// </remarks>
public sealed class OfertaDeCursos(CapacidadesDeLaOferta capacidades, RastroDeCursos rastro, TimeProvider reloj) : IDisposable
{
    public const string Invalida = "educacion.oferta_invalida";
    public const string NoExiste = "educacion.oferta_no_existe";

    private readonly StoreWriteGate _turno = new(rastro.Raiz, "educacion");

    public void Dispose() => _turno.Dispose();

    /// <summary>Publica la oferta. Repetirla con la misma llave no duplica nada.</summary>
    public async Task<Result<CursoOfertado>> PublicarAsync(PublicarOfertaDeCursoRequest o, IdempotencyKey llave, CancellationToken ct)
    {
        if (Revisar(o) is { } motivo) return Result.Rejected<CursoOfertado>(motivo);
        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<CursoOfertado>(_turno.Ocupado);

        var curso = Ref.Create(SujetoDelPlan.ClaseDelCurso, o.Curso!.Trim());
        var moneda = o.Moneda!.Trim().ToUpperInvariant();
        var planes = (o.Planes ?? []).Select(p => new PlanOfertado(p.Codigo!.Trim(), p.Total!.Value)).ToList();

        var precio = Money.Of(o.Precio!.Value, moneda);
        var fijado = await capacidades.FijarPrecioAsync(SujetoDelPlan.De(curso, null), precio, null,
            Derivada(llave.Value, "precio", curso.Id, precio.ToString()), ct);
        if (!fijado.IsOk) return Result.Rejected<CursoOfertado>(fijado.Rejection!);

        foreach (var plan in planes)
        {
            var total = Money.Of(plan.Total, moneda);
            var hecho = await capacidades.FijarPrecioAsync(SujetoDelPlan.De(curso, plan.Codigo), total, null,
                Derivada(llave.Value, "plan", curso.Id, plan.Codigo, total.ToString()), ct);
            if (!hecho.IsOk) return Result.Rejected<CursoOfertado>(hecho.Rejection!);
        }

        if (rastro.Buscar(curso.Id) is { Retirada: false } previa)
        {
            var ahora = reloj.GetUtcNow();
            foreach (var quitado in previa.Planes.Where(p => planes.All(n => n.Codigo != p.Codigo)))
            {
                var retirado = await capacidades.FijarPrecioAsync(SujetoDelPlan.De(curso, quitado.Codigo),
                    Money.Of(quitado.Total, previa.Moneda), ahora, Derivada(llave.Value, "quitar", curso.Id, quitado.Codigo), ct);
                if (!retirado.IsOk) return Result.Rejected<CursoOfertado>(retirado.Rejection!);
            }
        }

        var oferta = new CursoOfertado(curso.Id, precio.Amount, moneda, planes, Retirada: false);
        rastro.Guardar(oferta);
        return Result.Ok(oferta);
    }

    /// <summary>Retira la oferta: los precios del curso y de sus planes dejan de valer desde ahora. Repetirlo no hace daño.</summary>
    public async Task<Result<CursoOfertado>> RetirarAsync(string curso, IdempotencyKey llave, CancellationToken ct)
    {
        using var turno = await _turno.TryEnterAsync(ct);
        if (turno is null) return Result.Rejected<CursoOfertado>(_turno.Ocupado);

        if (rastro.Buscar(curso) is not { } previa)
        {
            return Result.Rejected<CursoOfertado>(Rejection.NotFound(NoExiste, $"El curso {curso} nunca se publicó."));
        }
        if (previa.Retirada) return Result.Ok(previa);

        var ahora = reloj.GetUtcNow();
        var sujeto = Ref.Create(SujetoDelPlan.ClaseDelCurso, curso);
        var retirados = new List<(Ref Sujeto, decimal Total, string Parte)> { (SujetoDelPlan.De(sujeto, null), previa.Precio, "curso") };
        retirados.AddRange(previa.Planes.Select(p => (SujetoDelPlan.De(sujeto, p.Codigo), p.Total, p.Codigo)));
        foreach (var (s, total, parte) in retirados)
        {
            var hecho = await capacidades.FijarPrecioAsync(s, Money.Of(total, previa.Moneda), ahora,
                Derivada(llave.Value, "retirar", curso, parte), ct);
            if (!hecho.IsOk) return Result.Rejected<CursoOfertado>(hecho.Rejection!);
        }

        var hecha = previa with { Retirada = true };
        rastro.Guardar(hecha);
        return Result.Ok(hecha);
    }

    internal static Rejection? Revisar(PublicarOfertaDeCursoRequest o)
    {
        if (!SujetoDelPlan.EsClave(o.Curso?.Trim())) return Mal("curso", "hace falta la clave del curso");
        if (o.Precio is not >= 0m) return Mal("precio", "tiene que ser mayor o igual que 0");
        if (o.Moneda?.Trim() is not { Length: 3 } moneda || !moneda.All(char.IsAsciiLetter)) return Mal("moneda", "tiene que ser un código ISO 4217");

        var codigos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in o.Planes ?? [])
        {
            if (!SujetoDelPlan.EsClave(p.Codigo?.Trim())) return Mal("planes.codigo", $"«{p.Codigo}» no es el código de un plan");
            if (p.Total is not >= 0m) return Mal("planes.total", $"el del plan «{p.Codigo}» tiene que ser mayor o igual que 0");
            if (!codigos.Add(p.Codigo!.Trim())) return Mal("planes.codigo", $"«{p.Codigo}» está dos veces");
        }

        return null;

        static Rejection Mal(string campo, string motivo) => Rejection.Invalid(Invalida, $"La oferta no se publica: «{campo}» {motivo}.");
    }

    private static IdempotencyKey Derivada(params string?[] partes)
        => IdempotencyKey.Of("oferta|" + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', partes.Select(p => p ?? "-"))))).ToLowerInvariant()[..40]);
}
