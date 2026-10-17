using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Alquiler.Domain;

/// <summary>Un alquiler de la puerta anotado con su dueño: lo que la lista de «mis alquileres» necesita encontrar.</summary>
/// <param name="SagaId">La saga de la renta.</param>
/// <param name="DuenoKind">El <c>Kind</c> del dueño, el que puso la puerta.</param>
/// <param name="DuenoId">Su id.</param>
/// <param name="AnotadoUtc">Cuándo se anotó: al cobrarse el alquiler.</param>
public sealed record AlquilerAnotado(string SagaId, string DuenoKind, string DuenoId, DateTimeOffset AnotadoUtc);

/// <summary>
/// Los alquileres de la puerta de cada dueño (línea 005, T024): lo que el CMS reconcilia en «mis alquileres» cuando
/// quien alquiló cerró la pestaña justo después de pagar (FR-020).
/// </summary>
/// <remarks>
/// <para><b>Pegamento atribuido a NN-7</b> (<c>specs/005-alquiler-renta/necesidades-del-nucleo.md</c>): el host genérico
/// todavía no lista las sagas de un flujo por dueño. Cuando lo haga, este índice y su paso se retiran y la lista sale
/// de ahí. No es una segunda verdad del alquiler: guarda sólo qué saga es de quién; el estado se lee de la saga.</para>
///
/// <para>Se anota en <c>cerrar</c>, en cuanto se cobra: desde ese momento el alquiler tiene que poder encontrarse, aunque
/// el resto del cierre falle y se deshaga (la lista dice entonces en qué quedó).</para>
/// </remarks>
public sealed class AlquileresDeLaPuerta
{
    private readonly JsonCollectionStore<AlquilerAnotado> _indice;
    private readonly TimeProvider _reloj;

    /// <summary>Construye el índice.</summary>
    /// <param name="almacen">Dónde viven las sagas del orquestador; el índice va al lado.</param>
    /// <param name="reloj">El reloj.</param>
    public AlquileresDeLaPuerta(IOptions<SagaStorageOptions> almacen, TimeProvider reloj)
    {
        ArgumentNullException.ThrowIfNull(almacen);
        _reloj = reloj ?? throw new ArgumentNullException(nameof(reloj));
        _indice = new JsonCollectionStore<AlquilerAnotado>(
            Path.Combine(almacen.Value.Root, "alquileres-de-la-puerta"), "duenos", a => a.SagaId);
    }

    /// <summary>Anota la saga con su dueño. Anotarla otra vez no cambia nada.</summary>
    /// <param name="sagaId">La saga.</param>
    /// <param name="dueno">Su dueño.</param>
    public void Anotar(string sagaId, Ref dueno)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sagaId);
        ArgumentNullException.ThrowIfNull(dueno);
        if (_indice.Find(sagaId) is null)
        {
            _indice.Put(new AlquilerAnotado(sagaId, dueno.Kind, dueno.Id, _reloj.GetUtcNow()));
        }
    }

    /// <summary>Los alquileres de un dueño, del más reciente al más viejo.</summary>
    /// <param name="dueno">El dueño, tal como lo pone la puerta.</param>
    /// <returns>Sus alquileres anotados.</returns>
    public IReadOnlyList<AlquilerAnotado> De(Ref dueno)
    {
        ArgumentNullException.ThrowIfNull(dueno);
        return _indice
            .Where(a => string.Equals(a.DuenoKind, dueno.Kind, StringComparison.Ordinal)
                        && string.Equals(a.DuenoId, dueno.Id, StringComparison.Ordinal))
            .OrderByDescending(a => a.AnotadoUtc)
            .ThenBy(a => a.SagaId, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>
/// <c>alquiler.anotar</c>: anota la saga con su dueño —el <c>inquilino</c> que puso la puerta— en
/// <see cref="AlquileresDeLaPuerta"/>. No escribe nada. Pegamento de NN-7 mientras el host no liste por dueño.
/// </summary>
/// <remarks>
/// Lee al inquilino y no usa <c>Origen</c>: el origen de un paso es la referencia de la propia saga (con él se nombran
/// los cobros), no quién la abrió.
/// </remarks>
public sealed class PasoAnotarDueno(AlquileresDeLaPuerta indice) : IPaso
{
    public string Tipo => "alquiler.anotar";
    public int Lecturas => 1;
    public int Escrituras => 0;
    public LlaveRequerida Llave => LlaveRequerida.Ninguna;
    public IReadOnlyList<Type> TiposDeEscritura => [];

    public Task<SalidaDePaso> EjecutarAsync(EntradaDePaso entrada, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        indice.Anotar(entrada.SagaId, entrada.Lee<Ref>(0));
        return Task.FromResult(SalidaDePaso.Sigue());
    }
}
