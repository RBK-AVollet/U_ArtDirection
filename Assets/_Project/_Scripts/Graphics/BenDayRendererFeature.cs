using Antoine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class BenDayRendererFeature : ScriptableRendererFeature {
    class BenDayPass : ScriptableRenderPass {
        const string m_PassName = "BenDayPass";
        Material m_bloomMaterial;
        Material m_compositeMaterial;
        
        // This class stores the data needed by the RenderGraph pass.
        // It is passed as a parameter to the delegate function that executes the RenderGraph pass.
        class PassData {
        }
        
        public void Setup(Material bloomMat, Material compositeMat) {
            m_bloomMaterial = bloomMat;
            m_compositeMaterial = compositeMat;

            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            requiresIntermediateTexture = true;
        }
        
        // RecordRenderGraph is where the RenderGraph handle can be accessed, through which render passes can be added to the graph.
        // FrameData is a context container through which URP resources can be accessed and managed.
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
            var stack = VolumeManager.instance.stack;
            var customEffect = stack.GetComponent<BenDayBloomEffectComponent>();

            if (!customEffect.IsActive())
                return;

            var resourceData = frameData.Get<UniversalResourceData>();

            if (resourceData.isActiveTargetBackBuffer) {
                Debug.LogError($"Skipping render pass. ben day bloom effect requires an intermediate ColorTexture, we can't use the BackBuffer as a target input.");
                return;
            }

            var source = resourceData.activeColorTexture;
            
            var destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = $"CameraColor-{m_PassName}";
            destinationDesc.clearBuffer = false;

            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            RenderGraphUtils.BlitMaterialParameters para = new(source, destination, m_bloomMaterial, 0);
            renderGraph.AddBlitPass(para, passName: m_PassName);
            
            resourceData.cameraColor = destination;
        }
    }

    [SerializeField] Shader bloomShader;
    [SerializeField] Shader compositeShader;

    Material bloomMaterial;
    Material compositeMaterial;
    
    BenDayPass m_pass;

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
        m_pass.Setup(bloomMaterial, compositeMaterial);
        renderer.EnqueuePass(m_pass);
    }
    
    /// <inheritdoc/>
    public override void Create() {
        bloomMaterial = CoreUtils.CreateEngineMaterial(bloomShader);
        compositeMaterial = CoreUtils.CreateEngineMaterial(compositeShader);
        
        m_pass = new BenDayPass();
    }

    protected override void Dispose(bool disposing) {
        CoreUtils.Destroy(bloomMaterial);
        CoreUtils.Destroy(compositeMaterial);
    }
}
