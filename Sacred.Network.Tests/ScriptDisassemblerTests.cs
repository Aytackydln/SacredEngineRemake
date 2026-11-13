using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;

namespace Sacred.Network.Tests;

[TestClass]
public sealed class ScriptDisassemblerTests
{
    [TestMethod]
    public void EditorExampleDecodesObjectAndPreservesBytes()
    {
        var command = Command(8, "02501400000426050000EB05000002000000034A012963686573745472696767657200");
        Assert.AreEqual("CreateObj SetType=5200 SetPos=1318,1515,2 SetDir=330 UseTrigger=\"chestTrigger\"",
            SacredScriptDisassembler.Format(command));
        AssertPreserved(command);
    }

    [TestMethod]
    public void SymbolicPositionsAndResourceBytesAreEscaped()
    {
        Assert.AreEqual("Teleport SetPos=@\"home\"", SacredScriptDisassembler.Format(Command(46, "04FEFFFFFF686F6D6500")));
        Assert.AreEqual("PlaySound SetName=\"a\\\"\\\\\\x0A\\xE4\"", SacredScriptDisassembler.Format(Command(104, "0161225C0AE400")));
    }

    [TestMethod]
    public void GoldTriggerScopeConsumesOneByteAndContinues()
    {
        Assert.AreEqual("CreateTrigger SetName=\"a\" TriggerType=1 TrgOpen",
            SacredScriptDisassembler.Format(Command(48, "016100280126")));
    }

    [TestMethod]
    public void UnknownAndTruncatedOperandsKeepTheirEntireSuffix()
    {
        foreach (var payload in new[] { "016100FF012600", "016100042600", "016100016263", "0BFFFFFFFF76617200", "48FFFFFFFF76617200" })
        {
            var command = Command(0xFFFF, payload);
            var operands = SacredScriptDisassembler.ReadOperands(command);
            Assert.IsTrue(operands[^1].IsRaw);
            Assert.IsTrue(SacredScriptDisassembler.Format(command).StartsWith("op_0xFFFF ", StringComparison.Ordinal));
            AssertPreserved(command);
        }
    }

    [TestMethod]
    public void FailureIncludesAuthoredOperandsAndCallPathWithoutMaskingCause()
    {
        var cause = new InvalidDataException("Unsupported construction");
        var failure = new WorldScriptExecutionFailure("FunkCode.bin", Command(8, "01646F6F72000256030000"), ["QIS_OnSetUp"], cause);
        StringAssert.Contains(failure.Message, "CreateObj SetName=\"door\" SetType=854");
        StringAssert.Contains(failure.Message, "calls: QIS_OnSetUp");
        Assert.AreSame(cause, failure.InnerException);
        var malformed = new WorldScriptExecutionFailure("StartCode.bin", new(0, new byte[4]), [], cause);
        StringAssert.Contains(malformed.Message, "raw=00000000");
    }

    [TestMethod]
    public void InvalidHeaderIsRejected()
    {
        Assert.ThrowsException<InvalidDataException>(() => SacredScriptDisassembler.Format(new(0, new byte[3])));
        Assert.ThrowsException<InvalidDataException>(() => SacredScriptDisassembler.Format(new(0, new byte[4])));
    }

    [TestMethod, TestCategory("Installation")]
    public void InstalledCampaignScriptsRetainEveryOperandByte()
    {
        var game = Environment.GetEnvironmentVariable("SACRED_GOLD_PATH") ?? @"E:\SteamLibrary\steamapps\common\Sacred Gold";
        if (!Directory.Exists(game)) Assert.Inconclusive("Sacred Gold installation is unavailable.");
        var files = Directory.EnumerateFiles(Path.Combine(game, "bin"), "*Code.bin", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) is "StartCode.bin" or "FunkCode.bin").ToArray();
        Assert.IsTrue(files.Length > 0);
        var commands = 0;
        var raw = 0;
        foreach (var file in files)
        foreach (var command in SacredCompiledScriptReader.Read(File.ReadAllBytes(file)))
        {
            AssertPreserved(command);
            Assert.IsFalse(string.IsNullOrWhiteSpace(SacredScriptDisassembler.Format(command)));
            raw += SacredScriptDisassembler.ReadOperands(command).Count(value => value.IsRaw);
            commands++;
        }
        Console.WriteLine($"Inspected {files.Length} original code files, {commands} instructions, {raw} preserved raw suffixes.");
    }

    private static SacredScriptCommand Command(ushort opcode, string payload)
    {
        var body = Convert.FromHexString(payload);
        var bytes = new byte[body.Length + 4];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, opcode);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), checked((ushort)bytes.Length));
        body.CopyTo(bytes, 4);
        return new(0x1234, bytes);
    }

    private static void AssertPreserved(SacredScriptCommand command)
    {
        var offset = 4;
        foreach (var operand in SacredScriptDisassembler.ReadOperands(command))
        {
            Assert.AreEqual(offset, operand.Offset);
            Assert.IsTrue(operand.Bytes.Length > 0);
            Assert.IsTrue(command.Bytes.Span.Slice(offset, operand.Bytes.Length).SequenceEqual(operand.Bytes.Span));
            offset += operand.Bytes.Length;
        }
        Assert.AreEqual(command.Bytes.Length, offset);
    }
}
