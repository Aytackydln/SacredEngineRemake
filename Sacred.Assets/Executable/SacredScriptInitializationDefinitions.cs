using System.Buffers.Binary;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.Assets.Executable;

/// <summary>Original Gold interpreter reset defaults, before script execution.
/// Reads initializer instructions; no captured bank or fabricated default block.</summary>
public sealed class SacredScriptInitializationDefinitions
{
    private readonly byte[] _parameters;
    private SacredScriptInitializationDefinitions(byte[] parameters) => _parameters = parameters;
    public byte[] CreateParameterBlock() => _parameters.ToArray();
    public int[] CreateVariableGlobals() => new int[SacredScriptVariableGlobalsLayout.Count];
    /// <summary>Initial reset storage only. Later regional mutations must be applied separately.</summary>
    public byte[] CreateRegionReplacements() => new byte[SacredScriptRegionReplacementLayout.TableSize];

    public static SacredScriptInitializationDefinitions Read(ReadOnlySpan<byte> image)
    {
        // Operand bytes for the eight defaults are deliberately excluded from validation.
        ReadOnlySpan<byte> pattern = Convert.FromHexString(
            "8D9684760000B92000000033C08BFAF3ABB800000000B92000000089869476000089869876000089869C7600008986A076000083C8FF8DBEA4030000C70200000000C7868876000000000000C7868C76000000000000C7869076000000000000F3AB");
        var at = Unique(image,pattern[..17]);
        if (image.Length-at < pattern.Length) throw new InvalidDataException("Truncated original parameter reset.");
        var code = image.Slice(at,pattern.Length);
        for (var i=0;i<pattern.Length;i++)
        {
            if (i is >=18 and <22 or >=62 and <66 or >=72 and <76 or >=82 and <86 or >=92 and <96) continue;
            if (code[i]!=pattern[i]) throw new InvalidDataException("Unknown original parameter initializer instructions.");
        }
        ReadOnlySpan<byte> clear = Convert.FromHexString(
            "B93219000033C08DBE48100000885E11885E10885E13885E12885E14885E15C70588E09400FFFFFFFFF3ABB9960000008DBE50770000F3AB");
        var resetAt=Unique(image,clear[..33]);
        if(image.Length-resetAt<clear.Length)throw new InvalidDataException("Truncated original global reset.");
        var reset=image.Slice(resetAt,clear.Length);
        for(var i=0;i<clear.Length;i++)
        {
            // The absolute unrelated reset-flag address relocates in the installed alternative executable.
            if(i is >=33 and <37)continue;
            if(reset[i]!=clear[i])throw new InvalidDataException("Unknown original global reset instructions.");
        }
        var parameters = new byte[SacredScriptStartupParameterLayout.BankBlockSize];
        Write(0,BinaryPrimitives.ReadUInt32LittleEndian(code[62..]));
        for(var i=0;i<3;i++) Write((i+1)*4,BinaryPrimitives.ReadUInt32LittleEndian(code[(72+i*10)..]));
        var common = BinaryPrimitives.ReadUInt32LittleEndian(code[18..]);
        for(var i=4;i<8;i++) Write(i*4,common);
        return new(parameters);
        void Write(int offset,uint value) => BinaryPrimitives.WriteUInt32LittleEndian(parameters.AsSpan(offset),value);
    }
    private static int Unique(ReadOnlySpan<byte> image,ReadOnlySpan<byte> signature)
    {
        var found=image.IndexOf(signature);
        if(found<0 || image[(found+signature.Length)..].IndexOf(signature)>=0)
            throw new InvalidDataException("Original script reset definition missing or ambiguous.");
        return found;
    }
    public static SacredScriptInitializationDefinitions Load(string installation)
    {
        Exception? failure=null;
        foreach(var filename in new[]{"Sacred.exe","pureHD.exe"})
        {
            var path=Path.Combine(installation,filename);
            if(!File.Exists(path))continue;
            try{return Read(File.ReadAllBytes(path));}
            catch(InvalidDataException error){failure=error;}
        }
        throw new InvalidDataException("Original script initialization definitions unavailable.",failure);
    }
}
