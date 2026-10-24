using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Educacion.Domain;

/// <summary>
/// <c>educacion.linea</c>: la línea a cotizar de una matrícula, desde el curso y el plan que eligió el alumno
/// (010-educacion-matricula, R7). Sin plan, el precio del curso.
/// </summary>
/// <remarks>
/// <para><b>Es un paso de dominio interino</b> (los pasos de forma no llevan parámetros, lote 2 001-P2): la otra cara
/// de la oferta de los cursos, con el MISMO nombre de sujeto (<see cref="SujetoDelPlan"/>). Atribuido a NM-1.</para>
///
/// <para><b>Una clave con otra forma no llega a precios</b>: se rechaza aquí con el código de la entrada inválida, en
/// vez de cotizar un sujeto que la oferta nunca habría podido publicar.</para>
/// </remarks>
public sealed class PasoLineaDelPlan : IPaso
{
    /// <summary>El código de una clave de curso o de plan con otra forma.</summary>
    public const string EntradaInvalida = "educacion.entrada_invalida";

    public string Tipo => "educacion.linea";
    public int Lecturas => 2;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(IReadOnlyList<(Ref Sujeto, int Cantidad)>)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var curso = entrada.Lee<Ref>(0);
        var plan = entrada.Lee<string?>(1);
        if (!SujetoDelPlan.EsClave(curso.Id) || (!string.IsNullOrWhiteSpace(plan) && !SujetoDelPlan.EsClave(plan.Trim())))
        {
            return Task.FromResult(SalidaDePaso.Rechaza(Rejection.Invalid(EntradaInvalida,
                "El curso o el plan no tienen la forma de una clave del catálogo.")));
        }

        IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas = [(SujetoDelPlan.De(curso, plan), 1)];
        return Task.FromResult(SalidaDePaso.Sigue(lineas));
    }
}
