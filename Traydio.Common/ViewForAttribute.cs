namespace Traydio.Common;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ViewForAttribute(Type viewModelType) : Attribute
{
    public Type ViewModelType { get; } = viewModelType;
}