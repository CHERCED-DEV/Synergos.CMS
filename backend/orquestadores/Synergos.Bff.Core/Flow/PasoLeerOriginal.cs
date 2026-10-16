using System.Text.Json;
using Synergos.Core;

namespace Synergos.Bff.Core.Flow;

/// <summary>
/// <c>flow.leer-original</c>: en una operación posterior al cierre (núcleo, P6 = A del lote 6), lee la saga ORIGINAL por
/// su id y escribe lo que la definición declara de su salida (<c>{ "tipo": …, "de": "original" }</c>).
/// </summary>
/// <remarks>
/// <para><b>Una operación posterior es un flujo aparte</b> (<c>posterior_a</c>), con su saga, su llave, su deshacer y su
/// barrido. La original está completada y no se toca —completar es la escritura que no se deshace—: este paso sólo la
/// lee. Por eso no decide nada de una espera humana (aprobar una devolución): esa forma es otra pregunta.</para>
///
/// <para><b>Sólo la del mismo dueño.</b> Una que no existe, que es de otro dueño o que es de otro flujo contesta lo
/// mismo, para no enseñar qué hay detrás. Y sólo una COMPLETADA: sobre una en curso, lo posterior todavía no existe
/// (cancelarla es la operación cancelar de su propio flujo), y una deshecha no tiene nada que devolver.</para>
///
/// <para><b>Lee con el tipo que la original guardó</b> y comprueba que es el que declara este flujo: si una versión nueva
/// cambió el tipo, lo dice en vez de leer otra cosa. Un dato que la original no tiene —el cobro de una cita gratis— sólo
/// pasa si este flujo lo declara anulable; si no, es un rechazo con su nombre.</para>
/// </remarks>
public sealed class PasoLeerOriginal(ISagaStore<SagaDeFlujo> almacen, TiposDeValor tipos) : IPaso
{
    /// <summary>El tipo con que lo nombra una definición.</summary>
    public const string Nombre = "flow.leer-original";

    /// <summary>No existe, es de otro dueño o es de otro flujo: lo mismo, a propósito.</summary>
    public const string NoEncontrada = "flow.original_no_encontrada";

    /// <summary>Existe y es suya, pero no está completada.</summary>
    public const string NoCompletada = "flow.original_no_completada";

    /// <summary>La original no tiene un dato que este flujo declara obligatorio.</summary>
    public const string SinDato = "flow.original_sin_dato";

    /// <summary>Cuántos datos puede leer de la original: los que declare la definición, hasta éstos.</summary>
    public const int Tope = 16;

    public string Tipo => Nombre;
    public int Lecturas => 1;

    /// <remarks>Escribe lo que la definición declara <c>de: original</c>, de cero a <see cref="Tope"/>, con el tipo que
    /// declara: por eso no declara <see cref="IPaso.TiposDeEscritura"/>.</remarks>
    public int Escrituras => Tope;
    public int EscriturasOpcionales => Tope;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public bool LeeLaOriginal => true;

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var flujo = entrada.Flujo
            ?? throw new InvalidOperationException($"«{Nombre}» corre dentro de un flujo, y su entrada no lo trae.");
        var deCual = flujo.PosteriorA
            ?? throw new InvalidOperationException($"El flujo «{flujo.Clave}» da «{Nombre}» y no declara «posterior_a».");
        var id = entrada.Lee<string>(0);

        var original = string.IsNullOrWhiteSpace(id) ? null : almacen.Find(id);
        if (original is null || !string.Equals(original.Flujo, deCual, StringComparison.Ordinal) || original.Dueno != entrada.Dueno)
        {
            return Rechaza(Rejection.NotFound(NoEncontrada, $"No hay una «{deCual}» tuya con ese id."));
        }
        if (original.Status != SagaStatus.Completed)
        {
            return Rechaza(Rejection.Conflict(NoCompletada,
                $"«{deCual}» {id} está {original.Status}: sólo sobre una completada hay algo posterior que hacer."));
        }

        var valores = new object?[entrada.Paso.Escribe.Count];
        for (var i = 0; i < valores.Length; i++)
        {
            var nombre = entrada.Paso.Escribe[i];
            var declarado = tipos.Resolver(flujo.Tipos![nombre].Tipo!)
                ?? throw new InvalidOperationException($"«{flujo.Clave}» declara «{nombre}» con un tipo que nadie registró.");

            if (original.Datos.GetValueOrDefault(nombre) is not { Valor: { ValueKind: not JsonValueKind.Null } } guardado)
            {
                if (!declarado.Anulable)
                {
                    return Rechaza(Rejection.Conflict(SinDato, $"«{deCual}» {id} no tiene «{nombre}»."));
                }
                continue;
            }

            var comoSeGuardo = tipos.Resolver(guardado.Tipo)
                ?? throw new InvalidOperationException($"«{deCual}» {id} guarda «{nombre}» como «{guardado.Tipo}», un tipo que nadie registró.");
            if (!string.Equals(comoSeGuardo.Tipo.Nombre, declarado.Tipo.Nombre, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"«{deCual}» {id} guarda «{nombre}» como «{guardado.Tipo}», y «{flujo.Clave}» lo lee como «{declarado.Declarado}».");
            }
            valores[i] = comoSeGuardo.Leer(guardado.Valor, $"«{deCual}» {id}, «{nombre}»");
        }

        return Task.FromResult(SalidaDePaso.Sigue(valores));
    }

    private static Task<SalidaDePaso> Rechaza(Rejection rechazo) => Task.FromResult(SalidaDePaso.Rechaza(rechazo));
}
