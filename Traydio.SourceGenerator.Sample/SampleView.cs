using Traydio.Common;

namespace Traydio.SourceGenerator.Sample;

[ViewFor(typeof(SampleViewModel))]
public partial class SampleView
{
    public SampleView(IGreetingService greetingService)
    {
        Greeting = greetingService.Greeting;
    }

    public string Greeting { get; }
}