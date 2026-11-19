using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Models;
using Sacred.Granny.Animation;

internal static class HostHeroModelChecks
{
    public static int Run(string root) => CompareAsync(root).GetAwaiter().GetResult();

    private static async Task<int> CompareAsync(string root)
    {
        var installation = Environment.GetEnvironmentVariable("SACRED_GAME_DIRECTORY") ?? @"E:\SteamLibrary\steamapps\common\Sacred Gold";
        using var proof = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-host-hero-model.json")));
        var data = proof.RootElement;
        var checks = 0;
        var item = ItemsPakArchive.Load(Path.Combine(installation, "pak", "Items.pak"))
            .Single(item => item.ItemIndex == data.GetProperty("resource").GetUInt32());
        Check(item.ModelName == data.GetProperty("model").GetString(), "original Items model name");
        Check(item.ModelDesc.TextureId == data.GetProperty("texture").GetUInt32(), "original Items texture");
        using var models = ModelsPakArchive.Load(Path.Combine(installation, "pak", "Models.pak"), Path.Combine(installation, "pak", "Models.tmp"));
        var selection = data.GetProperty("selection").GetByte();
        Check(models.TryGetModelMotionName(item.ModelName, selection, out var motion) && motion == data.GetProperty("animation").GetString(), "exact original numeric saved slot");
        var body = await models.LoadCharacterBaseModelAsync(item.ModelName, []);
        var animation = await models.LoadModelAnimationAsync(item.ModelName, selection)
            ?? throw new InvalidDataException("HostHeroModel: original selected motion unavailable.");
        Check(body.Skin is not null && body.Mesh is not null && body.DefaultAnimation is null, "CPU body before default/equipment animation");
        Check(OriginalHash(body.RawBytes) == data.GetProperty("model_sha256").GetString(), "same original model as native library probe");
        Check(OriginalHash((await models.LoadModelAsync(motion)).RawBytes) == data.GetProperty("animation_sha256").GetString(), "same original clip as native library probe");
        var skin = body.Skin!;
        var pose = new GrnPoseEvaluator(skin.SkinningData, animation).Evaluate(0);
        var bindPose = GrnBindPose.Create(skin.SkinningData);
        Check(bindPose.IsEvaluated && bindPose.Animation is null, "evaluated bind pose has no invented clip/control");
        foreach (var sample in data.GetProperty("samples").EnumerateArray())
        {
            Check(skin.Skeleton.TryFindBone(sample.GetProperty("bone").GetString()!, out var index), "native queried bone exists");
            var bone = skin.Skeleton.Bones[index];
            var local = sample.GetProperty("flags").GetUInt32() == 0x101;
            Matrix4x4 transform;
            if (sample.GetProperty("phase").GetString() == "bind")
            {
                transform = bindPose.BoneWorldTransforms[index];
                if (local && bone.ParentIndex != index)
                {
                    Check(Matrix4x4.Invert(bindPose.BoneWorldTransforms[bone.ParentIndex], out var inverse), "invertible bind parent");
                    transform *= inverse;
                }
            }
            else
            {
                transform = pose.BoneWorldTransforms[index];
                if (local && bone.ParentIndex != index)
                {
                    Check(Matrix4x4.Invert(pose.BoneWorldTransforms[bone.ParentIndex], out var inverse), "invertible animated parent");
                    transform *= inverse;
                }
            }
            var actual = transform.Translation;
            actual.Y = -actual.Y; // Original Granny model and animation reflection.
            var values = sample.GetProperty("position").EnumerateArray().Select(value => value.GetSingle()).ToArray();
            Check(Vector3.Distance(actual, new(values[0], values[1], values[2])) < .0003f,
                $"original {sample.GetProperty("phase")} {bone.Name} flags {sample.GetProperty("flags")}: {actual} / {string.Join(',', values)}");
        }
        var controls = data.GetProperty("controls");
        Check(controls.GetProperty("defaults")[0][1].GetDouble() == 0 &&
            controls.GetProperty("defaults")[2][1].GetDouble() == 1 && controls.GetProperty("defaults")[3][1].GetDouble() == 1,
            "new native control starts at zero with default rate/loop one");
        return checks;
        void Check(bool value, string reason) { if (!value) throw new InvalidDataException("HostHeroModel: " + reason); checks++; }
    }

    private static string OriginalHash(byte[] payload)
    {
        for (var index = 64; index + 8 < payload.Length; index++)
            if (BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(index)) == 0xCA5E0000 &&
                BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(index + 4)) is > 0 and <= 16)
                return Convert.ToHexStringLower(SHA256.HashData(payload.AsSpan(index - 64)));
        throw new InvalidDataException("HostHeroModel: original GRN main chunk absent.");
    }
}
