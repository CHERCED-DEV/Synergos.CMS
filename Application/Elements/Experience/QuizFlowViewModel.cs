using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class QuizFlowViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/QuizFlow";
    public override string BlockClass => "sg-element--exp-quiz-flow";
    public override string Alias      => "experienceQuizFlow";

    public ContentTextModel?       Text       { get; init; }
    public ContentCollectionModel? Collection { get; init; }
}
