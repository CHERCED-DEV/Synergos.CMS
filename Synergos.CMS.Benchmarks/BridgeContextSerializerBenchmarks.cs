using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Benchmarks;

/// <summary>
/// Benchmarks para serialización del <see cref="HostBridgeContext"/>
/// que el partial <c>_SynergosBridge.cshtml</c> emite per-request +
/// que el endpoint <c>/synergos-bridge.js</c> sirve en CSP-strict mode.
/// Cap-260 Batch D (Ola 259). El payload se serializa por cada page
/// load — el costo importa.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class BridgeContextSerializerBenchmarks
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private HostBridgeContext _smallContext = null!;   // anónimo, pocas keys
    private HostBridgeContext _typicalContext = null!; // member auth + 50 keys i18n
    private HostBridgeContext _largeContext = null!;   // 369 keys i18n + member full

    [GlobalSetup]
    public void Setup()
    {
        _smallContext = new HostBridgeContext(
            Version: "1.0.0",
            I18n: new HostBridgeI18n("es-CO", "es-CO",
                new Dictionary<string, string> { ["Form.Submit"] = "Enviar" }),
            Theme: new HostBridgeTheme("light", new[] { "light", "dark" }),
            Brand: new HostBridgeBrand("default", "Synergos"),
            Member: null,
            Page: new HostBridgePage(0, "", "", Array.Empty<string>()));

        _typicalContext = new HostBridgeContext(
            Version: "1.0.0",
            I18n: BuildI18n(50),
            Theme: new HostBridgeTheme("light", new[] { "light", "dark", "silvergold" }),
            Brand: new HostBridgeBrand("acme", "Acme Corp"),
            Member: new HostBridgeMember("abcd1234", "Alice Smith", "alice@example.com",
                new[] { "member", "premium" }),
            Page: new HostBridgePage(1042, "pageStandard",
                "https://acme.example.com/post/welcome", new[] { "es-CO", "en-US" }));

        _largeContext = _typicalContext with { I18n = BuildI18n(369) };
    }

    private static HostBridgeI18n BuildI18n(int keyCount)
    {
        var keys = new Dictionary<string, string>(keyCount);
        for (var i = 0; i < keyCount; i++)
        {
            keys[$"Section{i / 10}.Key{i}"] = $"Translated value number {i} con tildes á é í ó ú ñ";
        }
        return new HostBridgeI18n("es-CO", "es-CO", keys);
    }

    [Benchmark(Baseline = true)]
    public string Serialize_SmallAnon_FewKeys() =>
        JsonSerializer.Serialize(_smallContext, Options);

    [Benchmark]
    public string Serialize_TypicalAuthMember_50Keys() =>
        JsonSerializer.Serialize(_typicalContext, Options);

    [Benchmark]
    public string Serialize_FullCatalog_369Keys() =>
        JsonSerializer.Serialize(_largeContext, Options);

    [Benchmark]
    public byte[] SerializeUtf8_FullCatalog_369Keys() =>
        JsonSerializer.SerializeToUtf8Bytes(_largeContext, Options);
}
