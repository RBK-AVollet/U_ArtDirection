using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public partial class BenDayRendererFeature : ScriptableRendererFeature {
    [SerializeField] Shader bloomShader;
    [SerializeField] Shader compositeShader;

    Material bloomMaterial;
    Material compositeMaterial;
    
    BenDayBloomRenderPass benDayPass;
    
    /// <inheritdoc/>
    public override void Create() {
        bloomMaterial = CoreUtils.CreateEngineMaterial(bloomShader);
        compositeMaterial = CoreUtils.CreateEngineMaterial(compositeShader);
        
        benDayPass = new BenDayBloomRenderPass(bloomMaterial, compositeMaterial);

        // Configures where the render pass should be injected.
        benDayPass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(benDayPass);
    }

    protected override void Dispose(bool disposing) {
        CoreUtils.Destroy(bloomMaterial);
        CoreUtils.Destroy(compositeMaterial);
    }
}
