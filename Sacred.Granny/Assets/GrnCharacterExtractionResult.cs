using Sacred.Granny.Animation;
using Sacred.Granny.Meshes;

namespace Sacred.Granny.Assets;

public sealed record GrnCharacterExtractionResult(
    Mesh? Mesh,
    GrnMeshSkin? Skin,
    GrnModelDiagnostics Diagnostics);
