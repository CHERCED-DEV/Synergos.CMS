using Synergos.CMS.Application.Components;

namespace Synergos.CMS.Application.Output;

/// <summary>
/// Implementación de ISection que envuelve cualquier BaseComponentViewModel.
///
/// Permite que SectionMapperDispatcher y PageAssembler trabajen con el
/// contrato ISection sin depender de tipos de componente concretos.
/// </summary>
public sealed class ComponentSection : Domain.Sections.ISection
{
    public BaseComponentViewModel ViewModel { get; }

    public string ViewName   => ViewModel.ViewName;
    public string BlockClass => ViewModel.BlockClass;

    public ComponentSection(BaseComponentViewModel viewModel)
        => ViewModel = viewModel;
}
