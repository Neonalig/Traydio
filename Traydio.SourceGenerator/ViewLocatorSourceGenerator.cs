using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Traydio.SourceGenerator;

[Generator]
public sealed class ViewLocatorSourceGenerator : IIncrementalGenerator
{
    private const string _VIEW_FOR_ATTRIBUTE_NAME = "Traydio.Common.ViewForAttribute";
    private const string _VIEW_MODEL_FOR_ATTRIBUTE_NAME = "Traydio.Common.ViewModelForAttribute";
    private const string _GENERATE_VIEW_LOCATOR_ATTRIBUTE_NAME = "Traydio.Common.GenerateViewLocatorAttribute";

    private static readonly DiagnosticDescriptor _nonPartialTypeDescriptor = new(
        "TRAYDIOSG001",
        "Target type must be partial",
        "Type '{0}' must be declared partial for source generation",
        "Traydio.SourceGenerator",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor _invalidLinkedTypeDescriptor = new(
        "TRAYDIOSG002",
        "Linked type is invalid",
        "Linked type '{0}' is invalid for attribute '{1}'",
        "Traydio.SourceGenerator",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor _constructorDescriptor = new(
        "TRAYDIOSG003",
        "No usable constructor",
        "No usable constructor found for view '{0}'. Add a public parameterless constructor or reference Microsoft.Extensions.DependencyInjection for constructor injection.",
        "Traydio.SourceGenerator",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor _duplicateMappingDescriptor = new(
        "TRAYDIOSG004",
        "Duplicate mapping",
        "View model '{0}' is already mapped to '{1}'",
        "Traydio.SourceGenerator",
        DiagnosticSeverity.Error,
        true);

    private static readonly DiagnosticDescriptor _invalidConfigurationDescriptor = new(
        "TRAYDIOSG005",
        "Invalid view locator configuration",
        "Generated view locator configuration is invalid: {0}",
        "Traydio.SourceGenerator",
        DiagnosticSeverity.Error,
        true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var viewMappings = context.SyntaxProvider.ForAttributeWithMetadataName(
            _VIEW_FOR_ATTRIBUTE_NAME,
            static (_, _) => true,
            static (ctx, _) => CreateCandidate(ctx, MappingKind.ViewFor));

        var viewModelMappings = context.SyntaxProvider.ForAttributeWithMetadataName(
            _VIEW_MODEL_FOR_ATTRIBUTE_NAME,
            static (_, _) => true,
            static (ctx, _) => CreateCandidate(ctx, MappingKind.ViewModelFor));

        var payload = context.CompilationProvider
            .Combine(viewMappings.Collect())
            .Combine(viewModelMappings.Collect());

        context.RegisterSourceOutput(
            payload,
            static (spc, data) => Execute(spc, data.Left.Left, data.Left.Right, data.Right));
    }

    private static MappingCandidate? CreateCandidate(GeneratorAttributeSyntaxContext context, MappingKind kind)
    {
        if (context.TargetSymbol is not INamedTypeSymbol targetSymbol || context.Attributes.Length == 0)
        {
            return null;
        }

        var attributeData = context.Attributes[0];
        if (attributeData.ConstructorArguments.Length != 1)
        {
            return null;
        }

        var linkedType = attributeData.ConstructorArguments[0].Value as INamedTypeSymbol;
        if (linkedType is null)
        {
            return null;
        }

        var location = GetAttributeLocation(attributeData, targetSymbol);
        return new MappingCandidate(targetSymbol, linkedType, kind, location);
    }

    private static Location GetAttributeLocation(AttributeData attributeData, ISymbol fallbackSymbol)
    {
        var syntax = attributeData.ApplicationSyntaxReference?.GetSyntax();
        return syntax?.GetLocation() ?? fallbackSymbol.Locations.FirstOrDefault() ?? Location.None;
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<MappingCandidate?> viewMappings,
        ImmutableArray<MappingCandidate?> viewModelMappings)
    {
        var allCandidates = viewMappings.AddRange(viewModelMappings)
            .Where(static c => c != null)
            .Select(static c => c!)
            .ToArray();

        var config = ResolveLocatorConfig(compilation, context);
        var hasActivatorUtilities = compilation.GetTypeByMetadataName("Microsoft.Extensions.DependencyInjection.ActivatorUtilities") != null;
        var allowDiConstructors = config.EnableDependencyInjection && hasActivatorUtilities;
        var emitAvaloniaTemplate = compilation.GetTypeByMetadataName("Avalonia.Controls.Templates.IDataTemplate") != null
                                  && compilation.GetTypeByMetadataName("Avalonia.Controls.Control") != null;

        var mappings = new Dictionary<string, ViewModelToViewMapping>(StringComparer.Ordinal);
        var generatedTypeHints = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in allCandidates)
        {
            if (!ValidateTarget(context, candidate))
            {
                continue;
            }

            if (candidate.LinkedType.TypeKind != TypeKind.Class)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    _invalidLinkedTypeDescriptor,
                    candidate.Location,
                    candidate.LinkedType.ToDisplayString(),
                    candidate.Kind == MappingKind.ViewFor ? "ViewFor" : "ViewModelFor"));
                continue;
            }

            var viewType = candidate.Kind == MappingKind.ViewFor ? candidate.TargetType : candidate.LinkedType;
            var viewModelType = candidate.Kind == MappingKind.ViewFor ? candidate.LinkedType : candidate.TargetType;

            if (!HasUsableConstructor(viewType, allowDiConstructors))
            {
                context.ReportDiagnostic(Diagnostic.Create(_constructorDescriptor, candidate.Location, viewType.ToDisplayString()));
                continue;
            }

            var helperMethod = candidate.Kind == MappingKind.ViewFor
                ? "public static global::System.Type GetLinkedViewModelType() => typeof(" + ToFullyQualifiedType(viewModelType) + ");"
                : "public static global::System.Type GetLinkedViewType() => typeof(" + ToFullyQualifiedType(viewType) + ");";

            var helperHint = GetTypeHint(candidate.TargetType, candidate.Kind == MappingKind.ViewFor ? "ViewModel" : "View");
            if (generatedTypeHints.Add(helperHint))
            {
                context.AddSource(helperHint, BuildPartialTypeSource(candidate.TargetType, helperMethod));
            }

            var key = ToFullyQualifiedType(viewModelType);
            if (mappings.TryGetValue(key, out var existing))
            {
                if (!SymbolEqualityComparer.Default.Equals(existing.ViewType, viewType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        _duplicateMappingDescriptor,
                        candidate.Location,
                        viewModelType.ToDisplayString(),
                        existing.ViewType.ToDisplayString()));
                }

                continue;
            }

            mappings.Add(key, new ViewModelToViewMapping(viewModelType, viewType));
        }

        context.AddSource(
            config.Namespace + "." + config.ClassName + ".g.cs",
            BuildLocatorSource(config, mappings.Values, allowDiConstructors, emitAvaloniaTemplate));
    }

    private static bool ValidateTarget(SourceProductionContext context, MappingCandidate candidate)
    {
        if (candidate.TargetType.TypeKind != TypeKind.Class)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                _invalidLinkedTypeDescriptor,
                candidate.Location,
                candidate.TargetType.ToDisplayString(),
                candidate.Kind == MappingKind.ViewFor ? "ViewFor" : "ViewModelFor"));
            return false;
        }

        if (!IsPartial(candidate.TargetType))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                _nonPartialTypeDescriptor,
                candidate.Location,
                candidate.TargetType.ToDisplayString()));
            return false;
        }

        return true;
    }

    private static bool IsPartial(INamedTypeSymbol symbol)
    {
        if (symbol.DeclaringSyntaxReferences.Length == 0)
        {
            return false;
        }

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var declaration = syntaxRef.GetSyntax() as TypeDeclarationSyntax;
            if (declaration == null || !declaration.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasUsableConstructor(INamedTypeSymbol viewType, bool allowDiConstructors)
    {
        var publicConstructors = viewType.InstanceConstructors
            .Where(static c => c.DeclaredAccessibility == Accessibility.Public && !c.IsImplicitlyDeclared)
            .ToArray();

        if (publicConstructors.Length == 0)
        {
            return false;
        }

        if (allowDiConstructors)
        {
            return true;
        }

        return publicConstructors.Any(static c => c.Parameters.Length == 0);
    }

    private static LocatorConfig ResolveLocatorConfig(Compilation compilation, SourceProductionContext context)
    {
        var namespaceName = compilation.AssemblyName ?? "Traydio";
        var className = "GeneratedViewLocator";
        var serviceProviderPropertyName = "ServiceProvider";
        var enableDependencyInjection = true;

        var assemblyAttribute = compilation.Assembly.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == _GENERATE_VIEW_LOCATOR_ATTRIBUTE_NAME);

        if (assemblyAttribute == null)
        {
            return new LocatorConfig(namespaceName, className, serviceProviderPropertyName, enableDependencyInjection);
        }

        if (assemblyAttribute.ConstructorArguments.Length == 2)
        {
            var configuredNamespace = assemblyAttribute.ConstructorArguments[0].Value as string;
            var configuredClass = assemblyAttribute.ConstructorArguments[1].Value as string;
            if (!string.IsNullOrWhiteSpace(configuredNamespace) && !string.IsNullOrWhiteSpace(configuredClass))
            {
                namespaceName = configuredNamespace;
                className = configuredClass;
            }
        }

        foreach (var namedArgument in assemblyAttribute.NamedArguments)
        {
            if (namedArgument.Key == "FullyQualifiedName")
            {
                var value = namedArgument.Value.Value as string;
                if (!string.IsNullOrWhiteSpace(value) && TryParseFullyQualifiedName(value!, out var ns, out var cn))
                {
                    namespaceName = ns;
                    className = cn;
                }
                else if (!string.IsNullOrWhiteSpace(value))
                {
                    context.ReportDiagnostic(Diagnostic.Create(_invalidConfigurationDescriptor, Location.None, "FullyQualifiedName is not a valid type name."));
                }
            }
            else if (namedArgument.Key == "ServiceProviderPropertyName")
            {
                var value = namedArgument.Value.Value as string;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    serviceProviderPropertyName = value;
                }
            }
            else if (namedArgument.Key == "EnableDependencyInjection" && namedArgument.Value.Value is bool enabled)
            {
                enableDependencyInjection = enabled;
            }
        }

        if (!IsValidNamespace(namespaceName))
        {
            context.ReportDiagnostic(Diagnostic.Create(_invalidConfigurationDescriptor, Location.None, "Namespace is not a valid C# namespace."));
            namespaceName = compilation.AssemblyName ?? "Traydio";
        }

        if (!SyntaxFacts.IsValidIdentifier(className))
        {
            context.ReportDiagnostic(Diagnostic.Create(_invalidConfigurationDescriptor, Location.None, "ClassName is not a valid C# identifier."));
            className = "GeneratedViewLocator";
        }

        if (!SyntaxFacts.IsValidIdentifier(serviceProviderPropertyName))
        {
            context.ReportDiagnostic(Diagnostic.Create(_invalidConfigurationDescriptor, Location.None, "ServiceProviderPropertyName is not a valid C# identifier."));
            serviceProviderPropertyName = "ServiceProvider";
        }

        return new LocatorConfig(namespaceName!, className, serviceProviderPropertyName, enableDependencyInjection);
    }

    private static bool TryParseFullyQualifiedName(string value, out string @namespace, out string className)
    {
        var separator = value.LastIndexOf('.');
        if (separator <= 0 || separator == value.Length - 1)
        {
            @namespace = string.Empty;
            className = string.Empty;
            return false;
        }

        @namespace = value.Substring(0, separator);
        className = value.Substring(separator + 1);
        return true;
    }

    private static bool IsValidNamespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value!.Split('.').All(SyntaxFacts.IsValidIdentifier);
    }

    private static string BuildPartialTypeSource(INamedTypeSymbol targetType, string memberCode)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();

        if (!targetType.ContainingNamespace.IsGlobalNamespace)
        {
            builder.Append("namespace ").Append(targetType.ContainingNamespace.ToDisplayString()).AppendLine();
            builder.AppendLine("{");
        }

        var chain = new Stack<INamedTypeSymbol>();
        var current = targetType;
        while (current != null)
        {
            chain.Push(current);
            current = current.ContainingType;
        }

        var indent = string.Empty;
        while (chain.Count > 0)
        {
            var type = chain.Pop();
            builder.Append(indent)
                .Append("partial ")
                .Append(GetTypeKeyword(type))
                .Append(' ')
                .Append(type.Name)
                .AppendLine();
            builder.Append(indent).AppendLine("{");
            indent += "    ";
        }

        builder.Append(indent).AppendLine(memberCode);

        while (indent.Length > 0)
        {
            indent = indent.Substring(0, indent.Length - 4);
            builder.Append(indent).AppendLine("}");
        }

        if (!targetType.ContainingNamespace.IsGlobalNamespace)
        {
            builder.AppendLine("}");
        }

        builder.AppendLine("#nullable restore");

        return builder.ToString();
    }

    private static string GetTypeKeyword(INamedTypeSymbol type)
    {
        var syntax = type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        return syntax is RecordDeclarationSyntax ? "record" : "class";
    }

    private static string BuildLocatorSource(
        LocatorConfig config,
        IEnumerable<ViewModelToViewMapping> mappings,
        bool allowDiConstructors,
        bool emitAvaloniaTemplate)
    {
        var orderedMappings = mappings.OrderBy(m => ToFullyQualifiedType(m.ViewModelType), StringComparer.Ordinal).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.Append("namespace ").Append(config.Namespace).AppendLine();
        builder.AppendLine("{");
        builder.Append("    public partial class ").Append(config.ClassName);
        if (emitAvaloniaTemplate)
        {
            builder.Append(" : global::Avalonia.Controls.Templates.IDataTemplate");
        }

        builder.AppendLine();
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly global::System.IServiceProvider? _serviceProvider;");
        builder.AppendLine();
        builder.Append("        public global::System.IServiceProvider? ").Append(config.ServiceProviderPropertyName).AppendLine(" { get; set; }");
        builder.AppendLine();
        builder.Append("        public ").Append(config.ClassName).AppendLine("()")
            .AppendLine("        {")
            .AppendLine("        }");
        builder.AppendLine();
        builder.Append("        public ").Append(config.ClassName).AppendLine("(global::System.IServiceProvider serviceProvider)")
            .AppendLine("        {")
            .AppendLine("            _serviceProvider = serviceProvider;")
            .AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public global::System.Type? GetViewType(global::System.Type viewModelType)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (viewModelType == null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            var candidate = viewModelType;");
        foreach (var mapping in orderedMappings)
        {
            builder.Append("            if (candidate == typeof(").Append(ToFullyQualifiedType(mapping.ViewModelType)).AppendLine("))");
            builder.AppendLine("            {");
            builder.Append("                return typeof(").Append(ToFullyQualifiedType(mapping.ViewType)).AppendLine(");");
            builder.AppendLine("            }");
        }

        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public object? BuildObject(object? viewModel)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (viewModel == null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            var viewType = GetViewType(viewModel.GetType());");
        builder.AppendLine("            if (viewType == null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();

        if (allowDiConstructors)
        {
            builder.Append("            var provider = _serviceProvider ?? ").Append(config.ServiceProviderPropertyName).AppendLine(";");
            builder.AppendLine("            if (provider == null)");
            builder.AppendLine("            {");
            builder.AppendLine("                return global::System.Activator.CreateInstance(viewType);");
            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine("            return global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance(provider, viewType);");
        }
        else
        {
            builder.AppendLine("            return global::System.Activator.CreateInstance(viewType);");
        }

        builder.AppendLine("        }");
        builder.AppendLine();

        if (emitAvaloniaTemplate)
        {
            builder.AppendLine("        public global::Avalonia.Controls.Control? Build(object? param)");
            builder.AppendLine("        {");
            builder.AppendLine("            var instance = BuildObject(param);");
            builder.AppendLine("            if (instance is global::Avalonia.Controls.Control control)");
            builder.AppendLine("            {");
            builder.AppendLine("                return control;");
            builder.AppendLine("            }");
            builder.AppendLine();
            builder.AppendLine("            return new global::Avalonia.Controls.TextBlock");
            builder.AppendLine("            {");
            builder.AppendLine("                Text = param == null ? \"Not Found\" : \"Not Found: \" + param.GetType().FullName,");
            builder.AppendLine("            };");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        public bool Match(object? data)");
            builder.AppendLine("        {");
            builder.AppendLine("            return data != null && GetViewType(data.GetType()) != null;");
            builder.AppendLine("        }");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine("#nullable restore");
        return builder.ToString();
    }

    private static string GetTypeHint(INamedTypeSymbol targetType, string suffix)
    {
        var fullName = ToFullyQualifiedType(targetType)
            .Replace("global::", string.Empty)
            .Replace('.', '_')
            .Replace('+', '_');
        return fullName + "." + suffix + ".g.cs";
    }

    private static string ToFullyQualifiedType(INamedTypeSymbol symbol)
    {
        return symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private enum MappingKind
    {
        ViewFor,
        ViewModelFor,
    }

    private sealed class MappingCandidate(INamedTypeSymbol targetType, INamedTypeSymbol linkedType, MappingKind kind, Location location)
    {
        public INamedTypeSymbol TargetType { get; } = targetType;

        public INamedTypeSymbol LinkedType { get; } = linkedType;

        public MappingKind Kind { get; } = kind;

        public Location Location { get; } = location;
    }

    private sealed class ViewModelToViewMapping(INamedTypeSymbol viewModelType, INamedTypeSymbol viewType)
    {
        public INamedTypeSymbol ViewModelType { get; } = viewModelType;

        public INamedTypeSymbol ViewType { get; } = viewType;
    }

    private sealed class LocatorConfig(string ns, string className, string serviceProviderPropertyName, bool enableDependencyInjection)
    {
        public string Namespace { get; } = ns;

        public string ClassName { get; } = className;

        public string ServiceProviderPropertyName { get; } = serviceProviderPropertyName;

        public bool EnableDependencyInjection { get; } = enableDependencyInjection;
    }
}
