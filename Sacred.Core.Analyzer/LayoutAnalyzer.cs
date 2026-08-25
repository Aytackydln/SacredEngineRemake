using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sacred.Core.Analyzer;

/// <summary>Calculates binary-layout coverage directly from Roslyn source symbols.</summary>
internal sealed partial class LayoutAnalyzer
{
    private const string StructLayoutAttributeName = "System.Runtime.InteropServices.StructLayoutAttribute";
    private const string FieldOffsetAttributeName = "System.Runtime.InteropServices.FieldOffsetAttribute";
    private const string InlineArrayAttributeName = "System.Runtime.CompilerServices.InlineArrayAttribute";
    private const string BinaryStringAttributeName = "Sacred.Core.Binary.BinaryStringAttribute";
    private const string BinaryUnknownAttributeName = "Sacred.Core.Binary.BinaryUnknownAttribute";

    private readonly Compilation _compilation;

    public LayoutAnalyzer(Compilation compilation) => _compilation = compilation;

    public IReadOnlyList<string> DiscoverLayoutTypeNames()
    {
        var names = new List<string>();
        CollectLayoutTypeNames(_compilation.Assembly.GlobalNamespace, names);
        return names.OrderBy(static name => name, StringComparer.Ordinal).ToArray();
    }

    public LayoutCoverage Analyze(string metadataName)
    {
        var type = _compilation.GetTypeByMetadataName(metadataName)
                   ?? throw new InvalidOperationException($"Mapped layout type '{metadataName}' was not found in Sacred.Core source.");
        var layoutAttribute = FindAttribute(type, StructLayoutAttributeName)
                              ?? throw new InvalidOperationException($"Mapped type '{metadataName}' has no StructLayout attribute.");
        var layoutKind = layoutAttribute.ConstructorArguments.FirstOrDefault().Value is int kind ? kind : 3;
        if (layoutKind == 3)
            throw new InvalidOperationException($"Mapped type '{metadataName}' uses LayoutKind.Auto.");

        var fields = type.GetMembers().OfType<IFieldSymbol>()
            .Where(static member => !member.IsStatic && !member.IsImplicitlyDeclared)
            .OrderBy(static member => member.Locations.FirstOrDefault()?.SourceSpan.Start ?? int.MaxValue)
            .ToArray();
        var pack = GetNamedInt(layoutAttribute, "Pack") ?? 0;
        var explicitSize = GetNamedInt(layoutAttribute, "Size") ?? 0;
        var analyzedFields = AnalyzeFields(fields, layoutKind, pack).ToArray();
        ValidateNoOverlappingFields(type, analyzedFields);
        var computedSize = analyzedFields.Length == 0 ? 1 : analyzedFields.Max(static item => item.Offset + item.Size);
        var size = Math.Max(explicitSize, computedSize);

        var knownBytes = new bool[size];
        foreach (var item in analyzedFields.Where(static item => item.IsKnown))
        {
            for (var index = Math.Max(0, item.Offset); index < Math.Min(size, item.Offset + item.Size); index++)
                knownBytes[index] = true;
        }

        return new LayoutCoverage(
            type.Name,
            type.ContainingNamespace.ToDisplayString(),
            size,
            GetDocumentation(type),
            analyzedFields,
            FindUnknownRanges(knownBytes));
    }

    private static void ValidateNoOverlappingFields(
        INamedTypeSymbol layoutType,
        IReadOnlyList<FieldCoverage> fields)
    {
        for (var leftIndex = 0; leftIndex < fields.Count; leftIndex++)
        {
            var left = fields[leftIndex];
            var leftEnd = left.Offset + left.Size;
            for (var rightIndex = leftIndex + 1; rightIndex < fields.Count; rightIndex++)
            {
                var right = fields[rightIndex];
                var rightEnd = right.Offset + right.Size;
                if (left.Offset >= rightEnd || right.Offset >= leftEnd)
                    continue;

                throw new InvalidOperationException(
                    $"Layout '{layoutType.ToDisplayString()}' has overlapping serialized fields: " +
                    $"'{left.Name}' [0x{left.Offset:X}..0x{leftEnd - 1:X}] and " +
                    $"'{right.Name}' [0x{right.Offset:X}..0x{rightEnd - 1:X}].");
            }
        }
    }

    private IEnumerable<FieldCoverage> AnalyzeFields(IReadOnlyList<IFieldSymbol> fields, int layoutKind, int pack)
    {
        var sequentialOffset = 0;
        foreach (var sourceField in fields)
        {
            var stringAttribute = FindAttribute(sourceField, BinaryStringAttributeName);
            var naturalSize = GetSerializedSize(sourceField.Type);
            var size = stringAttribute?.ConstructorArguments.ElementAtOrDefault(1).Value is int byteLength
                ? byteLength
                : naturalSize;
            int offset;
            if (layoutKind == 2)
            {
                var offsetAttribute = FindAttribute(sourceField, FieldOffsetAttributeName);
                if (offsetAttribute?.ConstructorArguments.FirstOrDefault().Value is not int explicitOffset)
                    continue;
                offset = explicitOffset;
            }
            else
            {
                var alignment = pack > 0 ? Math.Min(pack, naturalSize) : naturalSize;
                offset = Align(sequentialOffset, Math.Max(1, alignment));
                sequentialOffset = checked(offset + size);
            }

            var isUnknown = FindAttribute(sourceField, BinaryUnknownAttributeName) is not null ||
                            sourceField.Name.Contains("unknown", StringComparison.OrdinalIgnoreCase);
            var name = stringAttribute?.ConstructorArguments.FirstOrDefault().Value as string
                       ?? sourceField.Name.TrimStart('_');
            var typeName = stringAttribute is null
                ? GetFriendlyTypeName(sourceField.Type)
                : GetStringTypeName(stringAttribute, size);
            yield return new FieldCoverage(
                name,
                typeName,
                offset,
                size,
                !isUnknown,
                stringAttribute is not null,
                GetDocumentation(sourceField));
        }
    }

    private int GetSerializedSize(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying })
            return GetSerializedSize(underlying);

        if (type is INamedTypeSymbol namedType)
        {
            var inlineArray = FindAttribute(namedType, InlineArrayAttributeName);
            if (inlineArray?.ConstructorArguments.FirstOrDefault().Value is int length)
            {
                var element = namedType.GetMembers().OfType<IFieldSymbol>()
                    .Single(static member => !member.IsStatic && !member.IsImplicitlyDeclared);
                return checked(length * GetSerializedSize(element.Type));
            }

            var layout = FindAttribute(namedType, StructLayoutAttributeName);
            var explicitSize = layout is null ? 0 : GetNamedInt(layout, "Size") ?? 0;
            if (explicitSize > 0)
                return explicitSize;
        }

        return type.SpecialType switch
        {
            SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Boolean => 1,
            SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Char => 2,
            SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => 4,
            SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Double => 8,
            _ => throw new InvalidOperationException($"Cannot determine serialized size of source type '{type.ToDisplayString()}'.")
        };
    }

    private static AttributeData? FindAttribute(ISymbol symbol, string metadataName) =>
        symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == metadataName);

    private static void CollectLayoutTypeNames(INamespaceSymbol namespaceSymbol, ICollection<string> names)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            if (FindAttribute(type, StructLayoutAttributeName) is not null)
                names.Add(type.ToDisplayString());
        }
        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
            CollectLayoutTypeNames(childNamespace, names);
    }

    private static int? GetNamedInt(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value is int value ? value : null;

    private static string GetStringTypeName(AttributeData attribute, int size)
    {
        var encoding = attribute.ConstructorArguments.ElementAtOrDefault(2).Value as string ?? "unknown encoding";
        var nullTerminatedArgument = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == "NullTerminated");
        var nullTerminated = nullTerminatedArgument.Key is null || nullTerminatedArgument.Value.Value is true;
        return $"string[{size}] ({encoding}{(nullTerminated ? ", NUL-terminated" : string.Empty)})";
    }

    private string GetFriendlyTypeName(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying } enumType)
            return $"{enumType.Name} ({GetFriendlyTypeName(underlying)})";

        if (type is INamedTypeSymbol namedType)
        {
            var inlineArray = FindAttribute(namedType, InlineArrayAttributeName);
            if (inlineArray?.ConstructorArguments.FirstOrDefault().Value is int length)
            {
                var element = namedType.GetMembers().OfType<IFieldSymbol>()
                    .Single(static member => !member.IsStatic && !member.IsImplicitlyDeclared);
                return $"{GetFriendlyTypeName(element.Type)}[{length}]";
            }
        }

        return type.SpecialType switch
        {
            SpecialType.System_Byte => "byte",
            SpecialType.System_SByte => "sbyte",
            SpecialType.System_Int16 => "int16",
            SpecialType.System_UInt16 => "uint16",
            SpecialType.System_Int32 => "int32",
            SpecialType.System_UInt32 => "uint32",
            SpecialType.System_Int64 => "int64",
            SpecialType.System_UInt64 => "uint64",
            SpecialType.System_Single => "float32",
            SpecialType.System_Double => "float64",
            _ => type.Name
        };
    }

    private static string GetDocumentation(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml(expandIncludes: true);
        if (string.IsNullOrWhiteSpace(xml))
            return "No XML documentation yet.";
        try
        {
            var summary = XElement.Parse(xml).Element("summary")?.Value;
            return string.IsNullOrWhiteSpace(summary)
                ? "No XML documentation yet."
                : Whitespace().Replace(summary, " ").Trim();
        }
        catch
        {
            return "No XML documentation yet.";
        }
    }

    private static int Align(int value, int alignment) => checked((value + alignment - 1) / alignment * alignment);

    private static IReadOnlyList<ByteRange> FindUnknownRanges(IReadOnlyList<bool> knownBytes)
    {
        var ranges = new List<ByteRange>();
        var start = -1;
        for (var index = 0; index <= knownBytes.Count; index++)
        {
            var isUnknown = index < knownBytes.Count && !knownBytes[index];
            if (isUnknown && start < 0)
                start = index;
            else if (!isUnknown && start >= 0)
            {
                ranges.Add(new ByteRange(start, index - start));
                start = -1;
            }
        }
        return ranges;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>Builds the Roslyn compilation used for source-symbol analysis.</summary>
internal static class SacredCoreCompilation
{
    public static CSharpCompilation Create(string sourceDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Sacred.Core source directory was not found: {sourceDirectory}");

        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var syntaxTrees = Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(static path => !IsGeneratedPath(path))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), parseOptions, path))
            .Prepend(CSharpSyntaxTree.ParseText(
                "global using System; global using System.Collections.Generic; global using System.IO; " +
                "global using System.Linq; global using System.Net.Http; global using System.Threading; " +
                "global using System.Threading.Tasks;",
                parseOptions,
                "Sacred.Core.GlobalUsings.g.cs"))
            .ToArray();
        return CSharpCompilation.Create(
            "Sacred.Core.SourceAnalysis",
            syntaxTrees,
            GetMetadataReferences(sourceDirectory),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
    }

    private static ImmutableArray<MetadataReference> GetMetadataReferences(string sourceDirectory)
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
                                ?? throw new InvalidOperationException("The runtime did not provide trusted platform assemblies.");
        var paths = trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
        AddProjectAssetReferences(sourceDirectory, paths);
        return paths.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();
    }

    private static void AddProjectAssetReferences(string sourceDirectory, ICollection<string> paths)
    {
        var assetsPath = Path.Combine(sourceDirectory, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
            return;

        using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
        var root = document.RootElement;
        var packageRoot = root.GetProperty("packageFolders").EnumerateObject().FirstOrDefault().Name;
        var target = root.GetProperty("targets").EnumerateObject().FirstOrDefault().Value;
        if (string.IsNullOrEmpty(packageRoot) || target.ValueKind != JsonValueKind.Object)
            return;

        var libraries = root.GetProperty("libraries");
        foreach (var targetLibrary in target.EnumerateObject())
        {
            if (!targetLibrary.Value.TryGetProperty("compile", out var compileFiles) ||
                !libraries.TryGetProperty(targetLibrary.Name, out var library) ||
                !library.TryGetProperty("path", out var libraryPath))
                continue;

            foreach (var compileFile in compileFiles.EnumerateObject())
            {
                var path = Path.Combine(packageRoot, libraryPath.GetString()!, compileFile.Name.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                    paths.Add(path);
            }
        }
    }

    private static bool IsGeneratedPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
