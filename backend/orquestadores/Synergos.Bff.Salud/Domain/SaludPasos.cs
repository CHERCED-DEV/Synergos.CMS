using Synergos.Bff.Core.Flow;
using Synergos.Bff.Salud.Clients;
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

/// <summary>
/// <c>salud.permiso</c>: lee el paciente y comprueba su permiso de agenda (<see cref="AppointmentFlow.ConsentPurpose"/>)
/// en <c>Api.Consent</c>. No escribe nada: sin permiso, el rechazo de la capacidad tal cual.
/// </summary>
/// <remarks>
/// <b>Provisional</b> (003-salud-cita, NN-3): es el paso común <c>consent.verificar</c> que trae N2, escrito aquí
/// para no esperar. Cuando N2 esté integrado, la definición nombra <c>consent.verificar</c> con
/// <c>"proposito": "salud.agenda"</c> y este paso deja de registrarse.
/// </remarks>
public sealed class PasoPermisoDeAgenda(SaludCapabilities caps) : IPaso
{
    public string Tipo => "salud.permiso";
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var permiso = await caps.CheckConsentAsync(entrada.Lee<Ref>(0), AppointmentFlow.ConsentPurpose, ct);
        return permiso.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(permiso.Rejection!);
    }
}
