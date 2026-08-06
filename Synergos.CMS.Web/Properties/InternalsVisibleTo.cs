using System.Runtime.CompilerServices;

// Expose internal helpers to the xUnit test assembly so the test suite
// can exercise invalidation and middleware logic without constructing
// Umbraco notification objects or booting the full host.
[assembly: InternalsVisibleTo("Synergos.CMS.Tests")]
