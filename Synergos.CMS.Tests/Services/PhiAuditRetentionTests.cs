using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Synergos.CMS.Web.Services.Retention;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre la separación de la auditoría de acceso a PHI de la administrativa (ADR 0121).
/// </summary>
/// <remarks>
/// <para>El defecto era de dos caras y ninguna fallaba: el XML-doc de
/// <see cref="HealthcareRetentionPolicy"/> afirmaba que la auditoría de acceso a PHI tenía
/// "retención indefinida por obligación legal" y que "la gestiona otra policy" — pero los
/// eventos <c>phi.*</c> caían en el mismo <c>{fecha}.jsonl</c> que todo lo demás, así que la
/// policy que de verdad los gestionaba los <b>borraba a los 90 días</b>. La documentación
/// prometía una cosa y el código hacía la contraria, en silencio y de forma irreversible.</para>
///
/// <para>Lo que estas pruebas fijan: que las dos familias de archivo existan, que cada purga
/// respete la ajena, y que el visor de administración siga viendo las dos.</para>
/// </remarks>
public sealed class PhiAuditRetentionTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _auditDir;

    public PhiAuditRetentionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-phi-audit-ret-" + Guid.NewGuid().ToString("N"));
        _auditDir = Path.Combine(_tempRoot, "App_Data", "syn-audit");
        Directory.CreateDirectory(_auditDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    // ── El escritor separa las dos familias ──────────────────────────────────

    [Theory]
    [InlineData("phi.access-granted")]
    [InlineData("phi.access-denied")]
    [InlineData("PHI.ACCESS-GRANTED")]
    public void Un_evento_de_acceso_a_PHI_va_a_su_propio_archivo(string action)
    {
        var name = FileSystemAuditTrailWriter.ResolveFileName(
            new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc), action);

        Assert.Equal("2026-08-01.phi.jsonl", name);
    }

    [Theory]
    [InlineData("member.login")]
    [InlineData("shop.return-advanced")]
    [InlineData("")]
    [InlineData(null)]
    public void Un_evento_administrativo_sigue_en_el_archivo_de_siempre(string? action)
    {
        var name = FileSystemAuditTrailWriter.ResolveFileName(
            new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc), action);

        Assert.Equal("2026-08-01.jsonl", name);
    }

    [Fact]
    public void Las_dos_familias_casan_con_el_patron_que_lee_el_visor()
    {
        // Separar el archivo cambia qué se PURGA, no qué se ve: el visor de administración
        // lista *.jsonl y tiene que seguir mostrando los dos.
        var phi = FileSystemAuditTrailWriter.ResolveFileName(DateTime.UtcNow, "phi.access-granted");
        var admin = FileSystemAuditTrailWriter.ResolveFileName(DateTime.UtcNow, "member.login");

        Assert.EndsWith(".jsonl", phi, StringComparison.Ordinal);
        Assert.EndsWith(".jsonl", admin, StringComparison.Ordinal);
    }

    // ── Cada purga respeta la ajena ──────────────────────────────────────────

    private string WriteAuditFile(string fileName)
    {
        var path = Path.Combine(_auditDir, fileName);
        File.WriteAllText(path, "{\"Id\":\"x\"}\n");
        return path;
    }

    private AuditRetentionPolicy BuildAuditPolicy(int retentionDays)
    {
        var monitor = Substitute.For<IOptionsMonitor<AdminSettings>>();
        monitor.CurrentValue.Returns(new AdminSettings { AuditRetentionDays = retentionDays });
        return new AuditRetentionPolicy(
            new StubHostEnvironment(_tempRoot), monitor, NullLogger<AuditRetentionPolicy>.Instance);
    }

    private HealthcareRetentionPolicy BuildHealthcarePolicy(int auditRetentionDays)
    {
        var monitor = Substitute.For<IOptionsMonitor<HealthcareSettings>>();
        monitor.CurrentValue.Returns(new HealthcareSettings
        {
            RecordRetentionDays = 0,
            AccessAuditRetentionDays = auditRetentionDays,
        });
        return new HealthcareRetentionPolicy(
            new StubHostEnvironment(_tempRoot), monitor, NullLogger<HealthcareRetentionPolicy>.Instance);
    }

    private static string OldDay(int daysAgo) => DateTime.UtcNow.Date.AddDays(-daysAgo).ToString("yyyy-MM-dd");

    [Fact]
    public async Task La_purga_administrativa_NO_toca_la_auditoria_clinica()
    {
        // Este es el defecto. Con 90 días de retención administrativa, un archivo clínico de
        // hace un año se borraba con los demás.
        var phi = WriteAuditFile(OldDay(365) + ".phi.jsonl");
        var admin = WriteAuditFile(OldDay(365) + ".jsonl");

        await BuildAuditPolicy(90).SweepAsync(CancellationToken.None);

        Assert.True(File.Exists(phi), "la auditoría de acceso a PHI no puede purgarse con la administrativa");
        Assert.False(File.Exists(admin));
    }

    [Fact]
    public async Task Por_defecto_la_auditoria_clinica_NO_se_purga_nunca()
    {
        // Default 0 = nunca. Borrar el registro de quién miró una historia clínica es
        // irreversible, y el sistema no elige ese número por el operador.
        var phi = WriteAuditFile(OldDay(3650) + ".phi.jsonl");

        await BuildHealthcarePolicy(new HealthcareSettings().AccessAuditRetentionDays)
            .SweepAsync(CancellationToken.None);

        Assert.True(File.Exists(phi));
    }

    [Fact]
    public async Task Con_una_retencion_declarada_la_auditoria_clinica_SI_se_purga()
    {
        var viejo = WriteAuditFile(OldDay(400) + ".phi.jsonl");
        var reciente = WriteAuditFile(OldDay(10) + ".phi.jsonl");

        var purged = await BuildHealthcarePolicy(365).SweepAsync(CancellationToken.None);

        Assert.Equal(1, purged);
        Assert.False(File.Exists(viejo));
        Assert.True(File.Exists(reciente));
    }

    [Fact]
    public async Task La_purga_clinica_NO_toca_la_administrativa()
    {
        var admin = WriteAuditFile(OldDay(400) + ".jsonl");

        await BuildHealthcarePolicy(365).SweepAsync(CancellationToken.None);

        Assert.True(File.Exists(admin));
    }

    [Fact]
    public async Task La_fecha_sale_del_NOMBRE_del_archivo_y_no_de_su_ultima_escritura()
    {
        // Un archivo del día se reescribe cada vez que alguien entra a un expediente. Con la
        // marca del sistema de archivos, la retención se reiniciaría en cada acceso y el
        // registro más consultado sería el último en borrarse — justo al revés.
        var viejo = WriteAuditFile(OldDay(400) + ".phi.jsonl");
        File.SetLastWriteTimeUtc(viejo, DateTime.UtcNow);

        await BuildHealthcarePolicy(365).SweepAsync(CancellationToken.None);

        Assert.False(File.Exists(viejo));
    }

    [Fact]
    public async Task Un_nombre_que_no_es_una_fecha_se_ignora_en_vez_de_borrarse()
    {
        var raro = WriteAuditFile("backup.phi.jsonl");

        await BuildHealthcarePolicy(1).SweepAsync(CancellationToken.None);

        Assert.True(File.Exists(raro));
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string contentRootPath) => ContentRootPath = contentRootPath;
        public string ApplicationName { get; set; } = "Synergos.CMS.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Tests";
    }
}
