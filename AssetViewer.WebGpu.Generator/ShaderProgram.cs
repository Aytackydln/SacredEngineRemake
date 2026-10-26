using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;

namespace AssetViewer.WebGpu.Generator;

/// <summary>The browser binding adapters; shader algorithms remain in the original HLSL.</summary>
internal sealed class ShaderProgram : IEquatable<ShaderProgram>
{
    internal static readonly string[] Names =
    [
        "SacredModel", "SacredAnimatedModel", "SacredEffectModel", "SacredItemParticle",
        "SacredItemGlow", "SacredInventoryUi", "PreviewGroundGrid", "SacredSkinning"
    ];

    internal string Name { get; }
    internal string Source { get; }
    internal string? Error { get; }
    internal string[] Stages => Name == "SacredSkinning" ? ["compute"]
        : Name == "SacredModel" ? ["vertex", "fragment", "opaque"] : ["vertex", "fragment"];

    private ShaderProgram(string name, string source, string? error = null)
    { Name = name; Source = source; Error = error; }

    internal static ImmutableArray<ShaderProgram> Create(ImmutableArray<(string Name, string Content)> inputs)
    {
        var files = inputs.GroupBy(file => file.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        return Names.Select(name => Adapt(name, files)).ToImmutableArray();
    }

    private static ShaderProgram Adapt(string name, Dictionary<string, (string Name, string Content)[]> files)
    {
        var required = name == "SacredSkinning" ? new[] { "SacredSkinning.hlsl", "SacredSkinning.compute.hlsl" }
            : name == "PreviewGroundGrid" ? new[] { "PreviewGroundGrid.hlsl" }
            : new[] { "HdrCommon.hlsl", "SacredItemTextureAnimation.hlsl", "SacredObjectFocus.hlsl", name + ".hlsl" };
        foreach (var file in required)
            if (!files.TryGetValue(file, out var matches) || matches.Length != 1)
                return new(name, "", $"Expected exactly one AdditionalFile named {file}.");
        var source = string.Join("\n", required.Select(file => files[file][0].Content));
        if (name == "SacredSkinning")
            source = source.Replace("StructuredBuffer<SkinVertex> skin_vertices", "ByteAddressBuffer skin_vertices")
                .Replace("skin_vertices[index]", "browser_skin_vertex(index)");
        else
            source = "Texture2D browser_secondary_glow : register(t3);\n" + source;
        source = source.Replace("Texture2D glow = ResourceDescriptorHeap[(uint)descriptor_slot];",
            "Texture2D glow = browser_secondary_glow;");
        source = Regex.Replace(source, @"(?m)^\s*#pragma (vertex|fragment).*$", "");
        return new(name, source);
    }

    public bool Equals(ShaderProgram? other) => other is not null &&
        Name == other.Name && Source == other.Source && Error == other.Error;
    public override bool Equals(object? obj) => obj is ShaderProgram other && Equals(other);
    public override int GetHashCode() => Name.GetHashCode() ^ Source.GetHashCode();
}
