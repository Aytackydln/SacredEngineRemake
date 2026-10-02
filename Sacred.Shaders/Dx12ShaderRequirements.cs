using Vortice.Direct3D12;

namespace Sacred.Shaders;

/// <summary>Checks DXIL requirements before creating shader pipelines.</summary>
public static class Dx12ShaderRequirements
{
    public static void Validate(ID3D12Device device, bool directlyIndexedTextures = false)
    {
        var model = new FeatureDataShaderModel { HighestShaderModel = ShaderModel.Model6_6 };
        if (!device.CheckFeatureSupport(Feature.ShaderModel, ref model) ||
            model.HighestShaderModel < ShaderModel.Model6_6)
            throw new NotSupportedException(
                "The Direct3D 12 renderer requires Shader Model 6.6. Update the graphics driver or use the terminal software renderer.");

        if (directlyIndexedTextures &&
            device.CheckFeatureSupport<FeatureDataD3D12Options>().ResourceBindingTier < ResourceBindingTier.Tier3)
            throw new NotSupportedException(
                "Direct texture heap indexing requires resource binding tier 3. Use the terminal software renderer on this device.");
    }
}
