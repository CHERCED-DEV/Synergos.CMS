using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Educacion.Domain;

/// <summary>
/// <c>educacion.linea</c>: la línea a cotizar de una matrícula, desde el curso y el plan que eligió el alumno
/// (010-educacion-matricula, R7). Sin plan, el precio del curso. Y, para el aviso, cómo se llaman.
/// </summary>
/// <remarks>
/// <para><b>Es un paso de dominio interino</b> (los pasos de forma no llevan parámetros, lote 2 001-P2): la otra cara
/// de la oferta de los cursos, con el MISMO nombre de sujeto (<see cref="SujetoDelPlan"/>). Atribuido a NM-1.</para>
///
/// <para><b>Una clave con otra forma no llega a precios</b>: se rechaza aquí con el código de la entrada inválida, en
/// vez de cotizar un sujeto que la oferta nunca habría podido publicar.</para>
///
/// <para><b>El título del curso y el nombre del plan salen de la oferta</b> que publicó el CMS (NM-2 de 010 con los
/// valores declarados del núcleo, T091): el curso viaja como referencia, que no se escribe en un correo, y el orquestador
/// sólo sabe su clave. Nunca del navegador. Una oferta sin nombres —publicada antes de que los trajera— cae a la clave
/// del curso y al código del plan; sin plan, el plan no tiene nombre. Son las dos últimas escrituras, y una definición
/// puede no nombrarlas.</para>
/// </remarks>
public sealed class PasoLineaDelPlan(RastroDeCursos? rastro = null) : IPaso
{
    /// <summary>El código de una clave de curso o de plan con otra forma.</summary>
    public const string EntradaInvalida = "educacion.entrada_invalida";

    public string Tipo => "educacion.linea";
    public int Lecturas => 2;
    public int Escrituras => 3;
    public int EscriturasOpcionales => 2;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(IReadOnlyList<(Ref Sujeto, int Cantidad)>), typeof(string), typeof(string)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var curso = entrada.Lee<Ref>(0);
        var plan = entrada.Lee<string?>(1)?.Trim();
        if (!SujetoDelPlan.EsClave(curso.Id) || (!string.IsNullOrEmpty(plan) && !SujetoDelPlan.EsClave(plan)))
        {
            return Task.FromResult(SalidaDePaso.Rechaza(Rejection.Invalid(EntradaInvalida,
                "El curso o el plan no tienen la forma de una clave del catálogo.")));
        }

        IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas = [(SujetoDelPlan.De(curso, plan), 1)];
        var oferta = rastro?.Buscar(curso.Id);
        var nombreDelCurso = oferta?.Nombre ?? curso.Id;
        var nombreDelPlan = string.IsNullOrEmpty(plan)
            ? null
            : oferta?.Planes.FirstOrDefault(p => p.Codigo == plan)?.Nombre ?? plan;
        return Task.FromResult(SalidaDePaso.Sigue(lineas, nombreDelCurso, nombreDelPlan));
    }
}
