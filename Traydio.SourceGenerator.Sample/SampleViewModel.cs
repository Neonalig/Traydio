using Traydio.Common;

namespace Traydio.SourceGenerator.Sample;

[ViewModelFor(typeof(SampleView))]
public partial class SampleViewModel
{
    public string Message { get; } = "Hello";
}