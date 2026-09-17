using System.Runtime.CompilerServices;

// Expose internal helpers to the xUnit test assembly so the test suite
// can exercise utility methods (e.g. DefaultSynHostEmitter.ToKebabCase)
// without making them public API of the Application layer.
[assembly: InternalsVisibleTo("Synergos.CMS.Tests")]
// Y a los gates, que desde el #135 viven en su propio ensamblado: son los que
// comprueban el CABLEADO del composer, y eso es implementación, no superficie.
[assembly: InternalsVisibleTo("Synergos.Arquitectura.Tests")]
