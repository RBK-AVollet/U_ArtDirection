using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

[Serializable, VolumeComponentMenu("Custom/Ben Day Bloom"),
 SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
public class BenDayBloomEffectComponent : VolumeComponent, IPostProcessComponent {
    [Header("Bloom Settings")] 
    public FloatParameter threshold = new FloatParameter(0.9f, true);
    public FloatParameter itensity = new FloatParameter(1, true);
    public ClampedFloatParameter scatter = new ClampedFloatParameter(0.7f, 0, 1, true);
    public IntParameter clamp = new IntParameter(65472, true);
    public ClampedIntParameter maxIterations = new ClampedIntParameter(6, 0, 10);
    public NoInterpColorParameter tint = new NoInterpColorParameter(Color.white);

    [Header("Benday")]
    public IntParameter dotsDensity = new IntParameter(10, true);
    public ClampedFloatParameter dotsCutoff = new ClampedFloatParameter(0.4f, 0, 1, true);
    public Vector2Parameter scrollDirection = new Vector2Parameter(new Vector2());
    
    public bool IsActive() {
        return true;
    }
}

public class BenDayBloomRenderPass : ScriptableRenderPass {
    Material bloomMaterial;
    Material compositeMaterial;

    const int k_maxPyramidSize = 16;
    int[] _BloomMipUp;
    int[] _BloomMipDown;
    TextureHandle[] m_BloomMipUp;
    TextureHandle[] m_BloomMipDown;
    GraphicsFormat hdrFormat;
    
    // This class stores the data needed by the RenderGraph pass.
    // It is passed as a parameter to the delegate function that executes the RenderGraph pass.
    class PassData {
        internal RenderTextureDescriptor cameraTargetDescriptor;
        internal TextureHandle colorTexture;
        internal TextureHandle depthTexture;
        internal Material bloomMaterial;
    }

    public BenDayBloomRenderPass(Material bloomMaterial, Material compositeMaterial) {
        bloomMaterial = bloomMaterial;
        compositeMaterial = compositeMaterial;
    }

    // This static method is passed as the RenderFunc delegate to the RenderGraph render pass.
    // It is used to execute draw commands.
    static void ExecutePass(PassData data, RasterGraphContext context) {
        VolumeStack stack = VolumeManager.instance.stack;
        var benDayBloom = stack.GetComponent<BenDayBloomEffectComponent>();
        
        using (new ProfilingScope(context.cmd, new ProfilingSampler("Custom Ben Day"))) {
            int downres = 1;
            int tw = data.cameraTargetDescriptor.width >> downres;
            int th = data.cameraTargetDescriptor.height >> downres;

            int maxSize = Mathf.Max(tw, th);
            int iterations = Mathf.FloorToInt(Mathf.Log(maxSize, 2f) - 1);
            int mipCount = Mathf.Clamp(iterations, 1, benDayBloom.maxIterations.value);

            float clamp = benDayBloom.clamp.value;
            float threshold = Mathf.GammaToLinearSpace(benDayBloom.threshold.value);
            float threhsoldKnee = threshold * 0.5f;

            float scatter = Mathf.Lerp(0.05f, 0.95f, benDayBloom.scatter.value);
            var bloomMaterial = data.bloomMaterial;
        } 
    }

    // RecordRenderGraph is where the RenderGraph handle can be accessed, through which render passes can be added to the graph.
    // FrameData is a context container through which URP resources can be accessed and managed.
    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
        const string k_passName = "Render Custom Pass";

        // This adds a raster render pass to the graph, specifying the name and the data type that will be passed to the ExecutePass function.
        using (var builder = renderGraph.AddRasterRenderPass<PassData>(k_passName, out var passData))
        {
            // Use this scope to set the required inputs and outputs of the pass and to
            // setup the passData with the required properties needed at pass execution time.

            // Make use of frameData to access resources and camera data through the dedicated containers.
            // Eg:
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            // Setup pass inputs and outputs through the builder interface.
            // Eg:
            // builder.UseTexture(sourceTexture);
            // TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, cameraData.cameraTargetDescriptor, "Destination Texture", false);
            
            _BloomMipUp = new int[k_maxPyramidSize];
            _BloomMipDown = new int[k_maxPyramidSize];
            m_BloomMipUp = new TextureHandle[k_maxPyramidSize];
            m_BloomMipDown = new TextureHandle[k_maxPyramidSize];

            for (int i = 0; i < k_maxPyramidSize; i++) {
                _BloomMipUp[i] = Shader.PropertyToID("_BloomMipUp" + i);
                _BloomMipDown[i] = Shader.PropertyToID("_BloomMipDown" + i);

                // Create a new TextureHandle for each bloop mip up
                m_BloomMipUp[i] = builder.CreateTransientTexture(new TextureDesc(Vector2.one, true, true) {
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true,
                    name = "BloomMipUp" + i
                });

                // Create a new TextureHandle for each bloop mip down
                m_BloomMipDown[i] = builder.CreateTransientTexture(new TextureDesc(Vector2.one, true, true) {
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    enableRandomWrite = true,
                    name = "BloomMipDown" + i
                });

                const GraphicsFormatUsage usage = GraphicsFormatUsage.Linear | GraphicsFormatUsage.Render;
                if (SystemInfo.IsFormatSupported(GraphicsFormat.B10G11R11_UFloatPack32, usage)) // HDR fallback
                {
                    hdrFormat = GraphicsFormat.B10G11R11_UFloatPack32;
                }
                else {
                    hdrFormat = QualitySettings.activeColorSpace == ColorSpace.Linear
                        ? GraphicsFormat.R8G8B8A8_SRGB
                        : GraphicsFormat.R8G8B8A8_UNorm;
                }
            }
            
            passData.cameraTargetDescriptor = cameraData.cameraTargetDescriptor;
            passData.colorTexture = resourceData.activeColorTexture;
            passData.depthTexture = resourceData.cameraDepthTexture;

            passData.bloomMaterial = bloomMaterial;
            
            builder.UseTexture(resourceData.activeColorTexture, 0);
            builder.UseTexture(resourceData.activeDepthTexture, 0);
            
            builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
            builder.SetRenderAttachmentDepth(resourceData.cameraDepthTexture);

            // Assigns the ExecutePass function to the render pass delegate. This will be called by the render graph when executing the pass.
            builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePass(data, context));
        }
    }
}