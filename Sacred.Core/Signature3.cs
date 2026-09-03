using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Sacred.Core;

[InlineArray(3)]
public struct Signature3 : IEquatable<Signature3>
{
    public static readonly Signature3 Item = new('I', 'T', 'M');
    public static readonly Signature3 Sound = new('S', 'N', 'D');
    public static readonly Signature3 Weapon = new('W', 'P', 'N');
    public static readonly Signature3 Texture = new('T', 'E', 'X');
    public static readonly Signature3 SoundProfile = new('S', 'P', 'F');

    private byte _element0;

    public string Text => string.Join("", this);

    public Signature3(char a, char b, char c)
    {
        this[0] = checked((byte)a);
        this[1] = checked((byte)b);
        this[2] = checked((byte)c);
    }

    [Pure]
    public bool Equals(Signature3 other)
    {
        return this[0] == other[0]  && this[1] == other[1] &&  this[2] == other[2];
    }

    public override bool Equals(object? obj)
    {
        return obj is Signature3 other && Equals(other);
    }

    public override int GetHashCode()
    {
        return _element0.GetHashCode();
    }

    public static bool operator ==(Signature3 left, Signature3 right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Signature3 left, Signature3 right)
    {
        return !(left == right);
    }
}