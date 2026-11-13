using Sacred.Core.GameBin.Scripts;
using System.Buffers.Binary;
using System.Text;

namespace Sacred.GameplayHost.Scripting;

public sealed record WorldScriptDeferredObjectState(string Name,short Open,short Locked)
{
    public uint PackedState=>unchecked((uint)(ushort)Open|((uint)(ushort)Locked<<16));
    /// <summary>Shorter names leave unknown native scratch after their terminator.</summary>
    public bool HasCompleteBytes=>Name.Length==SacredScriptDeferredObjectStateLayout.NameLength-1;
    /// <summary>Explicit normalization of bytes after the native name terminator.
    /// Lookup stops at NUL; these zeros are not claimed caller-stack defaults.</summary>
    public byte[] ToNormalizedPayload()
    {
        if (Name.Length >= SacredScriptDeferredObjectStateLayout.NameLength || Name.Any(value=>value>255 || value==0))
            throw new InvalidDataException("Deferred state name must fit the original Latin-1 C string.");
        var bytes=new byte[SacredScriptDeferredObjectStateLayout.SerializedSize];
        Encoding.Latin1.GetBytes(Name).CopyTo(bytes,0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(SacredScriptDeferredObjectStateLayout.StateOffset),PackedState);
        return bytes;
    }
    public byte[] ToPayload()=>HasCompleteBytes?ToNormalizedPayload():
        throw new InvalidDataException("Raw deferred state serialization requires original name-tail bytes.");
}

