namespace Synergos.Core;

/// <summary>
/// La llave que hace que repetir una operación no la ejecute dos veces.
/// </summary>
/// <remarks>
/// <para><b>Por qué es un tipo y no un <c>string</c>.</b> Porque en una arquitectura de
/// capacidades, "reintentar es seguro" deja de ser una cortesía y pasa a ser la única forma de
/// sobrevivir a un timeout: cuando <c>Api.Payments</c> no contesta, el orquestador <b>no sabe</b>
/// si cobró o no. Sin llave, reintentar cobra dos veces y no reintentar puede dejar un pedido sin
/// pagar. Con un <c>string</c> suelto, esa garantía es una promesa escrita en un comentario que
/// alguien borra.</para>
///
/// <para><b>Quién la genera.</b> El <i>llamador</i>, y la reusa en cada reintento del mismo
/// intento lógico. Si la generara el receptor, cada reintento traería una llave nueva y no serviría
/// para nada — que es el error habitual.</para>
/// </remarks>
public readonly record struct IdempotencyKey(string Value)
{
    /// <summary>Largo máximo. Suficiente para un GUID con prefijo, y acotado porque va a un índice.</summary>
    public const int MaxLength = 128;

    /// <summary>Construye validando.</summary>
    /// <exception cref="ArgumentException">Si va vacía o pasa de <see cref="MaxLength"/>.</exception>
    public static IdempotencyKey Of(string value)
    {
        var v = value?.Trim() ?? string.Empty;
        if (v.Length == 0)
        {
            throw new ArgumentException(
                "Una llave de idempotencia vacía no protege de nada, y da la sensación de que sí.", nameof(value));
        }
        if (v.Length > MaxLength)
        {
            throw new ArgumentException($"La llave pasa de {MaxLength} caracteres.", nameof(value));
        }
        return new IdempotencyKey(v);
    }

    /// <summary>
    /// Deriva una llave estable a partir de las partes de una operación.
    /// </summary>
    /// <remarks>
    /// Útil cuando el llamador no tiene una llave natural: la misma operación lógica produce la
    /// misma llave. Se une con <c>|</c> y no concatenando, porque <c>("ab","c")</c> y
    /// <c>("a","bc")</c> darían la misma llave — y serían dos operaciones distintas compartiendo
    /// protección.
    /// </remarks>
    public static IdempotencyKey From(params string[] parts) => Of(string.Join('|', parts));

    public override string ToString() => Value;
}
