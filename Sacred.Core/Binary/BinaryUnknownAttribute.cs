namespace Sacred.Core.Binary;

/// <summary>Marks a serialized field whose purpose has not yet been established.</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class BinaryUnknownAttribute : Attribute;
