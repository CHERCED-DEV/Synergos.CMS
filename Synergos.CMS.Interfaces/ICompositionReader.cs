namespace Synergos.CMS.Interfaces;

/// <summary>
/// Reads a typed composition model from an input source (typically an
/// Umbraco published element).
/// </summary>
/// <typeparam name="TInput">Source type. Specialised at registration time; the seam itself stays neutral.</typeparam>
/// <typeparam name="TOutput">Typed composition model.</typeparam>
/// <remarks>
/// Extension seam per ADR 0009 (Extension seams are mandatory). This
/// contract replaces the 27 copy-paste readers of the legacy project
/// (<c>_archive/fails/Synergos.CMS.epicfail2/Application/Mapping/Compositions/</c>).
/// In the new architecture most compositions resolve via a factory
/// (<c>CompositionResolver</c>) using convention; explicit readers are
/// created only when a composition needs non-declarative logic. See
/// Ola 5 of the migration plan.
/// </remarks>
public interface ICompositionReader<in TInput, out TOutput>
{
    /// <summary>Reads the typed composition model from the source.</summary>
    TOutput Read(TInput source);
}
