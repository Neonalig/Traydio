namespace Traydio.Common;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ViewModelForAttribute(Type viewType) : Attribute
{
    public Type ViewType { get; } = viewType;
}