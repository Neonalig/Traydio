using Traydio.Common;

namespace Traydio.SourceGenerator.Sample;

[ViewFor(typeof(SampleViewModel))]
public partial class SampleView(IGreetingService greetingService)
{
    public string Greeting { get; } = greetingService.Greeting;
}