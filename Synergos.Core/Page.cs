namespace Synergos.Core;

/// <summary>
/// Una porción de una lista más larga.
/// </summary>
/// <param name="Items">Lo que cabe en esta porción.</param>
/// <param name="Total">Cuántos hay en total, no cuántos vienen aquí.</param>
/// <param name="Offset">Desde cuál arranca esta porción.</param>
/// <remarks>
/// <para>Paginar una vez y no dieciséis. Sin un tipo común, cada capacidad elige
/// <c>page/size</c>, <c>offset/limit</c> o <c>cursor</c>, y el orquestador que consulta a tres
/// escribe tres paginaciones distintas para pintar una sola lista.</para>
///
/// <para><b><see cref="Total"/> separado de <c>Items.Count</c></b> porque son preguntas
/// distintas: "cuántos hay" mueve la paginación de la interfaz, "cuántos vinieron" no. Confundirlos
/// produce el clásico "página 2 de 1".</para>
/// </remarks>
public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int Offset)
{
    /// <summary>Página vacía — el resultado honesto de una búsqueda sin coincidencias.</summary>
    public static Page<T> Empty(int offset = 0) => new(Array.Empty<T>(), 0, offset);

    /// <summary>Si quedan elementos después de esta porción.</summary>
    public bool HasMore => Offset + Items.Count < Total;

    /// <summary>Transforma los elementos conservando el conteo.</summary>
    public Page<TOut> Map<TOut>(Func<T, TOut> map)
        => new(Items.Select(map).ToList(), Total, Offset);
}
