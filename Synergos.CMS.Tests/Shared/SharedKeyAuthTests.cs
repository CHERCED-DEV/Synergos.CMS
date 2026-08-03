using Microsoft.AspNetCore.Http;
using Synergos.CMS.Web.Services;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Shared;

/// <summary>
/// Cubre <see cref="SharedKeyAuth"/> — el borde de llave compartida de las APIs internas.
/// </summary>
/// <remarks>
/// Este código venía de <c>Synergos.Sessions</c>, donde funcionaba pero <b>no estaba
/// probado</b>. Al moverlo a <c>Synergos.Shared</c> pasa a servir a todas las APIs, y una
/// decisión sutil rota aquí se rompe en todas a la vez. Es la parte del trato: lo que se
/// comparte se prueba.
/// </remarks>
public sealed class SharedKeyAuthTests
{
    [Fact]
    public void La_comparacion_acepta_la_llave_correcta()
    {
        Assert.True(SharedKeyAuth.FixedTimeEquals("s3cr3t", "s3cr3t"));
    }

    [Theory]
    [InlineData("s3cr3", "s3cr3t")]      // prefijo correcto, más corta
    [InlineData("s3cr3t!", "s3cr3t")]    // prefijo correcto, más larga
    [InlineData("S3CR3T", "s3cr3t")]     // solo cambia el caso
    [InlineData("", "s3cr3t")]
    [InlineData(null, "s3cr3t")]
    public void La_comparacion_rechaza_todo_lo_demas(string? sent, string expected)
    {
        // El caso del prefijo es el que importa: es exactamente lo que una comparación
        // normal filtra por tiempo de respuesta, y como se adivina una llave sin saberla.
        Assert.False(SharedKeyAuth.FixedTimeEquals(sent, expected));
    }

    [Fact]
    public void Una_llave_vacia_NO_valida_contra_una_llave_vacia()
    {
        // Sin esto, un servicio sin llave configurada aceptaría peticiones sin cabecera
        // como si estuvieran autenticadas — "abierto" disfrazado de "validado". El caso
        // de la llave ausente se resuelve arriba, no aquí.
        Assert.False(SharedKeyAuth.FixedTimeEquals(null, null));
        Assert.False(SharedKeyAuth.FixedTimeEquals("", ""));
    }

    [Theory]
    [InlineData("/health", true)]
    [InlineData("/health/ready", true)]
    [InlineData("/v1/search/top", false)]
    [InlineData("/healthcheck", false)]   // segmento distinto, no es /health
    public void Solo_health_queda_exento(string path, bool exento)
    {
        // Un chequeo de vida que exige credenciales no sirve de chequeo de vida. Pero la
        // exención tiene que ser por SEGMENTO: si fuera por prefijo de texto, cualquier
        // ruta que empiece por "health…" entraría sin llave.
        Assert.Equal(exento, SharedKeyAuth.IsAlwaysOpen(new PathString(path)));
    }

    [Fact]
    public void La_cabecera_es_una_sola_para_todos_los_servicios()
    {
        // El adapter del CMS declara la suya por separado (no puede referenciar esto). Si
        // las dos se separan, la ingesta responde 401 y parece un problema de red.
        Assert.Equal("X-Synergos-Key", SharedKeyAuth.HeaderName);
        Assert.Equal(SharedKeyAuth.HeaderName, HttpSearchAnalyticsStore.ApiKeyHeader);
    }
}
