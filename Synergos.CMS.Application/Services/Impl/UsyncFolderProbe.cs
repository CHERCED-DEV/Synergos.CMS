using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Health probe that verifies the uSync root folder exists and is
/// readable.
/// </summary>
/// <remarks>
/// Per ADR 0008, uSync XMLs are the source of truth for schema and
/// therefore the folder must be present and readable at boot. The
/// probe does not validate XML contents — only that the directory
/// is reachable.
/// </remarks>
public sealed class UsyncFolderProbe : ISchemaHealthProbe
{
    public const string ProbeName = "usync_folder_readable";

    private readonly string _absoluteRootPath;

    public UsyncFolderProbe(string absoluteRootPath)
    {
        if (string.IsNullOrWhiteSpace(absoluteRootPath))
        {
            throw new ArgumentException(
                "Path is required.", nameof(absoluteRootPath));
        }

        _absoluteRootPath = absoluteRootPath;
    }

    public Task<SchemaHealthResult> CheckAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_absoluteRootPath))
        {
            return Task.FromResult(new SchemaHealthResult(
                Name: ProbeName,
                IsHealthy: false,
                Message: $"uSync folder not found at '{_absoluteRootPath}'."));
        }

        try
        {
            _ = Directory.EnumerateFileSystemEntries(_absoluteRootPath).FirstOrDefault();
            return Task.FromResult(new SchemaHealthResult(
                Name: ProbeName,
                IsHealthy: true,
                Message: _absoluteRootPath));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Task.FromResult(new SchemaHealthResult(
                Name: ProbeName,
                IsHealthy: false,
                Message: $"Access denied reading '{_absoluteRootPath}': {ex.Message}"));
        }
        catch (IOException ex)
        {
            return Task.FromResult(new SchemaHealthResult(
                Name: ProbeName,
                IsHealthy: false,
                Message: $"I/O error reading '{_absoluteRootPath}': {ex.Message}"));
        }
    }
}
