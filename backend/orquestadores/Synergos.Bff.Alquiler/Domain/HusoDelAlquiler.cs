using Synergos.Core;

namespace Synergos.Bff.Alquiler.Domain;

/// <summary>
/// El huso del despliegue: en qué día está «hoy» para la regla de cancelar (research R7, 005-P3). El inquilino
/// cancela si hoy, en este huso, es ANTERIOR al día de inicio; desde ese día, ya no.
/// </summary>
/// <remarks>
/// <para><b>El día de inicio es la fecha UTC de la ventana</b>: las fechas viajan a medianoche UTC —el CMS las manda
/// así y el orquestador las devuelve igual (<c>HttpEquipmentRentalService.MedianocheUtc</c>)—, y leerlas en otro
/// huso correría el día. Lo que se lee en el huso es el reloj: a las 23:30 de Bogotá todavía es el día anterior,
/// aunque en UTC ya sea el siguiente.</para>
///
/// <para><b>Un huso que no existe no arranca</b>: contar «hoy» en UTC en su lugar dejaría cancelar o no a deshora,
/// sin que nada fallara. El mismo valor por defecto que el CMS (<c>Synergos:Alquiler:TimeZone</c>).</para>
/// </remarks>
/// <param name="Zona">El huso.</param>
public sealed record HusoDelAlquiler(TimeZoneInfo Zona)
{
    /// <summary>El huso por defecto, el mismo del CMS.</summary>
    public const string PorDefecto = "America/Bogota";

    /// <summary>El huso de ese identificador IANA, o el de por defecto si viene vacío.</summary>
    /// <exception cref="InvalidOperationException">Si este sistema no conoce el huso.</exception>
    public static HusoDelAlquiler De(string? id)
    {
        var nombre = string.IsNullOrWhiteSpace(id) ? PorDefecto : id.Trim();
        try
        {
            return new HusoDelAlquiler(TimeZoneInfo.FindSystemTimeZoneById(nombre));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"Alquiler:TimeZone dice «{nombre}» y este sistema no conoce ese huso: sin él no se sabe qué día es hoy.", ex);
        }
    }

    /// <summary>Qué día es <paramref name="ahora"/> en este huso.</summary>
    public DateOnly Hoy(DateTimeOffset ahora) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(ahora, Zona).DateTime);

    /// <summary>El día de inicio de una ventana: su fecha UTC.</summary>
    public static DateOnly DiaDeInicio(TimeWindow ventana) => DateOnly.FromDateTime(ventana.Start.UtcDateTime);

    /// <summary>Si el alquiler ya empezó: hoy, en este huso, es el día de inicio o uno posterior.</summary>
    public bool YaEmpezo(TimeWindow ventana, DateTimeOffset ahora) => Hoy(ahora) >= DiaDeInicio(ventana);
}

/// <summary>Quién cancela un alquiler confirmado: la regla y la penalidad dependen de eso (S-3).</summary>
public enum QuienCancela
{
    /// <summary>Quien alquiló: antes del día de inicio y sin penalidad.</summary>
    Inquilino,

    /// <summary>El personal: siempre que no esté devuelto, con la penalidad que fije (≤ la garantía).</summary>
    Personal,
}
