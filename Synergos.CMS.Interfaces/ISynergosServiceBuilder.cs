namespace Synergos.CMS.Interfaces;

/// <summary>
/// Composition anchor for the Synergos.CMS solution.
/// Implementations live in Synergos.CMS.Web and wire up services
/// exposed from Synergos.CMS.Application, so the Application and Web layers
/// never need to reference each other directly through a concrete type.
/// </summary>
/// <remarks>
/// Pattern borrowed from NS.Booking.CMS.Interfaces.IIbeServiceBuilder.
/// Rationale: keep the Interfaces project as a neutral boundary that every
/// other project can reference without creating cycles (see ADR 0002).
/// </remarks>
public interface ISynergosServiceBuilder
{
}
