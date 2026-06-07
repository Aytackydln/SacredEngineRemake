namespace Sacred.Assets.Paks.Mixed;

public readonly record struct MixedCutoutRecord(
    uint MixedId,
    uint PieceIndex,
    string AtlasName,
    uint CutoutId,
    int Right,
    int Bottom,
    int Left,
    int Top,
    float Uv0,
    float Uv1,
    float Uv2,
    float Uv3);