namespace Synergos.Bff.Educacion.Contracts;

// Lo que cruza el cable de los nombres de un curso (010-educacion-matricula, NM-3). Todo anulable con `= null` en lo
// que llega (molde F2 de la ADR 0140): el dominio dice qué falta, con su código.

/// <summary>Cómo se llama un plan del curso: <c>{ codigo, nombre }</c>.</summary>
/// <param name="Nombre">Como lo lee el alumno («Pago de contado»): lo que dice el aviso de la matrícula.</param>
public sealed record NombreDelPlanDto(string? Codigo = null, string? Nombre = null);

/// <summary>Cómo se llaman un curso y sus planes (la llama el CMS al publicar la oferta del curso).</summary>
/// <param name="Nombre">El título del curso, como lo pinta la ficha.</param>
/// <param name="Planes">El nombre de cada plan ofrecido.</param>
public sealed record NombresDelCursoRequest(string? Nombre = null, IReadOnlyList<NombreDelPlanDto>? Planes = null);

/// <summary>Cómo quedaron los nombres del curso.</summary>
public sealed record NombresDelCursoResponse(string Curso, string? Nombre, IReadOnlyList<string> Planes);
