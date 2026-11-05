using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Retention;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="HealthcareRetentionPolicy"/> (ADR 0098 H3, enmendada por la ADR 0142). Los documentos
/// clínicos NO se purgan: pasado el término legal (15 años como mínimo) son candidatos, y su eliminación es el trámite
/// del art. 4 de la Resolución 839 de 2017. La prueba que decía «purga lo viejo» cambió con la decisión D-003-I2-5.
/// </summary>
public sealed class HealthcareRetentionPolicyTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _phiDir;

    public HealthcareRetentionPolicyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-hc-ret-test-" + Guid.NewGuid().ToString("N"));
        _phiDir = Path.Combine(_tempRoot, "App_Data", "syn-healthcare", "patients");
        Directory.CreateDirectory(_phiDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private HealthcareRetentionPolicy BuildSut(int retentionDays)
    {
        var monitor = Substitute.For<IOptionsMonitor<HealthcareSettings>>();
        monitor.CurrentValue.Returns(new HealthcareSettings { RecordRetentionDays = retentionDays });
        return new HealthcareRetentionPolicy(
            new StubHostEnvironment(_tempRoot), monitor, NullLogger<HealthcareRetentionPolicy>.Instance);
    }

    private string WritePhi(string name, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_phiDir, name + ".phi");
        File.WriteAllText(path, "cipher");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    [Fact]
    public void Name_IsHealthcare() => Assert.Equal("healthcare", BuildSut(2190).Name);

    [Fact] // ADR 0142: ninguna historia se borra sola, ni pasado el término.
    public async Task Sweep_NoBorraNingunDocumentoClinico_AunquePaseElTermino()
    {
        var viejisimo = WritePhi("viejisimo", DateTime.UtcNow.AddDays(-7000)); // > 15 años
        var viejo = WritePhi("viejo", DateTime.UtcNow.AddDays(-3000));         // > 6 años, < 15
        var reciente = WritePhi("reciente", DateTime.UtcNow.AddDays(-10));

        var purged = await BuildSut(retentionDays: 2190).SweepAsync(CancellationToken.None);

        Assert.Equal(0, purged);
        Assert.True(File.Exists(viejisimo));
        Assert.True(File.Exists(viejo));
        Assert.True(File.Exists(reciente));
    }

    [Theory] // el término se puede subir, nunca bajar: lo configurado por debajo de 15 años cuenta como 15.
    [InlineData(2190)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Candidatos_ElTerminoNuncaEsMenorQueElLegal(int configurado)
    {
        WritePhi("viejisimo", DateTime.UtcNow.AddDays(-7000));
        WritePhi("viejo", DateTime.UtcNow.AddDays(-3000));
        WritePhi("reciente", DateTime.UtcNow.AddDays(-10));

        Assert.Equal(1, BuildSut(configurado).AvisarLosCandidatos(configurado, CancellationToken.None));
    }

    [Fact]
    public void Candidatos_UnTerminoMasLargoQueElLegal_Se_respeta()
    {
        WritePhi("viejisimo", DateTime.UtcNow.AddDays(-7000));
        Assert.Equal(0, BuildSut(8000).AvisarLosCandidatos(8000, CancellationToken.None));
    }

    [Fact]
    public void Settings_LaRetencionPorDefectoEsLaLegal_YNuncaMenor()
    {
        Assert.Equal(HealthcareSettings.RetencionMinimaDias, new HealthcareSettings().RetencionEfectivaDias);
        Assert.Equal(HealthcareSettings.RetencionMinimaDias, new HealthcareSettings { RecordRetentionDays = 2190 }.RetencionEfectivaDias);
        Assert.Equal(8000, new HealthcareSettings { RecordRetentionDays = 8000 }.RetencionEfectivaDias);
        Assert.True(HealthcareSettings.RetencionMinimaDias >= 15 * 365);
    }

    [Fact]
    public async Task Sweep_ZeroRetention_NoOp()
    {
        WritePhi("viejo", DateTime.UtcNow.AddDays(-3000));
        var purged = await BuildSut(retentionDays: 0).SweepAsync(CancellationToken.None);
        Assert.Equal(0, purged);
    }

    [Fact]
    public async Task Sweep_EmptyDir_ReturnsZero() =>
        Assert.Equal(0, await BuildSut(2190).SweepAsync(CancellationToken.None));

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string contentRootPath) => ContentRootPath = contentRootPath;
        public string ApplicationName { get; set; } = "Synergos.CMS.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Tests";
    }
}
