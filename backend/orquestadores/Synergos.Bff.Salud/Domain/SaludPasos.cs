using Synergos.Bff.Core.Flow;
using Synergos.Bff.Salud.Clients;
using Synergos.Core;

namespace Synergos.Bff.Salud.Domain;

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
