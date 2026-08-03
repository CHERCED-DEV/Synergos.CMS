using System.Globalization;

namespace Synergos.Core;

/// <summary>
/// Un monto con su moneda.
/// </summary>
/// <param name="Amount">La cantidad. Puede ser negativa: una devolución lo es.</param>
/// <param name="Currency">Código ISO 4217 en mayúsculas — <c>COP</c>, <c>USD</c>.</param>
/// <remarks>
/// <para><b>Por qué no un <c>decimal</c> suelto.</b> Un decimal permite sumar pesos con dólares
/// sin que nada se queje, y el error no aparece en un test: aparece en un estado de cuenta.
/// Con este tipo, esa suma no compila… bueno, no: no compila el olvido de la moneda, y la suma
/// de monedas distintas <b>lanza</b>. Que lance y no que redondee es deliberado — no hay
/// conversión correcta sin una tasa, y adivinarla en silencio es peor que fallar.</para>
///
/// <para><b>Redondeo bancario</b> (<see cref="MidpointRounding.ToEven"/>) y no "arriba":
/// redondear siempre hacia arriba sesga los totales al alza, y sobre miles de líneas eso es
/// dinero que aparece de la nada.</para>
/// </remarks>
public readonly record struct Money(decimal Amount, string Currency) : IComparable<Money>
{
    /// <summary>Peso colombiano — la moneda por defecto del producto.</summary>
    public const string Cop = "COP";

    /// <summary>Construye validando el código de moneda.</summary>
    public static Money Of(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException($"'{currency}' no es un código ISO 4217.", nameof(currency));
        }
        return new Money(amount, currency.Trim().ToUpperInvariant());
    }

    /// <summary>Cero en la moneda dada. Sumar montos siempre necesita una semilla.</summary>
    public static Money Zero(string currency) => Of(0m, currency);

    /// <summary>Si el monto es exactamente cero. "Gratis" es una decisión, no un `== 0` disperso.</summary>
    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public static Money operator +(Money a, Money b)
    {
        MismaMoneda(a, b);
        return a with { Amount = a.Amount + b.Amount };
    }

    public static Money operator -(Money a, Money b)
    {
        MismaMoneda(a, b);
        return a with { Amount = a.Amount - b.Amount };
    }

    public static Money operator *(Money a, int factor) => a with { Amount = a.Amount * factor };

    public static Money operator -(Money a) => a with { Amount = -a.Amount };

    /// <summary>
    /// Redondea a las unidades mínimas de la moneda.
    /// </summary>
    /// <remarks>
    /// Los decimales por moneda NO se adivinan aquí: se pasan. `COP` se cobra sin centavos y
    /// `USD` con dos, pero esa tabla es un dato de negocio que cambia por país y no tiene por
    /// qué vivir clavada en un tipo del núcleo.
    /// </remarks>
    public Money Round(int decimals)
        => this with { Amount = Math.Round(Amount, decimals, MidpointRounding.ToEven) };

    /// <summary>Suma una secuencia. Vacía devuelve cero en <paramref name="currency"/>.</summary>
    public static Money Sum(IEnumerable<Money> montos, string currency)
        => montos.Aggregate(Zero(currency), (acc, m) => acc + m);

    public int CompareTo(Money other)
    {
        MismaMoneda(this, other);
        return Amount.CompareTo(other.Amount);
    }

    public static bool operator <(Money a, Money b) => a.CompareTo(b) < 0;
    public static bool operator >(Money a, Money b) => a.CompareTo(b) > 0;
    public static bool operator <=(Money a, Money b) => a.CompareTo(b) <= 0;
    public static bool operator >=(Money a, Money b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Amount.ToString("0.##", CultureInfo.InvariantCulture)} {Currency}";

    /// <summary>Exige que las dos monedas coincidan.</summary>
    /// <exception cref="InvalidOperationException">Si difieren.</exception>
    private static void MismaMoneda(Money a, Money b)
    {
        if (!string.Equals(a.Currency, b.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"No se pueden combinar {a.Currency} y {b.Currency}: hace falta una tasa, " +
                "y adivinarla en silencio es peor que fallar.");
        }
    }
}
