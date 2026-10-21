namespace Sacred.GameplayHost.World;

/// <summary>Gold EiMagic 0x51E110 local event 23: TYPE_FX_CAST_SPELL selector and bone.
/// Names are native attachment table entries 0/21/22.</summary>
public static class GameplayCastAttachments
{
    private static readonly GameplayCastAttachment[][] Left = Create(false, false);
    private static readonly GameplayCastAttachment[][] Right = Create(true, false);
    private static readonly GameplayCastAttachment[][] Both = Create(false, true);
    private static readonly GameplayCastAttachment[] Seraphim = [new(8, "Bip01 R Finger3"), new(8, "Bip01 L Finger3")];
    private static readonly GameplayCastAttachment[] Daemon = [new(14, "Bip01 L Finger3"), new(14, "Bip01 R Finger3"), new(22, "__Root")];
    private static readonly GameplayCastAttachment[] Additional = [new(24, "Bip01 R Finger3"), new(24, "Bip01 L Finger3")];

    public static ReadOnlySpan<GameplayCastAttachment> Select(uint code) => code switch
    {
        3 => Both[3],
        5 or 29 => Left[9],
        6 => Left[0],
        11 => Right[9],
        13 => Both[1],
        15 => Right[6],
        17 or 20 or 21 or 22 => Seraphim,
        18 => Right[7],
        23 or 43 => Left[5],
        24 => Left[6],
        26 => Left[1],
        35 or 41 => Both[5],
        36 => Both[10],
        37 => Both[11],
        39 => Left[2],
        40 => Right[4],
        52 => Both[12],
        60 => Daemon,
        61 or 62 or 63 or 64 or 65 or 67 => Both[14],
        66 => Both[16],
        72 => Additional,
        _ => []
    };

    private static GameplayCastAttachment[][] Create(bool right, bool both) => Enumerable.Range(0, 25)
        .Select(preset => both ? new GameplayCastAttachment[] { new(preset, "Bip01 L Finger3"), new(preset, "Bip01 R Finger3") }
            : [new GameplayCastAttachment(preset, right ? "Bip01 R Finger3" : "Bip01 L Finger3")]).ToArray();
}

public readonly record struct GameplayCastAttachment(int Preset, string Bone);
