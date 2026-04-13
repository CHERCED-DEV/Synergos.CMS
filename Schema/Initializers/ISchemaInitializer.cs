namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Uniform contract for all Synergos schema pipeline phases.
/// Each initializer runs exactly once per schema version bump, in explicit order.
///
/// Phase order is business-critical (later phases depend on types created by earlier ones).
/// Order is enforced by <see cref="SynergosSchemaInitializer"/> — not by DI resolution.
/// </summary>
internal interface ISchemaInitializer
{
    void Initialize();
}
