using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.UI.Hud;

namespace Sacred.UI.Layout;

/// <summary>Stacks independent components in local UI coordinates without changing their geometry.</summary>
public sealed class VerticalPanelLayout
{
    public IReadOnlyList<HudRectangle> Children { get; }
    public Vector2 Size { get; }

    public VerticalPanelLayout(IEnumerable<Vector2> sizes)
    {
        var children = new List<HudRectangle>();
        var width = 0f;
        var y = 0f;
        foreach (var size in sizes)
        {
            if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || size.X < 0 || size.Y < 0)
                throw new ArgumentOutOfRangeException(nameof(sizes));
            children.Add(new(new(0, y), size));
            width = MathF.Max(width, size.X);
            y += size.Y;
        }
        Children = children.AsReadOnly();
        Size = new(width, y);
    }
}
