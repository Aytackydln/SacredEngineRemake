using System.Buffers.Binary;
using Sacred.Core.GameRes;

namespace Sacred.Network.Gameplay;

/// <summary>Bank 3 sends only +0x120; full 1BA replacements also carry five function-name hashes.</summary>
public sealed class GoldQuestEntryState
{
    private readonly byte[]? _body;
    private readonly GoldScriptFunctionSnapshot? _functions;
    internal GoldQuestEntryState(ReadOnlySpan<byte> body, GoldScriptFunctionSnapshot? functions)
    {
        if (body.Length != GoldQuestLayout.EntrySize) throw new InvalidDataException("Invalid Gold quest entry.");
        _body = body.ToArray(); _functions = functions;
        RuntimeWord = BinaryPrimitives.ReadUInt32LittleEndian(body[GoldQuestLayout.EntryRuntimeWordOffset..]);
    }
    private GoldQuestEntryState(uint runtimeWord) => RuntimeWord = runtimeWord;
    public uint RuntimeWord { get; }
    public bool HasFullRecord => _body is not null;
    public byte[]? ToPayload() => _body?.ToArray();
    internal GoldQuestEntryState WithRuntimeWord(uint value)
    {
        if (_body is null) return new(value);
        var bytes = _body.ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldQuestLayout.EntryRuntimeWordOffset), value);
        return new(bytes, _functions);
    }
    internal static GoldQuestEntryState RuntimeOnly(uint value) => new(value);
    /// <summary>First matching native function-name hash; zero on no match. Null means the baseline was not loaded.</summary>
    public uint? ResolveCallback(int callback)
    {
        if ((uint)callback >= GoldQuestLayout.EntryCallbackOffsets.Count) throw new ArgumentOutOfRangeException(nameof(callback));
        if (_body is null || _functions is null || !_functions.TryGetDefinitions(out var definitions)) return null;
        var hash = BinaryPrimitives.ReadUInt32LittleEndian(_body.AsSpan(GoldQuestLayout.EntryCallbackOffsets[callback]));
        for (var i = 0; i < definitions.Count; i++)
            if (SacredResourceHash.ComputeDefaultCLocale(definitions[i].ToPayload().AsSpan(0, 64)) == hash) return (uint)i;
        return 0;
    }
}
