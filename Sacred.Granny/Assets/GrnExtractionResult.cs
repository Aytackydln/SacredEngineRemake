using Sacred.Granny.Meshes;

namespace Sacred.Granny.Assets;

public readonly record struct GrnExtractionResult(
    Mesh? Mesh,
    GrnModelDiagnostics Diagnostics);
