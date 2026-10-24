using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

internal static class WorldStartupChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
        void Reject(Action action, string name)
        {
            try { action(); } catch (InvalidDataException) { checks++; return; }
            throw new Exception("Accepted invalid world startup: " + name);
        }
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-world-startup.json")));
        foreach (var send in native.RootElement.GetProperty("sends").EnumerateArray())
        {
            var opcode = send.GetProperty("opcode").GetUInt16();
            var message = opcode switch
            {
                0x0F => GoldWorldEntryCodec.CreatePeerReady(3),
                0x15 => GoldWorldEntryCodec.CreateParticipantUpload(3),
                0x16 => GoldWorldEntryCodec.CreateHostUpload(3),
                0x17 => GoldWorldEntryCodec.CreateParticipantStart(3),
                0x18 => GoldWorldEntryCodec.CreateHostStart(3, 0x12345678),
                _ => throw new Exception("Unknown native fixture")
            };
            Check(message is { Sender: 3, Origin: 3, Sequence: 0 } && message.Opcode == opcode &&
                message.Payload.SequenceEqual(Convert.FromHexString(send.GetProperty("payload_hex").GetString()!)),
                "native startup send argument payload");
        }
        foreach (var slot in new byte[] { 0, 17, 255 })
        foreach (var create in new Func<byte, GoldMessage>[] { GoldWorldEntryCodec.CreateParticipantUpload,
                     GoldWorldEntryCodec.CreateHostUpload, GoldWorldEntryCodec.CreateParticipantStart,
                     GoldWorldEntryCodec.CreatePeerReady, s => GoldWorldEntryCodec.CreateHostStart(s, uint.MaxValue) })
        {
            try { create(slot); throw new Exception("Invalid startup sender accepted"); }
            catch (ArgumentOutOfRangeException) { checks++; }
        }
        var approval = new GoldMessage(0x11, 0, 0, 1, [4, 3, 0, 0, 0, 0]);
        foreach (var role in native.RootElement.GetProperty("roles").EnumerateArray())
        {
            var tracker = new GoldAdmissionTracker(); tracker.Begin();
            tracker.ProcessBatch([approval with { Payload = [role.GetProperty("flags").GetByte(), 3, 0, 0, 0, 0] }]);
            Check(tracker.Status.WorldStartup.AssignedClientHost == role.GetProperty("assigned_host").GetBoolean() &&
                tracker.Status.WorldStartup.HostWorldAvailable == role.GetProperty("host_world_available").GetBoolean() &&
                !tracker.Status.WorldStartup.ParticipantStartApproved, "native admission role bits");
        }
        var hostAvailable = new GoldMessage(0x18, 0, 1, 2, []);
        var approved = new GoldMessage(0x17, 0, 0, 3, []);
        Check(GoldGameEventDecoder.Decode(hostAvailable) is GoldHostWorldAvailableEvent &&
            GoldGameEventDecoder.Decode(approved) is GoldParticipantStartApprovedEvent, "directional server startup acknowledgements");
        Check(GoldGameEventDecoder.Decode(GoldWorldEntryCodec.CreateParticipantStart(3)) is GoldParticipantStartRequestEvent &&
            GoldGameEventDecoder.Decode(GoldWorldEntryCodec.CreateHostStart(3, uint.MaxValue)) is GoldHostStartRequestEvent { Word: uint.MaxValue },
            "client startup requests distinct from server acknowledgements");
        foreach (var opcode in new ushort[] { 0x0F, 0x15, 0x16, 0x17, 0x18 })
            Reject(() => GoldGameEventDecoder.Decode(new(opcode, 0, 0, 0, [0])), "empty startup control length");
        foreach (var length in new[] { 0, 1, 2, 3, 5 })
            Reject(() => GoldGameEventDecoder.Decode(new(0x18, 1, 1, 0, new byte[length])), "host start request length");

        foreach (var guard in native.RootElement.GetProperty("sector_guards").EnumerateArray())
        {
            // Span has no native null pointer distinction; test the pointer-present cases only.
            if (!guard.GetProperty("pointer_present").GetBoolean()) continue;
            var length = guard.GetProperty("length").GetUInt32();
            if (length > 1000) continue;
            if (guard.GetProperty("native_accepted").GetBoolean())
                Check(GoldSectorEntry.FromPayload(new byte[length]).Count == 9, "native exact sector-entry length");
            else Reject(() => GoldSectorEntry.FromPayload(new byte[length]), "native rejected sector-entry length");
        }
        foreach (var sample in native.RootElement.GetProperty("sector_samples").EnumerateArray())
        {
            var bytes = Convert.FromHexString(sample.GetProperty("payload_hex").GetString()!);
            var entry = GoldSectorEntry.FromPayload(bytes);
            Check(entry.Word0 == BinaryPrimitives.ReadUInt32LittleEndian(bytes) &&
                entry.Word58 == BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(58)), "sector entry retained end words");
            var index = 0;
            foreach (var row in sample.GetProperty("rows").EnumerateArray())
            {
                var slot = entry.GetSlot(index++);
                Check(slot.Sector == row.GetProperty("sector").GetUInt16() &&
                    slot.RetainedWord == row.GetProperty("retained_word").GetUInt16() &&
                    slot.ScriptMode == row.GetProperty("script_mode").GetByte() &&
                    slot.InitializeFlag == row.GetProperty("initialize_flag").GetByte() &&
                    slot.IsPresent == row.GetProperty("native_present").GetBoolean(), "native sector slot byte mapping");
                Check(slot.RequestsScripts == (slot.IsPresent && slot.ScriptMode != 0) &&
                    slot.RequestsInitialization == (slot.IsPresent && slot.InitializeFlag != 0), "nonzero flags and absent-sector guard");
            }
            var copy = bytes.ToArray(); bytes[0] ^= 1;
            var exported = entry.ToPayload(); exported[4] ^= 1;
            Check(entry.ToPayload().SequenceEqual(copy), "sector input/output isolation");
            foreach (var invalidIndex in new[] { -1, 9, int.MaxValue })
            {
                try { entry.GetSlot(invalidIndex); throw new Exception("Invalid sector slot accepted"); }
                catch (ArgumentOutOfRangeException) { checks++; }
            }
        }
        var sectorMessage = new GoldMessage(0x148, 0, 0, 4, new byte[62]);
        var session = new GoldAdmissionTracker(); session.Begin();
        Reject(() => session.ProcessBatch([hostAvailable]), "world startup before slot assignment");
        Reject(() => session.ProcessBatch([approval, hostAvailable, sectorMessage with { Payload = new byte[63] }]), "malformed late startup rolls back admission");
        Check(session.Status.Phase == GoldAdmissionPhase.WaitingForResponse && !session.Status.WorldStartup.HostWorldAvailable,
            "admission and startup commit together");
        session.ProcessBatch([approval with { Payload = [0, 3, 0, 0, 0, 0] }]);
        var before = session.Status;
        Reject(() => session.ProcessBatch([hostAvailable, sectorMessage, approved with { Origin = 17 }]), "invalid retained origin");
        Reject(() => session.ProcessBatch([hostAvailable, sectorMessage with { Sender = 1, Origin = 2 }]), "sector interest with mismatched client origin");
        Check(ReferenceEquals(before, session.Status), "late forged startup leaves state unchanged");
        session.ProcessBatch([GoldWorldEntryCodec.CreateParticipantStart(3), GoldWorldEntryCodec.CreateHostStart(1, 123)]);
        Check(!session.Status.WorldStartup.ParticipantStartApproved && !session.Status.WorldStartup.HostWorldAvailable,
            "client requests cannot approve startup");
        session.ProcessBatch([sectorMessage with { Sender = 1, Origin = 1 }]);
        Check(session.Status.WorldStartup.SectorEntryCount == 0, "client sector interest cannot mark a reply milestone");
        session.ProcessBatch([hostAvailable, sectorMessage, approved]);
        var first = session.Status.WorldStartup;
        session.ProcessBatch([sectorMessage]);
        Check(first is { HostWorldAvailable: true, ParticipantStartApproved: true, SectorEntryCount: 1 } &&
            session.Status.WorldStartup.SectorEntryCount == 2 && first.LastSectorEntry!.ToPayload().Length == 62,
            "native retained host origin, approval and immutable sector counts");
        var receiveControl = GoldGameEventDecoder.Decode(new(0x20, 0, 0, 0, []));
        Check(receiveControl is GoldSessionReceiveControlEvent { Enabled: true }, "0x20 sets the native singleton receive byte");
        var startupBeforeControl = session.Status.WorldStartup;
        session.ProcessBatch([receiveControl.Message]);
        Check(session.Status.WorldStartup == startupBeforeControl, "0x20 does not change startup milestones");
        using var capture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-world-startup-capture.json")));
        foreach (var control in capture.RootElement.GetProperty("controls").EnumerateArray())
        {
            var opcode = Convert.ToUInt16(control.GetProperty("opcode").GetString()![2..], 16);
            var payload = Convert.FromHexString(control.GetProperty("payload_hex").GetString()!);
            var decoded = GoldGameEventDecoder.Decode(new(opcode, control.GetProperty("Sender").GetByte(),
                control.GetProperty("Origin").GetByte(), 0, payload));
            // This capture's managed type annotation predates the 01F/020 singleton mapping.
            var expectedType = opcode is 0x1f or 0x20 ? nameof(GoldSessionReceiveControlEvent) : control.GetProperty("decoded_type").GetString();
            Check(decoded.GetType().Name == expectedType &&
                payload.Length == control.GetProperty("length").GetInt32(), "captured native startup shape and direction");
            if (decoded is GoldSectorEntryEvent sectors)
                Check(sectors.Entry.ToPayload().SequenceEqual(payload), "captured native sector entry lossless");
            if (decoded is GoldSectorInterestEvent interest)
                Check(interest.Request.ToPayload().SequenceEqual(payload), "captured native sector interest lossless");
        }
        Console.WriteLine($"PASS: {checks} world startup and sector-entry checks");
        return checks;
    }
}
