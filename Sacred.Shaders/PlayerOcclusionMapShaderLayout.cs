namespace Sacred.Shaders;

public static class PlayerOcclusionMapShaderLayout
{
    public const int SceneConstantsCount = PlayerOcclusionMapSceneConstants.FloatCount;
    public const int SceneConstantsRegister = 0; // HLSL: register(b0)
    public const int SceneConstantsRootParameter = 0;
}
