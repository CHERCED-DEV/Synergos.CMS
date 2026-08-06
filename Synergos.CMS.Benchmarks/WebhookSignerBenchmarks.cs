using System.Text;
using BenchmarkDotNet.Attributes;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Benchmarks;

/// <summary>
/// Benchmarks para <see cref="WebhookSigner.ComputeSignedHeaders"/>.
/// Cap-260 Batch D (Ola 259). El signing corre en el path de cada
/// outbound notifier que tenga HMAC secret configurado (3 webhook
/// notifiers + alert webhook = 4 sites). Establecer baseline para
/// detectar regresiones futuras (e.g. switch a SHA512 o más rounds).
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class WebhookSignerBenchmarks
{
    private const string Secret = "shared-secret-for-bench-only-not-prod";

    private byte[] _smallBody = null!;   // ~200 bytes — typical alert payload
    private byte[] _mediumBody = null!;  // ~2 KB — comment moderation con body largo
    private byte[] _largeBody = null!;   // ~20 KB — form submission con muchos fields

    [GlobalSetup]
    public void Setup()
    {
        _smallBody = Encoding.UTF8.GetBytes(
            """{"event":"alert.fired","channelName":"comment-teams","failRate":0.42,"totalCalls":1000}""");

        var medium = new StringBuilder();
        medium.Append("""{"event":"comment.pending","comment":{"id":"abc","body":""");
        medium.Append('x', 1900);
        medium.Append("\"}}");
        _mediumBody = Encoding.UTF8.GetBytes(medium.ToString());

        var large = new StringBuilder();
        large.Append("""{"event":"form.submitted","fields":{""");
        for (var i = 0; i < 80; i++)
        {
            if (i > 0) large.Append(',');
            large.Append($"\"field_{i}\":\"value_{i}_padding_{new string('y', 200)}\"");
        }
        large.Append("}}");
        _largeBody = Encoding.UTF8.GetBytes(large.ToString());
    }

    [Benchmark(Baseline = true)]
    public (string, string)? Sign_Small_200B() =>
        WebhookSigner.ComputeSignedHeaders(Secret, _smallBody);

    [Benchmark]
    public (string, string)? Sign_Medium_2KB() =>
        WebhookSigner.ComputeSignedHeaders(Secret, _mediumBody);

    [Benchmark]
    public (string, string)? Sign_Large_20KB() =>
        WebhookSigner.ComputeSignedHeaders(Secret, _largeBody);

    [Benchmark]
    public (string, string)? Sign_NullSecret_NoOp() =>
        WebhookSigner.ComputeSignedHeaders(null, _smallBody);
}
