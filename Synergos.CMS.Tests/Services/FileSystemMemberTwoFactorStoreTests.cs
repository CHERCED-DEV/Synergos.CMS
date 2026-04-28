using Microsoft.Extensions.Hosting;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="FileSystemMemberTwoFactorStore"/>
/// (Olas 178 + 197). Verifica read/write/delete via temp directory
/// con cleanup IDisposable.
/// </summary>
public sealed class FileSystemMemberTwoFactorStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly StubHostEnvironment _env;
    private readonly FileSystemMemberTwoFactorStore _sut;

    public FileSystemMemberTwoFactorStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-2fa-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _env = new StubHostEnvironment(_tempRoot);
        _sut = new FileSystemMemberTwoFactorStore(_env);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    [Fact]
    public void Read_NonexistentMember_ReturnsNull()
    {
        var result = _sut.Read(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public void Save_AndReadBack()
    {
        var memberKey = Guid.NewGuid();
        var record = new TwoFactorRecord(
            SecretBase32: "JBSWY3DPEHPK3PXP",
            IsEnabled: true,
            RecoveryCodes: new[] { "salt1:hash1", "salt2:hash2" },
            EnrolledUtc: DateTime.UtcNow);

        _sut.Save(memberKey, record);

        var read = _sut.Read(memberKey);
        Assert.NotNull(read);
        Assert.Equal("JBSWY3DPEHPK3PXP", read!.SecretBase32);
        Assert.True(read.IsEnabled);
        Assert.Equal(2, read.RecoveryCodes.Count);
    }

    [Fact]
    public void Save_OverwritesExisting()
    {
        var memberKey = Guid.NewGuid();
        _sut.Save(memberKey, new TwoFactorRecord("OLD", false, Array.Empty<string>(), DateTime.UtcNow));
        _sut.Save(memberKey, new TwoFactorRecord("NEW", true, Array.Empty<string>(), DateTime.UtcNow));

        var read = _sut.Read(memberKey);

        Assert.Equal("NEW", read!.SecretBase32);
        Assert.True(read.IsEnabled);
    }

    [Fact]
    public void Delete_ExistingRecord_ReturnsTrue()
    {
        var memberKey = Guid.NewGuid();
        _sut.Save(memberKey, new TwoFactorRecord("X", true, Array.Empty<string>(), DateTime.UtcNow));

        var deleted = _sut.Delete(memberKey);

        Assert.True(deleted);
        Assert.Null(_sut.Read(memberKey));
    }

    [Fact]
    public void Delete_NonexistentRecord_ReturnsFalse()
    {
        var deleted = _sut.Delete(Guid.NewGuid());

        Assert.False(deleted);
    }

    [Fact]
    public void Read_CorruptedJson_ReturnsNull()
    {
        var memberKey = Guid.NewGuid();
        var path = Path.Combine(_tempRoot, "App_Data", "syn-2fa", memberKey.ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not valid json }");

        var result = _sut.Read(memberKey);

        Assert.Null(result);
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string contentRootPath) =>
            ContentRootPath = contentRootPath;

        public string ApplicationName { get; set; } = "Synergos.CMS.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Tests";
    }
}
