using System.Reflection;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Guardrails that enforce the layer boundaries of ADR 0002 and the
/// extension-seam discipline of ADR 0009.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly ApplicationAssembly =
        typeof(DefaultBrandingProvider).Assembly;

    private static readonly Assembly InterfacesAssembly =
        typeof(ISynergosServiceBuilder).Assembly;

    [Fact]
    public void Application_DoesNotReferenceUmbracoCms()
    {
        var leaks = ApplicationAssembly
            .GetReferencedAssemblies()
            .Where(a => (a.Name ?? string.Empty)
                .StartsWith("Umbraco", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Name)
            .ToList();

        Assert.True(
            leaks.Count == 0,
            $"Synergos.CMS.Application must not reference Umbraco.Cms.* " +
            $"(ADR 0002). Found: {string.Join(", ", leaks)}.");
    }

    [Fact]
    public void Application_DoesNotReferenceAspNetCore()
    {
        var leaks = ApplicationAssembly
            .GetReferencedAssemblies()
            .Where(a => (a.Name ?? string.Empty)
                .StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Name)
            .ToList();

        Assert.True(
            leaks.Count == 0,
            $"Synergos.CMS.Application must not reference Microsoft.AspNetCore.* " +
            $"(ADR 0002). Found: {string.Join(", ", leaks)}.");
    }

    [Fact]
    public void Interfaces_DoesNotReferenceUmbracoCms()
    {
        var leaks = InterfacesAssembly
            .GetReferencedAssemblies()
            .Where(a => (a.Name ?? string.Empty)
                .StartsWith("Umbraco", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Name)
            .ToList();

        Assert.True(
            leaks.Count == 0,
            $"Synergos.CMS.Interfaces must not reference Umbraco.Cms.* " +
            $"(ADR 0002). Found: {string.Join(", ", leaks)}.");
    }

    [Fact]
    public void Interfaces_HasNoInternalProjectReferences()
    {
        var internalRefs = InterfacesAssembly
            .GetReferencedAssemblies()
            .Where(a => (a.Name ?? string.Empty)
                .StartsWith("Synergos.", StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Name)
            .ToList();

        Assert.True(
            internalRefs.Count == 0,
            $"Synergos.CMS.Interfaces must not reference other Synergos projects " +
            $"(ADR 0002). Found: {string.Join(", ", internalRefs)}.");
    }
}
