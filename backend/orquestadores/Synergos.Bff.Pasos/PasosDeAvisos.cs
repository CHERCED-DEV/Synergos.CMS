using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Pasos;

// ── El aviso programado (núcleo, NN-26 de 003, P13 = A) ─────────────────────────
// El recordatorio de una cita, el día antes. El disparo vive en Api.Notifications —un envío «no antes de», que su barrido
// manda cuando llega la hora—, porque es ella la que ya guarda la dirección de cada envío: el orquestador no la guarda
// (ADR 0140 F3). Un disparador genérico en el orquestador entra cuando una pantalla pida arrancar algo que no sea un aviso.

/// <summary>
/// <c>notifications.programar</c>: pide el aviso con la <c>plantilla</c> que declara la definición para que salga no antes de
/// un momento que lee —una ventana (su inicio) o un instante—, menos la antelación en minutos que lee si la definición la
/// nombra. Escribe el id de la entrega y la reserva (NN-26 de 003).
/// </summary>
/// <remarks>
/// <para><b>Lee como <c>notifications.avisar</c></b>: a quién, su contacto (efímero, de la fase) y, además, el momento y la
/// antelación. Los valores fijos son los del aviso sin el total; los declarados en <c>valores</c> se escriben igual, en la
/// hora del sitio (la ventana de la cita, en el correo del día antes).</para>
///
/// <para><b>Sin contacto no se programa nada y se rechaza</b>, al revés que avisar: lo que se programa se reserva, y no hay
/// qué reservar. Quien prefiera seguir sin recordatorio lo dice con <c>al_fallar</c>.</para>
///
/// <para><b>Reserva la entrega</b>: una definición que la declare con <c>antes: "notifications.retirar"</c> la retira si la
/// saga se deshace antes de consumirla. Retirarla después (la cita cancelada semanas más tarde) es el paso
/// <c>notifications.retirar</c>, que lee la entrega. Con la misma llave, la misma entrega.</para>
/// </remarks>
public sealed class PasoProgramarAviso(IAvisosProgramados avisos, TimeZoneInfo zona) : IPaso
{
    public const string SinContacto = "notifications.sin_contacto";

    private static readonly string[] Fijos = ["nombre", "numero", "enlace", "sitio"];

    public string Tipo => "notifications.programar";
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];
    public int Lecturas => 4;
    public int LecturasOpcionales => 1;
    public int Escrituras => 1;
    public int EscriturasOpcionales => 1;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public bool UsaPlantilla => true;
    public IReadOnlyCollection<string>? ValoresFijos => Fijos;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var contacto = entrada.Lee<Contacto?>(1);
        if (contacto is null || string.IsNullOrWhiteSpace(contacto.Correo))
        {
            return SalidaDePaso.Rechaza(Rejection.Invalid(SinContacto, "Sin la dirección de quien lo recibe no hay aviso que programar."));
        }

        var momento = entrada.Lee<object>(2) switch
        {
            TimeWindow ventana => ventana.Start,
            DateTimeOffset instante => instante,
            var otro => throw new InvalidOperationException(
                $"«{Tipo}» programa a partir de una ventana o de un instante, y «{entrada.Paso.Lee[2]}» es {otro?.GetType().Name ?? "nulo"}."),
        };
        var antelacion = entrada.Paso.Lee.Count > 3 ? TimeSpan.FromMinutes(entrada.Lee<int>(3)) : TimeSpan.Zero;

        var valores = new Dictionary<string, string>(PasoAvisar.Valores(entrada.SagaId, contacto, null), StringComparer.Ordinal);
        foreach (var nombre in entrada.Paso.Valores ?? [])
        {
            valores[nombre] = PasoAvisar.Escribir(entrada.LeePorNombre(nombre), zona, nombre);
        }

        var programado = await avisos.ProgramarAsync(
            entrada.Lee<Ref>(0), contacto.Correo, entrada.Paso.Plantilla!, valores, momento - antelacion, entrada.Llave(), ct);
        return programado.IsOk
            ? SalidaDePaso.Reserva(new Reservado(programado.Value), programado.Value)
            : SalidaDePaso.Rechaza(programado.Rejection!);
    }
}

/// <summary>
/// <c>notifications.retirar</c>: retira la entrega que lee antes de que salga (NN-26 de 003). Un acto propio, que se pide
/// cuando haga falta: la cita cancelada semanas después de programar su recordatorio.
/// </summary>
/// <remarks>
/// Retirar una retirada es lo mismo, y una que ya salió no tiene nada que retirar: las dos siguen. Sin entrega (no se
/// programó ninguna), sigue sin pedir nada. No pide llave: la capacidad no la lee.
/// </remarks>
public sealed class PasoRetirarAviso(IAvisosProgramados avisos) : IPaso
{
    public string Tipo => "notifications.retirar";
    public IReadOnlyList<Type> TiposDeEscritura => [];
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public bool ActuaSinLlave => true;

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (entrada.Lee<string?>(0) is not { Length: > 0 } entrega) return SalidaDePaso.Sigue();

        var retirada = await avisos.RetirarAsync(entrega, ct);
        return retirada.IsOk ? SalidaDePaso.Sigue() : SalidaDePaso.Rechaza(retirada.Rejection!);
    }
}

/// <summary>Deshace <c>notifications.programar</c>: retira la entrega antes de que salga (NN-26 de 003).</summary>
public sealed class DeshacerAviso(IAvisosProgramados avisos) : IDeshaceUnTipo
{
    public string Tipo => TiposDeDeshacer.RetirarAviso;

    public async Task<Rejection?> DeshacerAsync(SagaDeFlujo saga, Compensation pendiente, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pendiente);
        var retirada = await avisos.RetirarAsync(pendiente.TargetId, ct);
        return retirada.IsOk ? null : retirada.Rejection;
    }
}
