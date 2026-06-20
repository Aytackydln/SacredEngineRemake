namespace Sacred.Granny;

public sealed record GrnCharacterExtractionResult(
    Mesh? Mesh,
    GrnMeshSkin? Skin,
    GrnModelDiagnostics Diagnostics);
