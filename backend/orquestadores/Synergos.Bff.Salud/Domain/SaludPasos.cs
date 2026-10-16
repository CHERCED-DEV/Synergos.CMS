using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Salud.Domain;

/// <summary>
/// <c>salud.profesional</c>: lee el identificador del profesional que manda el navegador y escribe su referencia
/// con la clase FIJA <see cref="Clase"/>.
/// </summary>
/// <remarks>
/// <b>Interino</b> (NR-1, hallada por 008; NN-16 de esta línea): el tipo <c>ref</c> del cuerpo acepta cualquier
/// clase, y <c>booking.hallar</c> busca en la agenda que comparten todos los verticales, así que con
/// <c>"profesional": "ref"</c> un miembro podía nombrar el recurso de otro vertical (<c>realty.listado:…</c>) y
/// apartarlo. Cuando la definición pueda declarar la clase de una referencia y el host rechace otra, este paso
/// se va y el cuerpo vuelve a traer la referencia.
/// </remarks>
public sealed class PasoProfesionalDeSalud : IPaso
{
    /// <summary>La única clase de agenda que la cita puede apartar.</summary>
    public const string Clase = "salud.profesional";

    public string Tipo => "salud.profesional";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(Ref)];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var profesional = Ref.TryCreate(Clase, entrada.Lee<string>(0));
        return Task.FromResult(profesional is null
            ? SalidaDePaso.Rechaza(Rejection.Invalid("salud.bad_professional", "Hace falta el profesional."))
            : SalidaDePaso.Sigue(profesional));
    }
}
