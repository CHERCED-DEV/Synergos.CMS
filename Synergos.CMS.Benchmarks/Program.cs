using BenchmarkDotNet.Running;

namespace Synergos.CMS.Benchmarks;

/// <summary>
/// Entry point del BenchmarkDotNet harness (Cap-260 Batch D, Ola 259).
/// Run: <c>dotnet run -c Release -- --filter '*'</c> desde
/// <c>Synergos.CMS.Benchmarks/</c>. Pasa filter por glob para
/// ejecutar subset (e.g. <c>--filter '*WebhookSigner*'</c>).
/// </summary>
public static class Program
{
    public static void Main(string[] args)
    {
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}
