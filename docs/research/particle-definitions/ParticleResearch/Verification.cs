using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Particles;

namespace ParticleResearch;

internal static class Verification
{
    public static void Run()
    {
        var checks = 0;
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception($"Verification failed: {name}");
            checks++;
        }
        Check(Marshal.SizeOf<SacredScriptCommandHeaderLayout>() == 4, "command prefix size");
        Check(Marshal.SizeOf<SacredScriptHeightArgumentLayout>() == 5, "height operand size");
        Check(Marshal.SizeOf<SacredScriptPositionArgumentLayout>() == 13, "position operand size");
        Check(Marshal.SizeOf<SacredScriptTypeArgumentLayout>() == 5, "type operand size");
        Check(Marshal.SizeOf<SacredParticleEmissionLayout>() == 0x64, "emission size");
        Check(Marshal.SizeOf<SacredParticleMotionLayout>() == 0x20, "motion size");
        Check(Marshal.SizeOf<SacredParticleVectorLayout>() == 12, "native vector size");
        Check(Marshal.SizeOf<SacredSmokeParticleStateLayout>() == 0xD9C, "smoke save block size");
        Check(Marshal.SizeOf<SacredDwarfMagicParticleStateLayout>() == 0x48C, "dwarf magic save block size");
        Check(Marshal.SizeOf<SacredSmokeParticleSystemLayout>() == 0x2E4C, "smoke native object size");
        Check(Marshal.SizeOf<SacredDwarfMagicParticleSystemLayout>() == 0x2530, "dwarf magic native object size");

        // Captured command, bin/TYPE_NPC_VAMPIRELADY/FunkCode.bin at 0x307C68.
        var captured = Convert.FromHexString("08003400014e4f4e5f554e4951554500027403000004d4080000410c00000000000020dc d90100b7910200000000007e1a000000".Replace(" ", ""));
        Check(SacredScriptCreateObjectReader.TryRead(new(0, captured), out var effect, out _), "captured command decoded");
        Check(effect!.TypeId == 884 && effect.TilePosition == new SacredScriptPosition(2260, 3137, 0)
              && effect.WorldPosition == new SacredScriptPosition(121308, 168375, 0) && effect.HeightOffset == 26,
            "captured command independently known values");
        var changed = (byte[])captured.Clone();
        BinaryPrimitives.WriteInt16LittleEndian(changed.AsSpan(48), -123);
        BinaryPrimitives.WriteUInt16LittleEndian(changed.AsSpan(50), 0xBA98);
        Check(SacredScriptCreateObjectReader.TryRead(new(0, changed), out effect, out _) && effect.HeightOffset == -123,
            "height is signed short; upper payload word ignored");
        var height = MemoryMarshal.Read<SacredScriptHeightArgumentLayout>(changed.AsSpan(47));
        Check(height.HeightOffset == -123 && height.Unknown03 == 0xBA98, "height layout preserves upper word");

        // A shorter name and reordered operands prevent fixed-offset or 52-byte pattern decoding.
        var variable = Convert.FromHexString("080011007ef9ffabcd0201000000014100");
        Check(SacredScriptCreateObjectReader.TryRead(new(0, variable), out effect, out _) &&
              effect.Name == "A" && effect.TypeId == 1 && effect.HeightOffset == -7 && effect.TilePosition == null,
            "variable string length, operand order and optional positions");
        var unknown = new byte[] { 0xFE, 0xFF, 4, 0 };
        var stream = unknown.Concat(captured).Concat(variable).ToArray();
        var commands = SacredCompiledScriptReader.Read(stream).ToArray();
        Check(commands.Length == 3 && commands[1].FileOffset == 4 && commands[2].FileOffset == 56,
            "unknown opcode preserved; command boundaries followed");
        Check(commands[0].Bytes.Span.SequenceEqual(unknown), "raw unknown bytes preserved");

        foreach (var bad in new[] { new byte[] { 8 }, new byte[] { 8, 0, 0, 0 }, new byte[] { 8, 0, 3, 0 },
                     captured[..^1], captured.Concat(new byte[] { 0 }).ToArray() })
        {
            var rejected = false;
            try { _ = SacredCompiledScriptReader.Read(bad).ToArray(); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid/truncated boundary rejected");
        }
        changed = (byte[])captured.Clone();
        changed[47] = 0xFE;
        Check(!SacredScriptCreateObjectReader.TryRead(new(0, changed), out effect, out _) && effect == null,
            "unknown operand does not return a partial placement");
        changed = (byte[])captured.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(changed.AsSpan(22), -2);
        Check(!SacredScriptCreateObjectReader.TryRead(new(0, changed), out _, out var reason) && reason!.Contains("Symbolic"),
            "symbolic tile position is not interpreted as literal");
        Check(!SacredScriptCreateObjectReader.TryRead(new(0, new byte[] { 8, 0, 6, 0, 1, 65 }), out _, out _),
            "unterminated name rejected");
        Check(!SacredScriptCreateObjectReader.TryRead(new(0, new byte[] { 8, 0, 6, 0, 2, 65 }), out _, out _),
            "truncated operand rejected");
        Console.WriteLine($"Verification passed: {checks} checks.");
    }
}
