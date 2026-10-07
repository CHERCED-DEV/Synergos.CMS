using System.Text;

namespace Synergos.CMS.Tests.Contratos;

/// <summary>
/// El openapi.json comiteado de cada pieza es el que sale de su código (ADR 0140, F2): el patrón
/// de la ADR 0135 —fuente → contrato comiteado → tipo generado— llevado a las costuras HTTP.
/// </summary>
/// <remarks>
/// <para><b>Qué cierra.</b> Un record de contrato cambiado, un endpoint que deja de tipar su
/// respuesta, un esquema que sale de otra forma: el documento que lee el UI (y el gate de
/// compatibilidad de los orquestadores) se quedaría describiendo el cable viejo. Esto lo regenera
/// desde el host real y lo compara byte a byte.</para>
///
/// <para><b>Lo que NO cierra, dicho para no mentir sobre su alcance.</b> Un documento empobrecido
/// pasa en verde si alguien lo regenera: la deriva compara contra sí misma. Lo que un
/// <c>SYNERGOS_ACTUALIZAR_CONTRATOS=1</c> no salva lo vigilan el suelo del documento y las sondas
/// contra el host.</para>
///
/// <para>Para regenerar:
/// <c>SYNERGOS_ACTUALIZAR_CONTRATOS=1 dotnet test backend/Synergos.Servicios.Tests --filter ContratoOpenApi</c>.</para>
/// </remarks>
public sealed class ContratoOpenApiTests
{
    private const string VariableParaActualizar = "SYNERGOS_ACTUALIZAR_CONTRATOS";

    public static TheoryData<string> Piezas()
    {
        var datos = new TheoryData<string>();
        foreach (var p in ContratoOpenApi.Piezas) datos.Add(p.Ensamblado);
        return datos;
    }

    [Theory]
    [MemberData(nameof(Piezas))]
    public async Task El_documento_comiteado_es_el_que_sale_del_codigo(string ensamblado)
    {
        var esperado = await ContratoOpenApi.Pieza(ensamblado).Generar();
        var ruta = ContratoOpenApi.Ruta(ensamblado);

        if (Environment.GetEnvironmentVariable(VariableParaActualizar) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            await File.WriteAllTextAsync(ruta, esperado, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        Assert.True(File.Exists(ruta),
            $"No existe {ruta}. Generalo con {VariableParaActualizar}=1 dotnet test " +
            "backend/Synergos.Servicios.Tests --filter ContratoOpenApi.");

        var enDisco = await File.ReadAllTextAsync(ruta);
        if (string.Equals(enDisco, esperado, StringComparison.Ordinal)) return;

        var a = esperado.Split('\n');
        var b = enDisco.Split('\n');
        var i = Enumerable.Range(0, Math.Max(a.Length, b.Length))
            .First(n => n >= a.Length || n >= b.Length || !string.Equals(a[n], b[n], StringComparison.Ordinal));

        // La versión del runtime va en el mensaje porque es lo único que puede mover el documento sin
        // tocar código: el esquema lo produce System.Text.Json del framework compartido, y el CI
        // instala el último parche de 10.0.x.
        Assert.Fail(
            $"docs/contracts/openapi/{ensamblado}.json no es el que sale del código: el contrato que " +
            "leen el UI y el gate de compatibilidad describe otro cable." + Environment.NewLine +
            $"Primera diferencia, línea {i + 1}:" + Environment.NewLine +
            $"  esperado: {(i < a.Length ? a[i] : "(fin)")}" + Environment.NewLine +
            $"  en disco: {(i < b.Length ? b[i] : "(fin)")}" + Environment.NewLine +
            $"Runtime: .NET {Environment.Version}. Si el cambio es el que querías, regeneralo con " +
            $"{VariableParaActualizar}=1 dotnet test backend/Synergos.Servicios.Tests --filter ContratoOpenApi " +
            "(y en el UI, el tipo que se genera de él). Si no tocaste código, mirá si cambió el parche del runtime.");
    }

    /// <summary>
    /// En <c>openapi/</c> hay un documento por pieza que publica, y ninguno más.
    /// </summary>
    /// <remarks>
    /// Un documento que ya nadie regenera es el peor contrato: el UI seguiría generando tipos de él
    /// y el gate de deriva no lo mira, porque sólo recorre las piezas de la lista.
    /// </remarks>
    [Fact]
    public void La_carpeta_openapi_tiene_un_documento_por_pieza_y_ninguno_mas()
    {
        var carpeta = Path.GetDirectoryName(ContratoOpenApi.Ruta(ContratoOpenApi.Piezas[0].Ensamblado))!;
        var enDisco = Directory.Exists(carpeta)
            ? Directory.EnumerateFiles(carpeta).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToList()
            : [];
        var esperados = ContratoOpenApi.Piezas.Select(p => p.Ensamblado + ".json").Order(StringComparer.Ordinal).ToList();

        Assert.True(esperados.Count >= 4, $"La lista de piezas tiene {esperados.Count}: el descubrimiento está roto.");
        Assert.True(enDisco.SequenceEqual(esperados, StringComparer.Ordinal),
            $"docs/contracts/openapi/ tiene [{string.Join(", ", enDisco)}] y las piezas que publican son " +
            $"[{string.Join(", ", esperados)}]. Un documento sin pieza no lo regenera nadie; una pieza sin " +
            $"documento se genera con {VariableParaActualizar}=1.");
    }
}
