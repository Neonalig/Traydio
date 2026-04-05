namespace Traydio.Common;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class GenerateViewLocatorAttribute(string ns, string className) : Attribute
{
    public string Namespace { get; } = ns;

    public string ClassName { get; } = className;

    public string? FullyQualifiedName { get; init; }

    public bool EnableDependencyInjection { get; init; } = true;

    public string ServiceProviderPropertyName { get; init; } = "ServiceProvider";
}