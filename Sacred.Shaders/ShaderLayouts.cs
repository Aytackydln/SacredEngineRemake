namespace Sacred.Shaders;

public static class ModelShaderLayout
{
    public const int RootParameterCount = 4;

    public const int ModelConstantsRegister = 0; // HLSL: register(b0)
    public const int SceneConstantsRegister = 1; // HLSL: register(b1)
    public const int ModelTextureRegister = 0; // HLSL: register(t0)
    public const int ModelOverlayTextureRegister = 1; // HLSL: register(t1)
    public const int ModelSamplerRegister = 0; // HLSL: register(s0)

    public const int ModelConstantsRootParameter = 0;
    public const int ModelTextureRootParameter = 1;
    public const int ModelOverlayTextureRootParameter = 2;
    public const int SceneConstantsRootParameter = 3;

    public const int ModelConstantsCount = ModelShaderModelConstants.FloatCount;
    public const int ModelBaseConstantsOffset = 0;
    public const int ModelBaseConstantsCount = 36;
    public const int TextureFlagsOffset = 36;
    public const int TextureFlagsConstantsCount = ModelShaderTextureFlags.FloatCount;
    public const float PreserveProjectedDepth = -1.0f;

    public const int SceneConstantsCount = ModelShaderSceneConstants.FloatCount;
}

public static class StaticSpriteShaderLayout
{
    public const int SceneConstantsCount = StaticSpriteSceneConstants.FloatCount;

    public const int SceneConstantsRegister = 0; // HLSL: register(b0)
    public const int InstanceBufferRegister = 0; // HLSL: register(t0)
    public const int FirstTextureRegister = 1; // HLSL: register(t1)

    public const int SceneConstantsRootParameter = 0;
    public const int InstanceBufferRootParameter = 1;
    public const int TextureTableRootParameter = 2;
}

public static class WorldQuadShaderLayout
{
    public const int RootConstantsRegister = 0; // HLSL: register(b0)
    public const int RootConstantsRootParameter = 0;
    public const int TextureRootParameter = 1;
    public const int RootConstantsCount = WorldQuadShaderConstants.FloatCount;
}
