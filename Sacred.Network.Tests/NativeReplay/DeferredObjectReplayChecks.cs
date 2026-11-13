using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Assets.Paks.Weapon;
using Sacred.Assets.World;
using Sacred.Core.Pak.Items;
using Sacred.Core.World;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class DeferredObjectReplayChecks
{
    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-deferred-object-replay.json")));
        var checks = 0;
        foreach (var sample in native.RootElement.GetProperty("cases").EnumerateArray())
        {
            var pending = ReadQueue(Convert.FromHexString(sample.GetProperty("records_hex").GetString()!));
            foreach (var step in sample.GetProperty("steps").EnumerateArray())
            {
                var name = Encoding.Latin1.GetString(Convert.FromHexString(step.GetProperty("name_hex").GetString()!));
                var initial = step.GetProperty("initial");
                Check((pending.FindForCreation(name) is not null) == step.GetProperty("found").GetBoolean(), "nonmutating lookup");
                var found = pending.TryConsumeForCreation(name, new(initial[0].GetInt32(), initial[1].GetInt32()), out var result);
                Check(found == step.GetProperty("found").GetBoolean(), "native C-locale first match and miss");
                Check(result == new WorldScriptObjectCreationState(step.GetProperty("result")[0].GetInt32(),
                    step.GetProperty("result")[1].GetInt32()), "FFFF preserves Int32, other UInt16 halves zero-extend");
                var expected = ReadQueue(Convert.FromHexString(step.GetProperty("records_hex").GetString()!));
                Check(pending.Records.SequenceEqual(expected.Records), "native swap removal and repeated lookup order");
                var normalized = pending.ToNormalizedSaveBlock();
                Check(normalized.SequenceEqual(Convert.FromHexString(step.GetProperty("normalized_save_hex").GetString()!)),
                    "original count and contiguous writer after explicit post-NUL normalization");
                var raw = Convert.FromHexString(step.GetProperty("save_hex").GetString()!);
                Check(raw.Length == normalized.Length && raw.AsSpan(0, 4).SequenceEqual(normalized.AsSpan(0, 4)), "save count and size");
                for (var i = 0; i < pending.Records.Count; i++)
                {
                    var row = pending.Records[i]; var offset = 4 + i * 68;
                    Check(raw.AsSpan(offset, row.Name.Length + 1).SequenceEqual(normalized.AsSpan(offset, row.Name.Length + 1)) &&
                        raw.AsSpan(offset + 64, 4).SequenceEqual(normalized.AsSpan(offset + 64, 4)), "normalization preserves all name/state bytes");
                    if (row.HasCompleteBytes) Check(row.ToPayload().SequenceEqual(row.ToNormalizedPayload()), "63-byte name has no unknown tail");
                    else Reject(() => row.ToPayload(), "short name keeps raw serialization guarded");
                }
            }
        }
        foreach (var sample in native.RootElement.GetProperty("sources").EnumerateArray())
        {
            var pending = ReadQueue(Convert.FromHexString(sample.GetProperty("pending_hex").GetString()!));
            var rawSave = Convert.FromHexString(sample.GetProperty("private_save_hex").GetString()!);
            Check(BinaryPrimitives.ReadUInt32LittleEndian(rawSave) == pending.Records.Count &&
                rawSave.Length == 4 + pending.Records.Count * 68, "private save uses pending queue count");
            var callbacks = Convert.FromHexString(sample.GetProperty("callbacks_hex").GetString()!);
            var body = new List<byte>();
            for (var i = 0; i < callbacks.Length; i += 68)
                body.AddRange(GoldObjectScriptCallback.FromPayload(callbacks.AsSpan(i, 68)).ToPayload());
            Check(body.SequenceEqual(Convert.FromHexString(sample.GetProperty("bank_section_hex").GetString()!)),
                "bank-2 section uses callback source independently of pending queue");
        }
        var invalid = new WorldScriptDeferredObjectStates();
        Check(!invalid.AppendMissing(new("embedded\0name", 0, 1)) && !invalid.AppendMissing(new("\u0100", 0, 1)),
            "managed names cannot introduce nonnative embedded NUL or replacement bytes");
        Reject(() => new WorldScriptDeferredObjectState("\u0100", 0, 1).ToNormalizedPayload(), "lossy Latin-1 serialization");
        Check(!invalid.AppendMissing(new("both unchanged", -1, -1)), "append does not manufacture imported sentinel-only rows");
        CheckCreationGuard();
        return checks;

        WorldScriptDeferredObjectStates ReadQueue(byte[] bytes)
        {
            var result = new WorldScriptDeferredObjectStates();
            for (var i = 0; i < bytes.Length; i += 68)
            {
                var name = Encoding.Latin1.GetString(bytes, i, bytes.AsSpan(i, 64).IndexOf((byte)0));
                Check(result.AppendMissing(new(name, BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i + 64)),
                    BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i + 66)))), "fixture row is appendable");
            }
            return result;
        }
        void CheckCreationGuard()
        {
            var templates = new byte[256]; BinaryPrimitives.WriteUInt32LittleEndian(templates, 0x084E5057);
            var archive = SacredObjectTemplateArchive.FromPayload(new Dictionary<uint, ItemsPakEntry>(), templates);
            var triggerBytes = new byte[268]; BinaryPrimitives.WriteUInt32LittleEndian(triggerBytes, 0x01475254);
            var triggers = new WorldScriptTriggerRegistry(SacredWorldTriggerArchive.FromPayload(triggerBytes), new());
            var references = new GoldObjectReferencePool(); references.Apply(GoldReferenceBatch.FromPayload(new byte[] { 17, 0, 0, 0 }));
            var objects = new WorldScriptObjectWorld(archive, references, SacredSectorIndex.FromPayload(new byte[256]).CreateGrid(), triggers, 1, 0, false);
            var context = new WorldScriptExecutionContext(new(), [], ReadOnlyMemory<byte>.Empty, true) { ObjectWorld = objects };
            context.DeferredObjectStates.AppendMissing(new("door", -1, 1));
            // Valid type/placement operands; construction must stop before template lookup/allocation.
            var command = new byte[28];
            BinaryPrimitives.WriteUInt16LittleEndian(command, 8);
            "\u0001door\0"u8.CopyTo(command.AsSpan(4));
            command[10] = 2; BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(11), 1);
            command[15] = 4; // TilePosition, three Int32 coordinates, all zero.
            BinaryPrimitives.WriteUInt16LittleEndian(command.AsSpan(2), (ushort)command.Length);
            try { new WorldScriptExecutor(context).ExecuteStartup(command); }
            catch (WorldScriptExecutionFailure error)
            {
                Check(error.InnerException is NotSupportedException && error.Message.Contains("queued open/lock"), "matching CreateObj cannot silently ignore queued target effects");
                Check(context.IsInvalidated && context.DeferredObjectStates.Records.Count == 1 && references.Available == 1 &&
                    objects.AllocationCounter.Value == 0 && objects.Objects.Count == 0, "guard preserves queue and avoids construction side effects");
                return;
            }
            throw new Exception("CreateObj consumed unsupported deferred effects.");
        }
        void Check(bool pass, string why) { if (!pass) throw new Exception("Native deferred replay: " + why); checks++; }
        void Reject(Action action, string why)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            throw new Exception("Accepted invalid deferred replay: " + why);
        }
    }
}
