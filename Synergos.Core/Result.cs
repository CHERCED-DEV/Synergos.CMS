namespace Synergos.Core;

/// <summary>
/// O el valor, o el rechazo. Nunca los dos, nunca ninguno.
/// </summary>
/// <remarks>
/// <para><b>Por qué esto y no excepciones.</b> Un rechazo de negocio —cupo lleno, promoción
/// vencida— <b>no es excepcional</b>: es una respuesta prevista, frecuente, y parte del contrato.
/// Modelarlo como excepción lo saca del tipo de retorno, y entonces el que llama no tiene manera
/// de saber qué puede pasarle sin leer la implementación. Las excepciones se quedan para lo que
/// sí es excepcional: un disco lleno, un bug.</para>
///
/// <para><b>Deliberadamente mínimo.</b> No es una mónada ni el principio de una librería
/// funcional: es un par etiquetado con la disciplina de que no se puede leer el valor de un
/// rechazo. Todo lo demás —<c>Bind</c>, <c>Traverse</c>, operadores— se agrega el día que haga
/// falta de verdad, y probablemente no haga falta.</para>
/// </remarks>
public readonly struct Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        Rejection = null;
    }

    private Result(Rejection rejection)
    {
        _value = default;
        Rejection = rejection;
    }

    /// <summary>El rechazo, o <c>null</c> si salió bien.</summary>
    public Rejection? Rejection { get; }

    public bool IsOk => Rejection is null;

    /// <summary>El valor. Solo si <see cref="IsOk"/>.</summary>
    /// <exception cref="InvalidOperationException">Si se lee el valor de un rechazo.</exception>
    public T Value => IsOk
        ? _value!
        : throw new InvalidOperationException(
            $"Se leyó el valor de un Result rechazado ({Rejection}). Hay que preguntar por IsOk primero.");

    public static Result<T> Ok(T value) => new(value);

    public static Result<T> Rejected(Rejection rejection) => new(rejection);

    /// <summary>Conversión implícita para poder escribir <c>return valor;</c>.</summary>
    public static implicit operator Result<T>(T value) => Ok(value);

    /// <summary>Conversión implícita para poder escribir <c>return Rejection.Conflict(...);</c>.</summary>
    public static implicit operator Result<T>(Rejection rejection) => Rejected(rejection);

    /// <summary>
    /// Colapsa las dos ramas a un solo valor. Obliga a contemplar el rechazo.
    /// </summary>
    public TOut Match<TOut>(Func<T, TOut> ok, Func<Rejection, TOut> rejected)
        => IsOk ? ok(_value!) : rejected(Rejection!);

    /// <summary>Transforma el valor y deja pasar el rechazo intacto.</summary>
    public Result<TOut> Map<TOut>(Func<T, TOut> map)
        => IsOk ? Result<TOut>.Ok(map(_value!)) : Result<TOut>.Rejected(Rejection!);

    public override string ToString() => IsOk ? $"Ok({_value})" : $"Rejected({Rejection})";
}

/// <summary>Atajos para no repetir el tipo genérico al construir.</summary>
public static class Result
{
    public static Result<T> Ok<T>(T value) => Result<T>.Ok(value);

    public static Result<T> Rejected<T>(Rejection rejection) => Result<T>.Rejected(rejection);
}
