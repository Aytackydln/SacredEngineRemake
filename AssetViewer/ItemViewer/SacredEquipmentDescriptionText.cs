using System;
using System.Linq;
using Sacred.Core.Pak.Weapon.Descriptions;

namespace AssetViewer.ItemViewer;

internal static class SacredEquipmentDescriptionText
{
    public static string Format(SacredEquipmentDescription description)
    {
        var sections = description.Sections.Select(section => section.Heading + Environment.NewLine +
            string.Join(Environment.NewLine, section.Fields.Select(field =>
                string.IsNullOrEmpty(field.Value) ? field.Label : $"{field.Label}: {field.Value}")));
        return string.Join(Environment.NewLine + Environment.NewLine, new[] { description.Name }.Concat(sections));
    }
}
