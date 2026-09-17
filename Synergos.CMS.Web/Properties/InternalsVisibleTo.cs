using System.Runtime.CompilerServices;

// Expose internal helpers to the xUnit test assembly so the test suite
// can exercise invalidation and middleware logic without constructing
// Umbraco notification objects or booting the full host.
[assembly: InternalsVisibleTo("Synergos.CMS.Tests")]
// Y a los gates, que desde el #135 viven en su propio ensamblado: son los que
// comprueban el CABLEADO del composer, y eso es implementación, no superficie.
[assembly: InternalsVisibleTo("Synergos.Arquitectura.Tests")]
