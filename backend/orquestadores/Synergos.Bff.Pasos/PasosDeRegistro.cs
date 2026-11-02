using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Pasos;

// ── El rastro: quién hizo qué sobre qué (núcleo, NN-12 de 003) ──────────────
// El paso estándar de auditoría de la ADR 0140 (decisión 5) sobre Api.Audit, declarable en cualquier flujo. Se llama
// `audit.anotar` y no `auditar`: el compose deriva de qué capacidades habla un orquestador declarado por el prefijo de
// cada paso, así que con este nombre el despliegue le da Api.Audit a quien lo declare.

/// <summary>
/// <c>audit.anotar</c>: anota en <c>Api.Audit</c> que quien actúa hizo la <c>accion</c> que declara la definición sobre lo
/// que lee (NN-12 de 003). Escribe el id del asiento si la definición lo nombra.
/// </summary>
/// <remarks>
/// <para><b>Quién actúa es el dueño de la saga</b>, el sujeto que puso la puerta: el miembro en un flujo del dueño, la persona
/// del personal en uno del personal —quien abre una historia es justo lo que hay que anotar—. Nunca algo que se lee: un
/// actor leído dejaría anotar a nombre de otro. Sin dueño, no se anota a nombre de nadie y se rechaza.</para>
///
/// <para><b>Falla cerrado.</b> Si el registro no anota, el paso rechaza y la fase no sigue: declarado antes de lo que lee o
/// escribe, nada pasa sin quedar escrito. Quien prefiera seguir lo dice con <c>al_fallar</c>.</para>
///
/// <para><b>Sin datos personales en el rastro</b>, que es permanente: los detalles son el flujo y la saga, no la entrada. Qué
/// se tocó lo dice el objetivo, una referencia, y por qué, la acción.</para>
///
/// <para><b>Con la llave de la saga</b>: repetir la fase no anota dos veces lo mismo. Por eso no cabe en una lectura sin
/// saga, que no tiene de dónde sacar la llave; lo que lee el CMS se anota en el CMS.</para>
/// </remarks>
public sealed class PasoRegistroAnotar(IAuditPort registro) : IPaso
{
    public const string SinActor = "audit.actor_requerido";

    public string Tipo => "audit.anotar";
    public int Lecturas => 1;
    public int Escrituras => 1;
    public int EscriturasOpcionales => 1;
    public LlaveRequerida Llave => LlaveRequerida.Fija;
    public bool UsaAccion => true;
    public IReadOnlyList<Type> TiposDeEscritura => [typeof(string)];

    public async Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (entrada.Dueno is not { } actor)
        {
            return SalidaDePaso.Rechaza(Rejection.Forbidden(SinActor,
                $"«{Tipo}» anota a nombre de quien abrió la saga por la puerta, y aquí no lo hay."));
        }

        var detalles = new Dictionary<string, string>(StringComparer.Ordinal) { ["saga"] = entrada.SagaId };
        if (entrada.Flujo is { } flujo) detalles["flujo"] = flujo.Clave;

        var anotado = await registro.AnotarAsync(actor, entrada.Paso.Accion!, entrada.Lee<Ref>(0), detalles, entrada.Llave(), ct);
        return anotado.IsOk ? SalidaDePaso.Sigue(anotado.Value) : SalidaDePaso.Rechaza(anotado.Rejection!);
    }
}
