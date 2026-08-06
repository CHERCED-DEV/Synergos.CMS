using System.Runtime.CompilerServices;

// Expose internal helpers to the xUnit test assembly so the test suite
// can exercise utility methods (e.g. DefaultSynHostEmitter.ToKebabCase)
// without making them public API of the Application layer.
[assembly: InternalsVisibleTo("Synergos.CMS.Tests")]
