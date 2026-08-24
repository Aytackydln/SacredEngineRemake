using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;

namespace Sacred.Core;

[InlineArray(3)]
public struct Signature3
{
    private byte _element0;

    public string Text => string.Join("", this);

    [Pure]
    public bool Compare(char byte0, char byte1, char byte2) =>
        (byte)byte0 == this[0] && byte1 == this[1] && byte2 == this[2];
}