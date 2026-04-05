using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Traydio.Common;
using Xunit;

namespace Traydio.SourceGenerator.Tests;

public class ViewLocatorSourceGeneratorTests
{
    private const string _MAPPING_SOURCE = @"
using Traydio.Common;

[assembly: GenerateViewLocator(""TestNamespace"", ""ViewLocator"")]

namespace TestNamespace;

[ViewFor(typeof(MainViewModel))]
public partial class MainView
{
    public MainView()
    {
    }
}

[ViewModelFor(typeof(MainView))]
public partial class MainViewModel
{
}";

    private const string _NON_PARTIAL_SOURCE = @"
using Traydio.Common;

namespace TestNamespace;

[ViewFor(typeof(MainViewModel))]
public class MainView
{
    public MainView()
    {
    }
}

public partial class MainViewModel
{
}";

    private const string _MISSING_DI_SOURCE = @"
using Traydio.Common;

namespace TestNamespace;

[ViewFor(typeof(MainViewModel))]
public partial class MainView
{
    public MainView(IDependency dependency)
    {
    }
}

public interface IDependency
{
}

public partial class MainViewModel
{
}";

    private const string _WITH_DI_SOURCE = @"
using Traydio.Common;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ActivatorUtilities
    {
        public static object CreateInstance(System.IServiceProvider provider, System.Type type)
        {
            return null!;
        }
    }
}

namespace TestNamespace;

[ViewFor(typeof(MainViewModel))]
public partial class MainView
{
    public MainView(IDependency dependency)
    {
    }
}

public interface IDependency
{
}

public partial class MainViewModel
{
}";

    [Fact]
    public void GeneratesLocatorAndLinkedMethods()
    {
        var generator = new ViewLocatorSourceGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(generator);
        var compilation = CreateCompilation(_MAPPING_SOURCE);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        var runResult = driver.GetRunResult();

        Assert.Empty(runResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(runResult.GeneratedTrees, t => t.FilePath.EndsWith("TestNamespace_MainView.ViewModel.g.cs"));
        Assert.Contains(runResult.GeneratedTrees, t => t.FilePath.EndsWith("TestNamespace_MainViewModel.View.g.cs"));
        var locatorTree = runResult.GeneratedTrees.Single(t => t.FilePath.EndsWith("TestNamespace.ViewLocator.g.cs"));
        var locatorText = locatorTree.GetText().ToString();

        Assert.Contains("#nullable enable", locatorText);
        Assert.Contains("BuildDateUtcIso8601", locatorText);
        Assert.Contains("BuildDateUtc =>", locatorText);
        Assert.Contains("BuildYear =>", locatorText);
        Assert.DoesNotContain(".FullName:", locatorText);

        var compilationDiagnostics = updatedCompilation.GetDiagnostics();
        Assert.DoesNotContain(compilationDiagnostics, d => d.Id == "CS0117");
        Assert.DoesNotContain(compilationDiagnostics, d => d.Id == "CS8669");
    }

    [Fact]
    public void ReportsNonPartialTarget()
    {
        var generator = new ViewLocatorSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        var compilation = CreateCompilation(_NON_PARTIAL_SOURCE);

        var runResult = driver.RunGenerators(compilation).GetRunResult();
        Assert.Contains(runResult.Diagnostics, d => d.Id == "TRAYDIOSG001");
    }

    [Fact]
    public void ReportsMissingParameterlessCtorWithoutDi()
    {
        var generator = new ViewLocatorSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        var compilation = CreateCompilation(_MISSING_DI_SOURCE);

        var runResult = driver.RunGenerators(compilation).GetRunResult();
        Assert.Contains(runResult.Diagnostics, d => d.Id == "TRAYDIOSG003");
    }

    [Fact]
    public void AllowsParameterizedCtorWhenDiAvailable()
    {
        var generator = new ViewLocatorSourceGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);
        var compilation = CreateCompilation(_WITH_DI_SOURCE);

        var runResult = driver.RunGenerators(compilation).GetRunResult();
        Assert.DoesNotContain(runResult.Diagnostics, d => d.Id == "TRAYDIOSG003");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Attribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ViewForAttribute).Assembly.Location),
        };

        AddAssemblyReference(references, "System.Runtime");

        return CSharpCompilation.Create(
            assemblyName: nameof(ViewLocatorSourceGeneratorTests),
            syntaxTrees: [CSharpSyntaxTree.ParseText(source)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static void AddAssemblyReference(List<MetadataReference> references, string assemblyName)
    {
        var assembly = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal));

        if (assembly is null)
        {
            try
            {
                assembly = Assembly.Load(assemblyName);
            }
            catch
            {
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(assembly.Location))
        {
            references.Add(MetadataReference.CreateFromFile(assembly.Location));
        }
    }
}
