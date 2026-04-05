using System;
using Microsoft.Extensions.DependencyInjection;

namespace Traydio.SourceGenerator.Sample;

public class ViewLocatorExample
{
    public static Type[] GetLinkedTypes()
    {
        return new[]
        {
            SampleView.GetLinkedViewModelType(),
            SampleViewModel.GetLinkedViewType(),
        };
    }

    public static SampleView? CreateViewFor(SampleViewModel viewModel)
    {
        var services = new ServiceCollection()
            .AddSingleton<IGreetingService, GreetingService>()
            .BuildServiceProvider();

        var locator = new SampleViewLocator(services);
        return locator.BuildObject(viewModel) as SampleView;
    }
}
