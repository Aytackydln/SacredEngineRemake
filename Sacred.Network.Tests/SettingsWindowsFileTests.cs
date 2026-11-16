using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sacred.Assets.Executable;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Tests.Infrastructure;

namespace Sacred.Network.Tests;

[TestClass]
public sealed class SettingsWindowsFileTests
{
    [TestMethod, DataRow(false), DataRow(true)]
    public void NativeComparedFilePortWritesAndLoadsWorkspaceFixture(bool failDataAllocation)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file boundary test.");
        var root = RepositoryFiles.Root;
        var path = Path.Combine(root, "Sacred.Network.Tests", "TestResults", "settings-win32-" + Guid.NewGuid().ToString("N") + ".cfg");
        if (path.Any(value => value > 127)) Assert.Inconclusive("Fixture path needs explicit narrow-code-page bytes.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bufferEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-file-buffer.json")));
        using var saveEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-save.json")));
        using var cycleEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-file-roundtrip.json")));
        var buffer = SacredCrtBufferDefinitions.Read(ListenPortChecks.Image(bufferEvidence.RootElement));
        var initial = SacredCrtInitialFileStates.Read(ListenPortChecks.Image(bufferEvidence.RootElement));
        var image = ListenPortChecks.Image(saveEvidence.RootElement);
        var load = SacredSettingsFileDefinitions.Read(image); var save = SacredSettingsSaveDefinitions.Read(image);
        var example = cycleEvidence.RootElement.GetProperty("cases").EnumerateArray().First(row =>
            row.GetProperty("entries").GetArrayLength() == 3 && row.GetProperty("entries")[0][0].GetString() == "41" && row.GetProperty("delimiter_hex").GetString() == "3a");
        var pathBytes = Encoding.ASCII.GetBytes(path); var document = new SacredSettingsDocument(); document.SetPath(pathBytes);
        foreach (var entry in example.GetProperty("entries").EnumerateArray())
            document.Values.Insert(Convert.FromHexString(entry[0].GetString()!), Convert.FromHexString(entry[1].GetString()!));
        try
        {
            using var port = new WorldSettingsWindowsFilePort(buffer, size => !failDataAllocation || size != buffer.AllocationSize, initial.CommitFlags, initial.FileMode, initial.Umask);
            WorldSettingsFileSaver.Save(save, document, () => ":"u8.ToArray(), port.Writer);
            CollectionAssert.AreEqual(Convert.FromHexString(example.GetProperty("saved_hex").GetString()!), File.ReadAllBytes(path));
            WorldSettingsFileLoader.Load(load, document, pathBytes, ":"u8.ToArray(), port.Reader);
            CollectionAssert.AreEqual(example.GetProperty("after").EnumerateArray().Select(entry => entry[0].GetString() + ":" + entry[1].GetString()).ToArray(), SettingsStorageChecks.Snapshot(document.Values).ToArray());
            // Real missing-file open must retain the loader's destructive-clear behavior.
            WorldSettingsFileLoader.Load(load, document, Encoding.ASCII.GetBytes(path + ".missing"), ":"u8.ToArray(), port.Reader);
            Assert.AreEqual(0, document.Values.Count);
        }
        finally { File.Delete(path); }
    }
    [TestMethod, DataRow("r+"), DataRow("r+t"), DataRow("r+b"), DataRow("w+t"), DataRow("a+t"), DataRow("a+b")]
    public void UpdateOpenMatchesNativePhysicalTail(string mode)
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows file boundary test.");
        var root = RepositoryFiles.Root;
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-update.json")));
        using var bufferEvidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-settings-file-buffer.json")));
        var buffer = SacredCrtBufferDefinitions.Read(ListenPortChecks.Image(bufferEvidence.RootElement));
        var path = Path.Combine(root, "Sacred.Network.Tests", "TestResults", "settings-update-" + Guid.NewGuid().ToString("N") + ".cfg");
        if (path.Any(value => value > 127)) Assert.Inconclusive("Fixture path needs explicit narrow-code-page bytes.");
        var modeHex = Convert.ToHexString(Encoding.ASCII.GetBytes(mode));
        try
        {
            using var port = new WorldSettingsWindowsFilePort(buffer, _ => true, 0, 0x4000, 0x12);
            foreach (var row in native.RootElement.GetProperty("cases").EnumerateArray().Where(row =>
                row.GetProperty("mode_hex").GetString()!.Equals(modeHex, StringComparison.OrdinalIgnoreCase) &&
                row.GetProperty("spec").EnumerateObject().Count() == 1).Take(10))
            {
                File.WriteAllBytes(path, Convert.FromHexString(row.GetProperty("spec").GetProperty("content_hex").GetString()!));
                var handle = port.Reader.Open(Encoding.ASCII.GetBytes(path), Encoding.ASCII.GetBytes(mode));
                Assert.AreEqual(row.GetProperty("returned").GetBoolean(), handle != 0, mode);
                if (handle != 0) port.Reader.Close(handle);
                CollectionAssert.AreEqual(Convert.FromHexString(row.GetProperty("content_hex").GetString()!), File.ReadAllBytes(path), mode);
            }
        }
        finally { File.Delete(path); }
    }
}
