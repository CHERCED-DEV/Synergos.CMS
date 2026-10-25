namespace Synergos.Bff.Educacion.Contracts;

// Lo que cruza el cable de la oferta (010-educacion-matricula, contracts/oferta.md). Todo anulable con `= null`
// en lo que llega (molde F2 de la ADR 0140): el dominio dice qué falta, con su código.

/// <summary>El precio de un plan del curso: <c>{ codigo, total, nombre }</c>.</summary>
/// <param name="Nombre">Como lo lee el alumno («Pago de contado»): lo que dice el aviso de la matrícula (NM-2).</param>
public sealed record PlanDeLaOfertaDto(string? Codigo = null, decimal? Total = null, string? Nombre = null);

/// <summary>Publicar la oferta de un curso (la llama el CMS al publicarlo, o «republicar» a mano).</summary>
/// <param name="Curso">La clave estable del curso.</param>
/// <param name="Precio">Lo que vale el curso sin plan; cero si es gratis (se publica igual).</param>
/// <param name="Moneda">La moneda (ISO 4217).</param>
/// <param name="Planes">Los planes del curso con su total; un total cero también se publica.</param>
/// <param name="Nombre">El título del curso, como lo pinta la ficha: lo que dice el aviso de la matrícula (NM-2).</param>
public sealed record PublicarOfertaDeCursoRequest(
    string? Curso = null,
    decimal? Precio = null,
    string? Moneda = null,
    IReadOnlyList<PlanDeLaOfertaDto>? Planes = null,
    string? Nombre = null);

/// <summary>Cómo quedó publicada la oferta de un curso.</summary>
/// <param name="Precios">Cuántos precios vigentes dejó: el del curso más uno por plan (cero si se retiró).</param>
public sealed record OfertaDeCursoResponse(
    string Curso,
    decimal Precio,
    string Moneda,
    IReadOnlyList<string> Planes,
    int Precios,
    bool Retirada);
