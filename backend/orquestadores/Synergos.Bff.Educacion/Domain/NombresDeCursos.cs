using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Educacion.Contracts;
using Synergos.Bff.Pasos;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Educacion.Domain;

/// <summary>Cómo se llaman un curso y sus planes, como los publicó el CMS.</summary>
public sealed record NombresDelCurso(string Curso, string? Nombre, IReadOnlyDictionary<string, string> Planes);

/// <summary>
/// Los nombres de los cursos, en el almacén del orquestador (ADR 0105), para el aviso de la matrícula (NM-2).
/// </summary>
/// <remarks>
/// <para><b>Es interino, y declarado (NM-3)</b>: la oferta común del núcleo (P12, 001-P11) publica los precios de la
/// pieza y de sus variantes, y no cómo se llaman. Los nombres viajan aparte, desde la verdad del servidor (el CMS), nunca
/// desde el navegador. Cuando la oferta común los lleve, esto se retira.</para>
/// </remarks>
public sealed class NombresDeCursos(string raiz)
{
    public const string Invalidos = "educacion.nombres_invalidos";

    /// <summary>Lo más largo que puede ser un título o el nombre de un plan: van en el asunto de un correo.</summary>
    public const int NombreMaximo = 200;

    private readonly JsonCollectionStore<NombresDelCurso> _cursos = new(raiz, "nombres", n => n.Curso);

    /// <summary>Los nombres en la carpeta <c>ofertas/</c> del almacén de <c>educacion</c>, junto a la oferta común.</summary>
    public static NombresDeCursos De(IConfiguration configuracion)
        => new(Path.Combine(configuracion["educacion:Storage:Root"] ?? new SagaStorageOptions().Root, "ofertas"));

    public NombresDelCurso? Buscar(string curso) => _cursos.Find(curso);

    /// <summary>Guarda los nombres del curso; publicarlos otra vez los reemplaza.</summary>
    public Result<NombresDelCurso> Guardar(string curso, NombresDelCursoRequest o)
    {
        ArgumentNullException.ThrowIfNull(o);
        if (Revisar(curso, o) is { } motivo) return Result.Rejected<NombresDelCurso>(motivo);

        var planes = (o.Planes ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p.Nombre))
            .ToDictionary(p => p.Codigo!.Trim(), p => p.Nombre!.Trim(), StringComparer.Ordinal);
        var nombres = new NombresDelCurso(curso.Trim(), string.IsNullOrWhiteSpace(o.Nombre) ? null : o.Nombre.Trim(), planes);
        _cursos.Put(nombres);
        return Result.Ok(nombres);
    }

    private static Rejection? Revisar(string curso, NombresDelCursoRequest o)
    {
        if (!SujetoDeLaOferta.EsClave(curso?.Trim())) return Mal("curso", "hace falta la clave del curso");
        if (o.Nombre?.Trim().Length > NombreMaximo) return Mal("nombre", $"pasa de {NombreMaximo} caracteres");

        var codigos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in o.Planes ?? [])
        {
            if (!SujetoDeLaOferta.EsClave(p.Codigo?.Trim())) return Mal("planes.codigo", $"«{p.Codigo}» no es el código de un plan");
            if (!codigos.Add(p.Codigo!.Trim())) return Mal("planes.codigo", $"«{p.Codigo}» está dos veces");
            if (p.Nombre?.Trim().Length > NombreMaximo) return Mal("planes.nombre", $"el del plan «{p.Codigo}» pasa de {NombreMaximo} caracteres");
        }

        return null;

        static Rejection Mal(string campo, string motivo) => Rejection.Invalid(Invalidos, $"Los nombres no se guardan: «{campo}» {motivo}.");
    }
}

/// <summary>
/// <c>educacion.nombres</c>: cómo se llaman el curso y el plan que eligió el alumno, para el aviso (NM-2 con T091).
/// </summary>
/// <remarks>
/// Lo que queda de <c>educacion.linea</c> tras adoptar la oferta común: la línea a cotizar la compone
/// <c>forma.una-linea-de-oferta</c>, y esto sólo dice los nombres. Sin nombres publicados cae a la clave del curso y al
/// código del plan; sin plan, el plan no tiene nombre. Interino (NM-3).
/// </remarks>
public sealed class PasoNombresDelCurso(NombresDeCursos? nombres = null) : IPaso
{
    public string Tipo => "educacion.nombres";
    public int Lecturas => 2;
    public int Escrituras => 2;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string), typeof(string)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var curso = entrada.Lee<Ref>(0);
        var plan = entrada.Lee<string?>(1)?.Trim();
        var publicados = nombres?.Buscar(curso.Id);

        var nombreDelCurso = publicados?.Nombre ?? curso.Id;
        var nombreDelPlan = string.IsNullOrEmpty(plan)
            ? null
            : publicados is not null && publicados.Planes.TryGetValue(plan, out var nombre) ? nombre : plan;
        return Task.FromResult(SalidaDePaso.Sigue(nombreDelCurso, nombreDelPlan));
    }
}
