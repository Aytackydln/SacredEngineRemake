using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using Sacred.Core.Runtime;

namespace Sacred.Assets.Executable;

/// <summary>Automatic CreateNPC level operands from the installed executable.
/// Immediate curve operands and referenced floats are read, not copied defaults.</summary>
public sealed class SacredCreatureAutomaticLevelDefinitions
{
    private SacredCreatureAutomaticLevelDefinitions(SacredCreatureAutomaticLevelTuning tuning)=>Tuning=tuning;
    public SacredCreatureAutomaticLevelTuning Tuning { get; }

    public static SacredCreatureAutomaticLevelDefinitions Read(byte[] image)
    {
        using var pe=new PEReader(new MemoryStream(image,writable:false));
        var headers=pe.PEHeaders;
        var imageBase=headers.PEHeader?.ImageBase??throw new InvalidDataException("Missing original PE header.");
        var at=Unique(Convert.FromHexString("B889888888F7E1C1EA05899424B0000000899C24B4000000DFAC24B000000068"));
        var body=image.AsSpan(at,65);
        if(body[36]!=0x68 || !body.Slice(41,6).SequenceEqual<byte>([0x51,0xD9,0x1C,0x24,0x53,0xE8]) ||
            !body.Slice(51,2).SequenceEqual<byte>([0xD8,0x0D]))
            throw new InvalidDataException("Unknown original automatic-level time curve caller.");
        var limit=BinaryPrimitives.ReadSingleLittleEndian(body[32..]);
        var divisor=BinaryPrimitives.ReadSingleLittleEndian(body[37..]);
        var multiplier=Float(BinaryPrimitives.ReadUInt32LittleEndian(body[53..]));
        // cmp byte [eax],threshold; jb; mov eax,[difficulty]; fld default;
        // dec eax; cmp eax,3; ja; jmp [eax*4+table].
        var progressAt=-1;
        // Other native consumers reuse the same progress switch. Follow only
        // this time caller's bounded continuation, not unrelated functions.
        for(var i=at+65;i<=Math.Min(image.Length-29,at+0x200);i++)
        {
            var code=image.AsSpan(i,29);
            if(code[0]!=0x80||code[1]!=0x38||code[3]!=0x72||code[5]!=0xA1||
                !code.Slice(10,2).SequenceEqual<byte>([0xD9,5])||
                !code.Slice(16,5).SequenceEqual<byte>([0x48,0x83,0xF8,3,0x77])||
                !code.Slice(22,3).SequenceEqual<byte>([0xFF,0x24,0x85]))continue;
            if(progressAt>=0)throw new InvalidDataException("Ambiguous original automatic-level progress switch.");
            progressAt=i;
        }
        if(progressAt<0)throw new InvalidDataException("Original automatic-level progress switch unavailable.");
        var progress=image.AsSpan(progressAt,29);
        var fallback=Float(BinaryPrimitives.ReadUInt32LittleEndian(progress[12..]));
        var table=BinaryPrimitives.ReadUInt32LittleEndian(progress[25..]);
        var additions=Enumerable.Repeat(fallback,6).ToArray();
        for(var i=0;i<4;i++)
        {
            var target=BinaryPrimitives.ReadUInt32LittleEndian(At(table+(uint)i*4,4));
            var code=At(target,8);
            if(!code[..4].SequenceEqual<byte>([0xDD,0xD8,0xD9,5]))
                throw new InvalidDataException("Unknown original automatic-level progress addition.");
            additions[i]=Float(BinaryPrimitives.ReadUInt32LittleEndian(code[4..]));
        }
        if(!float.IsFinite(divisor)||divisor<=0||!float.IsFinite(limit)||!float.IsFinite(multiplier)||
            additions.Any(value=>!float.IsFinite(value)))throw new InvalidDataException("Invalid original automatic-level operands.");
        return new(new(divisor,limit,multiplier,progress[2],additions));

        int Unique(ReadOnlySpan<byte> pattern)
        {
            var index=image.AsSpan().IndexOf(pattern);
            if(index<0||image.AsSpan(index+pattern.Length).IndexOf(pattern)>=0)
                throw new InvalidDataException("Original automatic-level time caller missing or ambiguous.");
            return index;
        }
        float Float(uint address)=>BinaryPrimitives.ReadSingleLittleEndian(At(address,4));
        ReadOnlySpan<byte> At(uint address,int count)
        {
            if(address<imageBase)throw new InvalidDataException("Original operand address precedes its image.");
            var rva=(ulong)address-imageBase;
            foreach(var section in headers.SectionHeaders)
            {
                if(rva<(uint)section.VirtualAddress)continue;
                var relative=rva-(uint)section.VirtualAddress;
                if(relative+(uint)count>(uint)section.SizeOfRawData)continue;
                var offset=(ulong)(uint)section.PointerToRawData+relative;
                if(offset+(uint)count>(ulong)image.Length)break;
                return image.AsSpan(checked((int)offset),count);
            }
            throw new InvalidDataException("Original automatic-level operand lies outside stored sections.");
        }
    }

    public static SacredCreatureAutomaticLevelDefinitions Load(string directory)
    {
        Exception? failure=null;
        foreach(var name in new[]{"Sacred.exe","pureHD.exe"})
        {
            var path=Path.Combine(directory,name);if(!File.Exists(path))continue;
            try{return Read(File.ReadAllBytes(path));}catch(InvalidDataException error){failure=error;}
        }
        throw new InvalidDataException("Original automatic creature level definitions unavailable.",failure);
    }
}
