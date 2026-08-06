using Synergos.Shared;

namespace Synergos.CMS.Tests.Shared;

/// <summary>
/// Cubre <see cref="QueryWindow"/> — la normalización de <c>from</c>/<c>to</c>/<c>limit</c>.
/// </summary>
public sealed class QueryWindowTests
{
    private static readonly DateTime Now = new(2026, 3, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Sin_from_la_ventana_arranca_siete_dias_atras()
    {
        Assert.Equal(new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc), QueryWindow.From(null, Now));
    }

    [Fact]
    public void Sin_to_la_ventana_termina_ahora()
    {
        Assert.Equal(Now, QueryWindow.To(null, Now));
    }

    [Fact]
    public void Una_hora_local_se_convierte_a_UTC_y_no_se_toma_cruda()
    {
        // Si no se convirtiera, la ventana se correría tantas horas como el huso del
        // llamador y el tablero mostraría un rango distinto del que se pidió, sin decirlo.
        var local = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.FromHours(-5)).LocalDateTime;
        var utc = QueryWindow.From(local, Now);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 3, 1, 14, 0, 0, DateTimeKind.Utc), utc.ToUniversalTime());
    }

    [Theory]
    [InlineData(null, QueryWindow.DefaultLimit)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(42, 42)]
    [InlineData(10_000, QueryWindow.MaxLimit)]
    public void El_limite_se_acota_siempre(int? pedido, int esperado)
    {
        // El tope duro no es cortesía: sin él, una petición con limit=10000000 es una
        // descarga del almacén entero — denegación de servicio con formulario.
        Assert.Equal(esperado, QueryWindow.Limit(pedido));
    }
}
