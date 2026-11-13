using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Network.Gameplay;

internal static class MagicPrisonSnapshotChecks
{
    public static int Run(string root)
    {
        using var source=Read(root);var checks=0;
        foreach(var row in source.RootElement.GetProperty("cases").EnumerateArray())
        {
            Check(row.GetProperty("rng_calls").GetArrayLength()==0&&row.GetProperty("rng_after").GetUInt32()==row.GetProperty("seed").GetUInt32(),"actual constructor and caller consume no RNG");
            foreach(var publication in row.GetProperty("publications").EnumerateArray())
            {
                var bytes=Convert.FromHexString(publication.GetProperty("payload_hex").GetString()!);var state=GoldObjectFxState.FromPayload(bytes);
                Check(state.BaseState.ObjectType==854&&state.FxType==78&&state.IsSystemActive&&state.ToPayload().AsSpan().SequenceEqual(bytes),"complete native FX state is retained");
                var raw=state.ParticleState.ToArray();var expected=raw.ToArray();var snapshot=GoldParticleSnapshot.FromPayload(raw);
                Check(!snapshot.HasDerivedMarker&&snapshot.DerivedState.Length==GoldMagicPrisonParticleLayout.DerivedSize&&snapshot.Resource==854,"native size-only derived framing and base resource");
                Check(snapshot.ToPayload().AsSpan().SequenceEqual(expected),"every transmitted byte retained, including caller backing");
                Array.Fill(raw,(byte)0xCC);Check(snapshot.ToPayload().AsSpan().SequenceEqual(expected),"particle payload is owned");
                foreach(var size in new[]{0,3,27,1191})
                {
                    try{_=GoldParticleSnapshot.FromPayload(expected.AsSpan(0,size));throw new Exception("Truncated unmarked snapshot accepted");}catch(InvalidDataException){checks++;}
                }
                BinaryPrimitives.WriteUInt32LittleEndian(expected,uint.MaxValue);
                try{_=GoldParticleSnapshot.FromPayload(expected);throw new Exception("Oversized snapshot accepted");}catch(InvalidDataException){checks++;}
            }
        }
        return checks;
        void Check(bool pass,string why){if(!pass)throw new Exception("MAGICPRISON snapshot: "+why);checks++;}
    }
    public static byte[] WireExample(string root)
    {
        using var source=Read(root);var row=source.RootElement.GetProperty("cases").EnumerateArray().First(value=>value.GetProperty("mode").GetString()=="creation").GetProperty("publications")[0];
        return Convert.FromHexString(row.GetProperty("prefix_hex").GetString()!).Concat(Convert.FromHexString(row.GetProperty("payload_hex").GetString()!)).ToArray();
    }
    private static JsonDocument Read(string root)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"docs/game-server/evidence/native-magic-prison-snapshots.json")));
}
