using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Enumerates the identifiers to use with the FrameResource manager to get/set URP frame resources.
    /// </summary>
    public enum UniversalResource
    {
        /// <summary>
        /// The backbuffer color used to render directly to screen. All passes can write to it depending on frame setup.
        /// </summary>
        BackBufferColor,

        /// <summary>
        /// The backbuffer depth used to render directly to screen. All passes can write to it depending on frame setup.
        /// </summary>
        BackBufferDepth,

        // intermediate camera targets

        /// <summary>
        /// Main offscreen camera color target. All passes can write to it depending on frame setup.
        /// Can hold multiple samples if MSAA is enabled.
        /// </summary>
        CameraColor,
        /// <summary>
        /// Main offscreen camera depth target. All passes can write to it depending on frame setup.
        /// Can hold multiple samples if MSAA is enabled.
        /// </summary>
        CameraDepth,

        // shadows

        /// <summary>
        /// Main shadow map.
        /// </summary>
        MainShadowsTexture,
        /// <summary>
        /// Additional shadow map.
        /// </summary>
        AdditionalShadowsTexture,

        // gbuffer targets

        /// <summary>
        /// GBuffer0. Written to by the GBuffer pass.
        /// </summary>
        GBuffer0,
        /// <summary>
        /// GBuffer1. Written to by the GBuffer pass.
        /// </summary>
        GBuffer1,
        /// <summary>
        /// GBuffer2. Written to by the GBuffer pass.
        /// </summary>
        GBuffer2,
        /// <summary>
        /// GBuffer3. Written to by the GBuffer pass.
        /// </summary>
        GBuffer3,
        /// <summary>
        /// GBuffer4. Written to by the GBuffer pass.
        /// </summary>
        GBuffer4,
        /// <summary>
        /// GBuffer5. Written to by the GBuffer pass.
        /// </summary>
        GBuffer5,
        /// <summary>
        /// GBuffer6. Written to by the GBuffer pass.
        /// </summary>
        GBuffer6,

        // camera opaque/depth/normal

        /// <summary>
        /// Camera opaque texture. Contains a copy of CameraColor if the CopyColor pass is executed.
        /// </summary>
        CameraOpaqueTexture,
        /// <summary>
        /// Camera depth texture. Contains the scene depth if the CopyDepth or Depth Prepass passes are executed.
        /// </summary>
        CameraDepthTexture,
        /// <summary>
        /// Camera normals texture. Contains the scene depth if the DepthNormals Prepass pass is executed.
        /// </summary>
        CameraNormalsTexture,

        // motion vector

        /// <summary>
        /// Motion Vector Color. Written to by the Motion Vector passes.
        /// </summary>
        MotionVectorColor,
        /// <summary>
        /// Motion Vector Depth. Written to by the Motion Vector passes.
        /// </summary>
        MotionVectorDepth,

        // postFx

        /// <summary>
        /// Internal Color LUT. Written to by the InternalLUT pass.
        /// </summary>
        InternalColorLut,
        /// <summary>
        /// Color output of post-process passes (uberPost and finalPost) when HDR debug views are enabled. It replaces
        /// the backbuffer color as standard output because the later cannot be sampled back (or may not be in HDR format).
        /// If used, DebugHandler will perform the blit from DebugScreenTexture to BackBufferColor.
        /// </summary>
        DebugScreenColor,
        /// <summary>
        /// Depth output of post-process passes (uberPost and finalPost) when HDR debug views are enabled. It replaces
        /// the backbuffer depth as standard output because the later cannot be sampled back.
        /// </summary>
        DebugScreenDepth,
        /// <summary>
        /// After Post Process Color. Stores the contents of the main color target after the post processing passes.
        /// </summary>
        AfterPostProcessColor,
        /// <summary>
        /// Overlay UI Texture. The DrawScreenSpaceUI pass writes to this texture when rendering off-screen.
        /// </summary>
        OverlayUITexture,

        // rendering layers

        /// <summary>
        /// Rendering Layers Texture. Can be written to by the DrawOpaques pass or DepthNormals prepass based on settings.
        /// </summary>
        RenderingLayersTexture,

        // decals

        /// <summary>
        /// DBuffer0. Written to by the Decals pass.
        /// </summary>
        DBuffer0,
        /// <summary>
        /// DBuffer1. Written to by the Decals pass.
        /// </summary>
        DBuffer1,
        /// <summary>
        /// DBuffer2. Written to by the Decals pass.
        /// </summary>
        DBuffer2,

        /// <summary>
        /// DBufferDepth. Written to by the Decals pass.
        /// </summary>
        DBufferDepth,

        /// <summary>
        /// Screen Space Ambient Occlusion texture. Written to by the SSAO pass.
        /// </summary>
        SSAOTexture
    }

    public sealed partial class UniversalRenderer
    {
        // TODO RENDERGRAPH: Once all cameras will run in a single RenderGraph we should remove all RTHandles and use per frame RG textures.
        // We use 2 camera color handles so we can handle the edge case when a pass might want to read and write the same target.
        // This is not allowed so we just swap the current target, this keeps camera stacking working and avoids an extra blit pass.
        private static RTHandle[] m_RenderGraphCameraColorHandles = new RTHandle[]
        {
            null, null
        };
        private static RTHandle m_RenderGraphCameraDepthHandle;
        private static int m_CurrentColorHandle = 0;

        private RTHandle currentRenderGraphCameraColorHandle => (m_RenderGraphCameraColorHandles[m_CurrentColorHandle]);

        // get the next m_RenderGraphCameraColorHandles and make it the new current for future accesses
        private RTHandle nextRenderGraphCameraColorHandle
        {
            get
            {
                m_CurrentColorHandle = (m_CurrentColorHandle + 1) % 2;
                return m_RenderGraphCameraColorHandles[m_CurrentColorHandle];
            }
        }

        /// <summary>
        /// Returns the current active color target texture. To be referenced at RenderGraph pass recording time, not in passes render functions.
        /// </summary>
        /// <returns>TextureHandle</returns>
        public TextureHandle activeColorTexture => resources.frameData.Get<UniversalResourcesData>().activeColorTexture;

        /// <summary>
        /// Returns the current active depth target texture. To be referenced at RenderGraph pass recording time, not in passes render functions.
        /// </summary>
        /// <returns>TextureHandle</returns>
        public TextureHandle activeDepthTexture => resources.frameData.Get<UniversalResourcesData>().activeDepthTexture;

        /// <summary>
        /// True if the current active target is the backbuffer. To be referenced at RenderGraph pass recording time, not in passes render functions.
        /// </summary>
        /// <returns>bool</returns>
        public bool isActiveTargetBackBuffer
        {
            get
            {
                UniversalResourcesData resourcesData = resources.frameData.Get<UniversalResourcesData>();
                if (!resourcesData.isAccessible)
                {
                    Debug.LogError("Trying to access FrameResources outside of the current frame setup.");
                    return false;
                }

                return resourcesData.activeColorID == UniversalResourcesData.ActiveID.BackBuffer;
            }
        }

        // rendering layers
        private bool m_RequiresRenderingLayer;
        private RenderingLayerUtils.Event m_RenderingLayersEvent;
        private RenderingLayerUtils.MaskSize m_RenderingLayersMaskSize;
        private bool m_RenderingLayerProvidesRenderObjectPass;
        private bool m_RenderingLayerProvidesByDepthNormalPass;
        private string m_RenderingLayersTextureName;

        private void CleanupRenderGraphResources()
        {
            m_RenderGraphCameraColorHandles[0]?.Release();
            m_RenderGraphCameraColorHandles[1]?.Release();
            m_RenderGraphCameraDepthHandle?.Release();
        }

        /// <summary>
        /// Utility method to convert RenderTextureDescriptor to TextureHandle and create a RenderGraph texture
        /// </summary>
        /// <param name="renderGraph"></param>
        /// <param name="desc"></param>
        /// <param name="name"></param>
        /// <param name="clear"></param>
        /// <param name="filterMode"></param>
        /// <param name="wrapMode"></param>
        /// <returns></returns>
        public static TextureHandle CreateRenderGraphTexture(RenderGraph renderGraph, RenderTextureDescriptor desc, string name, bool clear,
            FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Clamp)
        {
            TextureDesc rgDesc = new TextureDesc(desc.width, desc.height);
            rgDesc.dimension = desc.dimension;
            rgDesc.clearBuffer = clear;
            rgDesc.bindTextureMS = desc.bindMS;
            rgDesc.colorFormat = desc.graphicsFormat;
            rgDesc.depthBufferBits = (DepthBits)desc.depthBufferBits;
            rgDesc.slices = desc.volumeDepth;
            rgDesc.msaaSamples = (MSAASamples)desc.msaaSamples;
            rgDesc.name = name;
            rgDesc.enableRandomWrite = desc.enableRandomWrite;
            rgDesc.filterMode = filterMode;
            rgDesc.wrapMode = wrapMode;
            rgDesc.isShadowMap = desc.shadowSamplingMode != ShadowSamplingMode.None && desc.depthStencilFormat != GraphicsFormat.None;
            // TODO RENDERGRAPH: depthStencilFormat handling?

            return renderGraph.CreateTexture(rgDesc);
        }

        internal static TextureHandle CreateRenderGraphTexture(RenderGraph renderGraph, RenderTextureDescriptor desc, string name, bool clear, Color color,
            FilterMode filterMode = FilterMode.Point, TextureWrapMode wrapMode = TextureWrapMode.Clamp)
        {
            TextureDesc rgDesc = new TextureDesc(desc.width, desc.height);
            rgDesc.dimension = desc.dimension;
            rgDesc.clearBuffer = clear;
            rgDesc.clearColor = color;
            rgDesc.bindTextureMS = desc.bindMS;
            rgDesc.colorFormat = desc.graphicsFormat;
            rgDesc.depthBufferBits = (DepthBits)desc.depthBufferBits;
            rgDesc.slices = desc.volumeDepth;
            rgDesc.msaaSamples = (MSAASamples)desc.msaaSamples;
            rgDesc.name = name;
            rgDesc.enableRandomWrite = desc.enableRandomWrite;
            rgDesc.filterMode = filterMode;
            rgDesc.wrapMode = wrapMode;

            return renderGraph.CreateTexture(rgDesc);
        }

        bool ShouldApplyPostProcessing(ref RenderingData renderingData)
        {
            return renderingData.cameraData.postProcessEnabled && m_PostProcessPasses.isCreated;
        }

        bool CameraHasPostProcessingWithDepth(ref RenderingData renderingData)
        {
            return ShouldApplyPostProcessing(ref renderingData) && renderingData.cameraData.postProcessingRequiresDepthTexture;
        }

        void RequiresColorAndDepthTextures(RenderGraph renderGraph, out bool createColorTexture, out bool createDepthTexture, ref RenderingData renderingData, RenderPassInputSummary renderPassInputs)
        {
            bool isPreviewCamera = renderingData.cameraData.isPreviewCamera;
            bool requiresDepthPrepass = RequireDepthPrepass(ref renderingData, renderPassInputs);

            var requireColorTexture = HasActiveRenderFeatures() && m_IntermediateTextureMode == IntermediateTextureMode.Always;
            requireColorTexture |= Application.isEditor && m_Clustering;
            requireColorTexture |= RequiresIntermediateColorTexture(ref renderingData.cameraData);
            requireColorTexture &= !isPreviewCamera;

            var requireDepthTexture = RequireDepthTexture(ref renderingData, renderPassInputs, requiresDepthPrepass);

            useDepthPriming = IsDepthPrimingEnabled(ref renderingData.cameraData);

            // Intermediate texture has different yflip state than backbuffer. In case we use intermedaite texture, we must use both color and depth together.
            bool intermediateRenderTexture = (requireColorTexture || requireDepthTexture);
            createDepthTexture = intermediateRenderTexture;
            createColorTexture = intermediateRenderTexture;
        }

        void CreateRenderGraphCameraRenderTargets(RenderGraph renderGraph, ref RenderingData renderingData, bool isCameraTargetOffscreenDepth)
        {
            ContextContainer frameData = renderingData.frameData;
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();

            ref CameraData cameraData = ref renderingData.cameraData;
            bool lastCameraInTheStack = cameraData.resolveFinalTarget;
            bool isBuiltInTexture = (cameraData.targetTexture == null);

            RenderTargetIdentifier targetColorId = cameraData.targetTexture != null ? new RenderTargetIdentifier(cameraData.targetTexture) : BuiltinRenderTextureType.CameraTarget;
            RenderTargetIdentifier targetDepthId = cameraData.targetTexture != null ? new RenderTargetIdentifier(cameraData.targetTexture) : BuiltinRenderTextureType.Depth;

            bool clearColor = renderingData.cameraData.renderType == CameraRenderType.Base;
            bool clearDepth = renderingData.cameraData.renderType == CameraRenderType.Base || renderingData.cameraData.clearDepth;
            // if the camera background type is "uninitialized" clear using a yellow color, so users can clearly understand the underlying behaviour
            Color cameraBackgroundColor = (cameraData.camera.clearFlags == CameraClearFlags.Nothing) ? Color.yellow : renderingData.cameraData.backgroundColor;

            // If scene filtering is enabled (prefab edit mode), the filtering is implemented compositing some builtin ImageEffect passes.
            // For the composition to work, we need to clear the color buffer alpha to 0
            // How filtering works:
            // - SRP frame is fully rendered as background
            // - builtin ImageEffect pass grey-out of the full scene previously rendered
            // - SRP frame rendering only the objects belonging to the prefab being edited (with clearColor.a = 0)
            // - builtin ImageEffect pass compositing the two previous passes
            // TODO: We should implement filtering fully in SRP to remove builtin dependencies
            if (IsSceneFilteringEnabled(cameraData.camera))
            {
                cameraBackgroundColor.a = 0;
                clearDepth = false;
            }

            // Certain debug modes (e.g. wireframe/overdraw modes) require that we override clear flags and clear everything.
            var debugHandler = cameraData.renderer.DebugHandler;
            if (debugHandler != null && debugHandler.IsActiveForCamera(ref cameraData) && debugHandler.IsScreenClearNeeded)
            {
                clearColor = true;
                clearDepth = true;
            }

            ImportResourceParams importColorParams = new ImportResourceParams();
            importColorParams.clearOnFirstUse = clearColor; // && cameraData.camera.clearFlags != CameraClearFlags.Nothing;
            importColorParams.clearColor = cameraBackgroundColor;
            importColorParams.discardOnLastUse = false;

            ImportResourceParams importDepthParams = new ImportResourceParams();
            importDepthParams.clearOnFirstUse = clearDepth;
            importDepthParams.clearColor = cameraBackgroundColor;
            importDepthParams.discardOnLastUse = false;

#if ENABLE_VR && ENABLE_XR_MODULE
            if (cameraData.xr.enabled)
            {
                targetColorId = cameraData.xr.renderTarget;
                targetDepthId = cameraData.xr.renderTarget;
                isBuiltInTexture = false;
            }
#endif

            if (m_TargetColorHandle == null)
            {
                m_TargetColorHandle = RTHandles.Alloc(targetColorId, "Backbuffer color");
            }
            else if(m_TargetColorHandle.nameID != targetColorId)
            {
                RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_TargetColorHandle, targetColorId);
            }

            if (m_TargetDepthHandle == null)
            {
                m_TargetDepthHandle = RTHandles.Alloc(targetDepthId, "Backbuffer depth");
            }
            else if (m_TargetDepthHandle.nameID != targetDepthId)
            {
                RTHandleStaticHelpers.SetRTHandleUserManagedWrapper(ref m_TargetDepthHandle, targetDepthId);
            }

            RenderPassInputSummary renderPassInputs = GetRenderPassInputs(ref renderingData);

            // Enable depth normal prepass if it's needed by rendering layers
            if (m_RenderingLayerProvidesByDepthNormalPass)
                renderPassInputs.requiresNormalsTexture = true;

            // We configure this for the first camera of the stack and overlay camera will reuse create color/depth var
            // to pick the correct target, as if there is an intermediate texture, overlay cam should use them
            if (cameraData.renderType == CameraRenderType.Base)
                RequiresColorAndDepthTextures(renderGraph, out m_CreateColorTexture, out m_CreateDepthTexture, ref renderingData, renderPassInputs);


            // The final output back buffer should be cleared by the graph on first use only if we have no final blit pass.
            // If there is a final blit, that blit will write the buffers so on first sight an extra clear should not be problem,
            // blit will simply blit over the cleared values. BUT! the final blit may not write the whole buffer in case of a camera
            // with a Viewport Rect smaller than the full screen. So the existing backbuffer contents need to be preserved in this case.
            // Finally for non-base cameras the backbuffer should never be cleared. (Note that there might still be two base cameras
            // rendering to the same screen. See e.g. test foundation 014 that renders a minimap)
            bool clearBackbufferOnFirstUse = (renderingData.cameraData.renderType == CameraRenderType.Base) && !m_CreateColorTexture;

            ImportResourceParams importBackbufferColorParams = new ImportResourceParams();
            importBackbufferColorParams.clearOnFirstUse = clearBackbufferOnFirstUse;
            importBackbufferColorParams.clearColor = cameraBackgroundColor;
            importBackbufferColorParams.discardOnLastUse = false;

            ImportResourceParams importBackbufferDepthParams = new ImportResourceParams();
            importBackbufferDepthParams.clearOnFirstUse = clearBackbufferOnFirstUse;
            importBackbufferDepthParams.clearColor = cameraBackgroundColor;
            importBackbufferDepthParams.discardOnLastUse = true;

            // For BuiltinRenderTextureType wrapping RTHandles RenderGraph can't know what they are so we have to pass it in.
            RenderTargetInfo importInfo = new RenderTargetInfo();
            RenderTargetInfo importInfoDepth = new RenderTargetInfo();

            // So the render target we pass into render graph is
            // RTHandles(RenderTargetIdentifier(BuiltinRenderTextureType.CameraTarget))
            // or
            // RTHandles(RenderTargetIdentifier(RenderTexture(cameraData.targetTexture)))
            //
            // Because of the RenderTargetIdentifier in the "wrapper chain" the graph can't know
            // the size of the passed in textures so we need to provide it for the graph.
            // The amount of texture handle wrappers and their subtleties is probably something to be investigated.
            if (isBuiltInTexture)
            {
                bool msaaSamplesChangedThisFrame = false;
#if !UNITY_EDITOR
                // for safety do this only for the NRP path, even though works also on non NRP, but would need extensive testing
                if (m_CreateColorTexture && renderGraph.NativeRenderPassesEnabled && Screen.msaaSamples > 1)
                {
                    msaaSamplesChangedThisFrame = true;
                    Screen.SetMSAASamples(1);
                }
#endif
                int numSamples = Mathf.Max(Screen.msaaSamples, 1);

                // Handle edge cases regarding numSamples setup
                // On OSX player, the Screen API MSAA samples change request is only applied in the following frame,
                // as a workaround we keep the old MSAA sample count for the previous frame
                // this workaround can be removed once the Screen API issue (UUM-42825) is fixed
                // The editor always allocates the system rendertarget with a single msaa sample
                // See: ConfigureTargetTexture in PlayModeView.cs
                if (msaaSamplesChangedThisFrame && Application.platform == RuntimePlatform.OSXPlayer)
                    numSamples = cameraData.cameraTargetDescriptor.msaaSamples;
                else if (Application.isEditor)
                    numSamples = 1;

                //BuiltinRenderTextureType.CameraTarget so this is either system render target or camera.targetTexture if non null
                //NOTE: Careful what you use here as many of the properties bake-in the camera rect so for example
                //cameraData.cameraTargetDescriptor.width is the width of the rectangle but not the actual render target
                //same with cameraData.camera.pixelWidth
                importInfo.width = Screen.width;
                importInfo.height = Screen.height;
                importInfo.volumeDepth = 1;
                importInfo.msaaSamples = numSamples;

                importInfo.format = UniversalRenderPipeline.MakeRenderTextureGraphicsFormat(cameraData.isHdrEnabled, cameraData.hdrColorBufferPrecision, Graphics.preserveFramebufferAlpha);

                importInfoDepth = importInfo;
                importInfoDepth.format = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil);
            }
            else
            {
#if ENABLE_VR && ENABLE_XR_MODULE
                if (cameraData.xr.enabled)
                {
                    importInfo.width = cameraData.xr.renderTargetDesc.width;
                    importInfo.height = cameraData.xr.renderTargetDesc.height;
                    importInfo.volumeDepth = cameraData.xr.renderTargetDesc.volumeDepth;
                    importInfo.msaaSamples = cameraData.xr.renderTargetDesc.msaaSamples;
                    importInfo.format = cameraData.xr.renderTargetDesc.graphicsFormat;

                    importInfoDepth = importInfo;
                    importInfoDepth.format = cameraData.xr.renderTargetDesc.depthStencilFormat;
                }
                else
#endif
                {
                    importInfo.width = cameraData.targetTexture.width;
                    importInfo.height = cameraData.targetTexture.height;
                    importInfo.volumeDepth = cameraData.targetTexture.volumeDepth;
                    importInfo.msaaSamples = cameraData.targetTexture.antiAliasing;
                    importInfo.format = cameraData.targetTexture.graphicsFormat;

                    importInfoDepth = importInfo;
                    importInfoDepth.format = cameraData.targetTexture.depthStencilFormat;
                }

                // We let users know that a depth format is required for correct usage, but we fallback to the old default depth format behaviour to avoid regressions
                if (importInfoDepth.format == GraphicsFormat.None)
                {
                    importInfoDepth.format = SystemInfo.GetGraphicsFormat(DefaultFormat.DepthStencil);
                    Debug.LogWarning("Trying to render to a rendertexture without a depth buffer. URP+RG needs a depthbuffer to render.");
                }
            }

            // TODO: Don't think the backbuffer color and depth should be imported at all if !isBuiltinTexture, double check
            if (!isCameraTargetOffscreenDepth)
            {
                resourcesData.backBufferColor = renderGraph.ImportTexture(m_TargetColorHandle, importInfo, importBackbufferColorParams);
                resourcesData.backBufferDepth = renderGraph.ImportTexture(m_TargetDepthHandle, importInfoDepth, importBackbufferDepthParams);
            }

            #region Intermediate Camera Target

            if (m_CreateColorTexture && !isCameraTargetOffscreenDepth)
            {
                var cameraTargetDescriptor = cameraData.cameraTargetDescriptor;
                cameraTargetDescriptor.useMipMap = false;
                cameraTargetDescriptor.autoGenerateMips = false;
                cameraTargetDescriptor.depthBufferBits = (int)DepthBits.None;

                RenderingUtils.ReAllocateIfNeeded(ref m_RenderGraphCameraColorHandles[0], cameraTargetDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CameraTargetAttachmentA");
                RenderingUtils.ReAllocateIfNeeded(ref m_RenderGraphCameraColorHandles[1], cameraTargetDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CameraTargetAttachmentB");

                // Make sure that the base camera always starts rendering to the ColorAttachmentA for deterministic frame results.
                // Not doing so makes the targets look different every frame, causing the frame debugger to flash, and making debugging harder.
                if (renderingData.cameraData.renderType == CameraRenderType.Base)
                    m_CurrentColorHandle = 0;

                importColorParams.discardOnLastUse = lastCameraInTheStack;
                resourcesData.cameraColor = renderGraph.ImportTexture(currentRenderGraphCameraColorHandle, importColorParams);
                resourcesData.activeColorID = UniversalResourcesData.ActiveID.Camera;
            }
            else
            {
                resourcesData.activeColorID = UniversalResourcesData.ActiveID.BackBuffer;
            }


            if (m_CreateDepthTexture)
            {
                var depthDescriptor = cameraData.cameraTargetDescriptor;
                depthDescriptor.useMipMap = false;
                depthDescriptor.autoGenerateMips = false;
                depthDescriptor.bindMS = false;

                bool hasMSAA = depthDescriptor.msaaSamples > 1 && (SystemInfo.supportsMultisampledTextures != 0);
                bool resolveDepth = RenderingUtils.MultisampleDepthResolveSupported() && renderGraph.NativeRenderPassesEnabled;

                // TODO RENDERGRAPH: once all passes are ported to RasterCommandBuffers we need to reenable depth resolve
                m_CopyDepthMode = renderPassInputs.requiresDepthTextureEarliestEvent < RenderPassEvent.AfterRenderingTransparents ? CopyDepthMode.AfterOpaques : m_CopyDepthMode;
                m_CopyDepthPass.m_CopyResolvedDepth = resolveDepth && m_CopyDepthMode == CopyDepthMode.AfterTransparents;

                if (hasMSAA)
                {
                    // if depth priming is enabled the copy depth primed pass is meant to do the MSAA resolve, so we want to bind the MS surface
                    if (IsDepthPrimingEnabled(ref cameraData))
                        depthDescriptor.bindMS = true;
                    else
                        depthDescriptor.bindMS = !(resolveDepth && m_CopyDepthMode == CopyDepthMode.AfterTransparents);
                }

                // binding MS surfaces is not supported by the GLES backend, and it won't be fixed after investigating
                // the high performance impact of potential fixes, which would make it more expensive than depth prepass (fogbugz 1339401 for more info)
                if (IsGLESDevice())
                    depthDescriptor.bindMS = false;

                depthDescriptor.graphicsFormat = GraphicsFormat.None;
                depthDescriptor.depthStencilFormat = k_DepthStencilFormat;

                RenderingUtils.ReAllocateIfNeeded(ref m_RenderGraphCameraDepthHandle, depthDescriptor, FilterMode.Point, TextureWrapMode.Clamp, name: "_CameraDepthAttachment");

                importDepthParams.discardOnLastUse = lastCameraInTheStack;
                resourcesData.cameraDepth = renderGraph.ImportTexture(m_RenderGraphCameraDepthHandle, importDepthParams);
                resourcesData.activeDepthID = UniversalResourcesData.ActiveID.Camera;
            }
            else
            {
                resourcesData.activeDepthID = UniversalResourcesData.ActiveID.BackBuffer;
            }
            #endregion

            CreateCameraDepthCopyTexture(renderGraph, cameraData.cameraTargetDescriptor, RequireDepthPrepass(ref renderingData, renderPassInputs) && this.renderingModeActual != RenderingMode.Deferred);

            CreateCameraNormalsTexture(renderGraph, cameraData.cameraTargetDescriptor);

            CreateMotionVectorTextures(renderGraph, cameraData.cameraTargetDescriptor);

            CreateRenderingLayersTexture(renderGraph, cameraData.cameraTargetDescriptor);

            if (!isCameraTargetOffscreenDepth)
                CreateAfterPostProcessTexture(renderGraph, cameraData.cameraTargetDescriptor);
        }

        void SetupRenderingLayers(ref RenderingData renderingData)
        {
            // Gather render pass require rendering layers event and mask size
            m_RequiresRenderingLayer = RenderingLayerUtils.RequireRenderingLayers(this, rendererFeatures, renderingData.cameraData.cameraTargetDescriptor.msaaSamples,
                out m_RenderingLayersEvent, out m_RenderingLayersMaskSize);

            m_RenderingLayerProvidesRenderObjectPass = m_RequiresRenderingLayer && renderingModeActual == RenderingMode.Forward && m_RenderingLayersEvent == RenderingLayerUtils.Event.Opaque;
            m_RenderingLayerProvidesByDepthNormalPass = m_RequiresRenderingLayer && m_RenderingLayersEvent == RenderingLayerUtils.Event.DepthNormalPrePass;

            if (m_DeferredLights != null)
            {
                m_DeferredLights.RenderingLayerMaskSize = m_RenderingLayersMaskSize;
                m_DeferredLights.UseDecalLayers = m_RequiresRenderingLayer;
            }
        }

        internal void SetupRenderGraphLights(RenderGraph renderGraph, ref RenderingData renderingData)
        {
            m_ForwardLights.SetupRenderGraphLights(renderGraph, ref renderingData);
            if (this.renderingModeActual == RenderingMode.Deferred)
            {
                m_DeferredLights.UseRenderPass = renderGraph.NativeRenderPassesEnabled;
                m_DeferredLights.SetupRenderGraphLights(renderGraph, ref renderingData);
            }
        }

        internal override void OnBeginRenderGraphFrame(ContextContainer frameData)
        {
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();
            resourcesData.InitFrame();
        }

        internal override void OnRecordRenderGraph(RenderGraph renderGraph, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ContextContainer frameData = renderingData.frameData;
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();

            ref CameraData cameraData = ref renderingData.cameraData;
            useRenderPassEnabled = renderGraph.NativeRenderPassesEnabled;

            SetupMotionVectorGlobalMatrix(renderingData.commandBuffer, ref cameraData);

            SetupRenderGraphLights(renderGraph, ref renderingData);

            SetupRenderingLayers(ref renderingData);

            bool isCameraTargetOffscreenDepth = cameraData.camera.targetTexture != null && cameraData.camera.targetTexture.format == RenderTextureFormat.Depth;

            CreateRenderGraphCameraRenderTargets(renderGraph, ref renderingData, isCameraTargetOffscreenDepth);

            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.BeforeRendering);

            SetupRenderGraphCameraProperties(renderGraph, ref renderingData, isActiveTargetBackBuffer);

#if VISUAL_EFFECT_GRAPH_0_0_1_OR_NEWER
            ProcessVFXCameraCommand(renderGraph, ref renderingData);
#endif
            cameraData.renderer.useDepthPriming = useDepthPriming;

            if (isCameraTargetOffscreenDepth)
            {
                OnOffscreenDepthTextureRendering(renderGraph, context, ref renderingData);
                return;
            }

            OnBeforeRendering(renderGraph, ref renderingData);

            BeginRenderGraphXRRendering(renderGraph, ref renderingData);

            OnMainRendering(renderGraph, context, ref renderingData);

            OnAfterRendering(renderGraph, ref renderingData);

            EndRenderGraphXRRendering(renderGraph, ref renderingData);
        }

        internal override void OnEndRenderGraphFrame(ContextContainer frameData)
        {
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();
            resourcesData.EndFrame();
        }

        internal override void OnFinishRenderGraphRendering(ref RenderingData renderingData)
        {
            if (this.renderingModeActual == RenderingMode.Deferred)
                m_DeferredPass.OnCameraCleanup(renderingData.commandBuffer);

            m_CopyDepthPass.OnCameraCleanup(renderingData.commandBuffer);
            m_DepthNormalPrepass.OnCameraCleanup(renderingData.commandBuffer);
        }

        private static bool m_CreateColorTexture;
        private static bool m_CreateDepthTexture;

        private void OnOffscreenDepthTextureRendering(RenderGraph renderGraph, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ContextContainer frameData = renderingData.frameData;
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();

            if (!renderGraph.NativeRenderPassesEnabled)
                ClearTargetsPass.Render(renderGraph, resourcesData.activeColorTexture, resourcesData.backBufferDepth, RTClearFlags.Depth, renderingData.cameraData.backgroundColor);

            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.BeforeRenderingShadows, RenderPassEvent.BeforeRenderingOpaques);
            m_RenderOpaqueForwardPass.Render(renderGraph, TextureHandle.nullHandle, resourcesData.backBufferDepth, TextureHandle.nullHandle, TextureHandle.nullHandle, ref renderingData);
            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.AfterRenderingOpaques, RenderPassEvent.BeforeRenderingTransparents);
#if ADAPTIVE_PERFORMANCE_2_1_0_OR_NEWER
            if (needTransparencyPass)
#endif
            m_RenderTransparentForwardPass.Render(renderGraph, TextureHandle.nullHandle, resourcesData.backBufferDepth, TextureHandle.nullHandle, TextureHandle.nullHandle, ref renderingData);
            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.AfterRenderingTransparents, RenderPassEvent.AfterRendering);
        }
        private void OnBeforeRendering(RenderGraph renderGraph, ref RenderingData renderingData)
        {
            ContextContainer frameData = renderingData.frameData;
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();

            m_ForwardLights.PreSetup(ref renderingData);

            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.BeforeRenderingShadows);

            bool renderShadows = false;

            if (m_MainLightShadowCasterPass.Setup(ref renderingData))
            {
                renderShadows = true;
                resourcesData.mainShadowsTexture = m_MainLightShadowCasterPass.Render(renderGraph, ref renderingData);
            }

            if (m_AdditionalLightsShadowCasterPass.Setup(ref renderingData))
            {
                renderShadows = true;
                resourcesData.additionalShadowsTexture = m_AdditionalLightsShadowCasterPass.Render(renderGraph, ref renderingData);
            }

            // The camera need to be setup again after the shadows since those passes override some settings
            // TODO RENDERGRAPH: move the setup code into the shadow passes
            if (renderShadows)
                SetupRenderGraphCameraProperties(renderGraph, ref renderingData, isActiveTargetBackBuffer);

            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.AfterRenderingShadows);
        }

        private void OnMainRendering(RenderGraph renderGraph, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            ContextContainer frameData = renderingData.frameData;
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();

            if (!renderGraph.NativeRenderPassesEnabled)
            {
                RTClearFlags clearFlags = (RTClearFlags) GetCameraClearFlag(ref renderingData.cameraData);

                if (clearFlags != RTClearFlags.None)
                    ClearTargetsPass.Render(renderGraph, resourcesData.activeColorTexture, resourcesData.activeDepthTexture, clearFlags, renderingData.cameraData.backgroundColor);
            }

            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.BeforeRenderingPrePasses);

            ref var cameraData = ref renderingData.cameraData;
            // If Camera's PostProcessing is enabled and if there any enabled PostProcessing requires depth texture as shader read resource (Motion Blur/DoF)
            bool cameraHasPostProcessingWithDepth = CameraHasPostProcessingWithDepth(ref renderingData);

            RenderPassInputSummary renderPassInputs = GetRenderPassInputs(ref renderingData);

            if (m_RenderingLayerProvidesByDepthNormalPass)
                renderPassInputs.requiresNormalsTexture = true;

#if UNITY_EDITOR
            if (ProbeReferenceVolume.instance.IsProbeSamplingDebugEnabled())
                renderPassInputs.requiresNormalsTexture = true;
#endif

            bool requiresDepthPrepass = RequireDepthPrepass(ref renderingData, renderPassInputs);
            bool requiresDepthCopyPass = !requiresDepthPrepass
                                         && (cameraData.requiresDepthTexture || cameraHasPostProcessingWithDepth || renderPassInputs.requiresDepthTexture)
                                         && m_CreateDepthTexture; // we create both intermediate textures if this is true, so instead of repeating the checks we reuse this
            bool requiresColorCopyPass = renderingData.cameraData.requiresOpaqueTexture || renderPassInputs.requiresColorTexture;
            requiresColorCopyPass &= !cameraData.isPreviewCamera;
            bool requiredColorGradingLutPass = cameraData.postProcessEnabled && m_PostProcessPasses.isCreated;

            bool isDeferred = this.renderingModeActual == RenderingMode.Deferred;

            if (requiresDepthPrepass)
            {
                // TODO RENDERGRAPH: is this always a valid assumption for deferred rendering?
                TextureHandle depthTarget = (renderingModeActual == RenderingMode.Deferred) ? resourcesData.activeDepthTexture : resourcesData.cameraDepthTexture;
                depthTarget = (useDepthPriming && (cameraData.renderType == CameraRenderType.Base || cameraData.clearDepth)) ? resourcesData.activeDepthTexture : depthTarget;

                if (renderPassInputs.requiresNormalsTexture)
                    DepthNormalPrepassRender(renderGraph, renderPassInputs, depthTarget, ref renderingData);
                else
                    m_DepthPrepass.Render(renderGraph, ref depthTarget, ref renderingData);
            }

            // depth priming still needs to copy depth because the prepass doesn't target anymore CameraDepthTexture
            // TODO: this is unoptimal, investigate optimizations
            if (useDepthPriming)
            {
                TextureHandle depth = resourcesData.cameraDepth;
                TextureHandle cameraDepthTexture = resourcesData.cameraDepthTexture;
                m_PrimedDepthCopyPass.Render(renderGraph, cameraDepthTexture, depth, ref renderingData, true);
            }

            if (cameraData.renderType == CameraRenderType.Base && !requiresDepthPrepass && !requiresDepthCopyPass)
                RenderGraphUtils.SetGlobalTexture(renderGraph, "_CameraDepthTexture", SystemInfo.usesReversedZBuffer ? renderGraph.defaultResources.blackTexture : renderGraph.defaultResources.whiteTexture, "Set default Camera Depth Texture");

            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.AfterRenderingPrePasses);

            if (requiredColorGradingLutPass)
            {
                TextureHandle internalColorLut;
                m_PostProcessPasses.colorGradingLutPass.Render(renderGraph, out internalColorLut, ref renderingData);
                resourcesData.internalColorLut = internalColorLut;
            }

#if ENABLE_VR && ENABLE_XR_MODULE
            if (cameraData.xr.hasValidOcclusionMesh)
                m_XROcclusionMeshPass.Render(renderGraph, resourcesData.activeDepthTexture, ref renderingData);
#endif

            if (isDeferred)
            {
                m_DeferredLights.Setup(m_AdditionalLightsShadowCasterPass);
                if (m_DeferredLights != null)
                {
                    m_DeferredLights.UseRenderPass = renderGraph.NativeRenderPassesEnabled;
                    m_DeferredLights.HasNormalPrepass = renderPassInputs.requiresNormalsTexture;
                    m_DeferredLights.HasDepthPrepass = requiresDepthPrepass;
                    m_DeferredLights.ResolveMixedLightingMode(ref renderingData);
                    m_DeferredLights.IsOverlay = cameraData.renderType == CameraRenderType.Overlay;
                }

                RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.BeforeRenderingGbuffer);

                m_GBufferPass.Render(renderGraph, resourcesData.activeColorTexture, resourcesData.activeDepthTexture, ref renderingData, resources);

                if (!renderGraph.NativeRenderPassesEnabled)
                {
                    TextureHandle cameraDepthTexture = resourcesData.cameraDepthTexture;
                    m_GBufferCopyDepthPass.Render(renderGraph, cameraDepthTexture, resourcesData.activeDepthTexture, ref renderingData, true, "GBuffer Depth Copy");
                }

                RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.AfterRenderingGbuffer, RenderPassEvent.BeforeRenderingDeferredLights);

                m_DeferredPass.Render(renderGraph, resourcesData.activeColorTexture, resourcesData.activeDepthTexture, resourcesData.gBuffer, ref renderingData, resources);

                RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.AfterRenderingDeferredLights, RenderPassEvent.BeforeRenderingOpaques);

                TextureHandle mainShadowsTexture = resourcesData.mainShadowsTexture;
                TextureHandle additionalShadowsTexture = resourcesData.additionalShadowsTexture;
                m_RenderOpaqueForwardOnlyPass.Render(renderGraph, resourcesData.activeColorTexture, resourcesData.activeDepthTexture, mainShadowsTexture, additionalShadowsTexture, ref renderingData);
            }
            else
            {
                RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.BeforeRenderingOpaques);

                if (m_RenderingLayerProvidesRenderObjectPass)
                {
                    m_RenderOpaqueForwardWithRenderingLayersPass.Render(
                        renderGraph,
                        resourcesData.activeColorTexture,
                        resourcesData.renderingLayersTexture,
                        resourcesData.activeDepthTexture,
                        resourcesData.mainShadowsTexture,
                        resourcesData.additionalShadowsTexture,
                        m_RenderingLayersMaskSize,
                        ref renderingData);
                    SetRenderingLayersGlobalTextures(renderGraph);
                }
                else
                {
                    m_RenderOpaqueForwardPass.Render(
                        renderGraph,
                        resourcesData.activeColorTexture,
                        resourcesData.activeDepthTexture,
                        resourcesData.mainShadowsTexture,
                        resourcesData.additionalShadowsTexture,
                        ref renderingData);
                }
            }

            // Custom passes come before built-in passes to keep parity with non-RG code path where custom passes are added before renderer Setup.
            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.AfterRenderingOpaques, RenderPassEvent.BeforeRenderingSkybox);

            if (cameraData.camera.clearFlags == CameraClearFlags.Skybox && cameraData.renderType != CameraRenderType.Overlay)
            {
                if (RenderSettings.skybox != null || (cameraData.camera.TryGetComponent(out Skybox cameraSkybox) && cameraSkybox.material != null))
                    m_DrawSkyboxPass.Render(renderGraph, context, resourcesData.activeColorTexture, resourcesData.activeDepthTexture, ref renderingData);
            }

            if (requiresDepthCopyPass && m_CopyDepthMode != CopyDepthMode.AfterTransparents)
            {
                TextureHandle cameraDepthTexture = resourcesData.cameraDepthTexture;
                m_CopyDepthPass.Render(renderGraph, cameraDepthTexture, resourcesData.activeDepthTexture, ref renderingData, true);
            }

            if (requiresColorCopyPass)
            {
                TextureHandle activeColor = resourcesData.activeColorTexture;
                Downsampling downsamplingMethod = UniversalRenderPipeline.asset.opaqueDownsampling;
                TextureHandle cameraOpaqueTexture;
                m_CopyColorPass.Render(renderGraph, out cameraOpaqueTexture, in activeColor, downsamplingMethod, ref renderingData);
                resourcesData.cameraOpaqueTexture = cameraOpaqueTexture;
            }

#if UNITY_EDITOR
            {
                TextureHandle cameraDepthTexture = resourcesData.cameraDepthTexture;
                TextureHandle cameraNormalsTexture = resourcesData.cameraNormalsTexture;
                m_ProbeVolumeDebugPass.Render(renderGraph, ref renderingData, cameraDepthTexture, cameraNormalsTexture);
            }
#endif

            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.AfterRenderingSkybox, RenderPassEvent.BeforeRenderingTransparents);

            // TODO RENDERGRAPH: bind _CameraOpaqueTexture, _CameraDepthTexture in transparent pass?
#if ADAPTIVE_PERFORMANCE_2_1_0_OR_NEWER
            if (needTransparencyPass)
#endif
            {
                m_RenderTransparentForwardPass.m_ShouldTransparentsReceiveShadows = !m_TransparentSettingsPass.Setup(ref renderingData);
                m_RenderTransparentForwardPass.Render(
                    renderGraph,
                    resourcesData.activeColorTexture,
                    resourcesData.activeDepthTexture,
                    resourcesData.mainShadowsTexture,
                    resourcesData.additionalShadowsTexture,
                    ref renderingData);
            }

            // Custom passes come before built-in passes to keep parity with non-RG code path where custom passes are added before renderer Setup.

            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.AfterRenderingTransparents);

            if (requiresDepthCopyPass && m_CopyDepthMode == CopyDepthMode.AfterTransparents)
                m_CopyDepthPass.Render(renderGraph, resourcesData.cameraDepthTexture, resourcesData.activeDepthTexture, ref renderingData, true);

            // TODO: Postprocess pass should be able configure its render pass inputs per camera per frame (settings) BEFORE building any of the graph
            // TODO: Alternatively we could always build the graph (a potential graph) and cull away unused passes if "record + cull" is fast enough.
            // TODO: Currently we just override "requiresMotionVectors" for TAA in GetRenderPassInputs()
            // Depends on camera depth
            if (renderPassInputs.requiresMotionVectors)
                m_MotionVectorPass.Render(renderGraph, resourcesData.cameraDepthTexture, resourcesData.motionVectorColor, resourcesData.motionVectorDepth, ref renderingData);

            if (context.HasInvokeOnRenderObjectCallbacks())
                m_OnRenderObjectCallbackPass.Render(renderGraph, resourcesData.activeColorTexture, resourcesData.activeDepthTexture, ref renderingData);

            bool shouldRenderUI = cameraData.rendersOverlayUI;
            bool outputToHDR = cameraData.isHDROutputActive;
            if (shouldRenderUI && outputToHDR)
            {
                TextureHandle overlayUI;
                m_DrawOffscreenUIPass.RenderOffscreen(renderGraph, k_DepthStencilFormat, out overlayUI, ref renderingData);
                resourcesData.overlayUITexture = overlayUI;
            }
        }

        private void OnAfterRendering(RenderGraph renderGraph, ref RenderingData renderingData)
        {
            ContextContainer frameData = renderingData.frameData;
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();

#if UNITY_EDITOR
            bool isGizmosEnabled = UnityEditor.Handles.ShouldRenderGizmos();
#endif
            // Disable Gizmos when using scene overrides. Gizmos break some effects like Overdraw debug.
            bool drawGizmos = UniversalRenderPipelineDebugDisplaySettings.Instance.renderingSettings.sceneOverrideMode == DebugSceneOverrideMode.None;

            if (drawGizmos)
                DrawRenderGraphGizmos(renderGraph, resourcesData.activeColorTexture, resourcesData.activeDepthTexture, GizmoSubset.PreImageEffects, ref renderingData);

            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.BeforeRenderingPostProcessing);

            bool cameraTargetResolved = false;
            bool applyPostProcessing = ShouldApplyPostProcessing(ref renderingData);
            // There's at least a camera in the camera stack that applies post-processing
            bool anyPostProcessing = renderingData.postProcessingEnabled && m_PostProcessPasses.isCreated;

            // When FXAA or scaling is active, we must perform an additional pass at the end of the frame for the following reasons:
            // 1. FXAA expects to be the last shader running on the image before it's presented to the screen. Since users are allowed
            //    to add additional render passes after post processing occurs, we can't run FXAA until all of those passes complete as well.
            //    The FinalPost pass is guaranteed to execute after user authored passes so FXAA is always run inside of it.
            // 2. UberPost can only handle upscaling with linear filtering. All other filtering methods require the FinalPost pass.
            // 3. TAA sharpening using standalone RCAS pass is required. (When upscaling is not enabled).
            bool applyFinalPostProcessing = anyPostProcessing && renderingData.cameraData.resolveFinalTarget &&
                                            ((renderingData.cameraData.antialiasing == AntialiasingMode.FastApproximateAntialiasing) ||
                                             ((renderingData.cameraData.imageScalingMode == ImageScalingMode.Upscaling) && (renderingData.cameraData.upscalingFilter != ImageUpscalingFilter.Linear)) ||
                                             (renderingData.cameraData.IsTemporalAAEnabled() && renderingData.cameraData.taaSettings.contrastAdaptiveSharpening > 0.0f));
            bool hasCaptureActions = renderingData.cameraData.captureActions != null && renderingData.cameraData.resolveFinalTarget;

            bool hasPassesAfterPostProcessing = activeRenderPassQueue.Find(x => x.renderPassEvent == RenderPassEvent.AfterRenderingPostProcessing) != null;

            bool resolvePostProcessingToCameraTarget = !hasCaptureActions && !hasPassesAfterPostProcessing && !applyFinalPostProcessing;
            bool needsColorEncoding = DebugHandler == null || !DebugHandler.HDRDebugViewIsActive(ref renderingData.cameraData);

            TextureHandle cameraColor = resourcesData.cameraColor;

            DebugHandler debugHandler = ScriptableRenderPass.GetActiveDebugHandler(ref renderingData);
            bool resolveToDebugScreen = debugHandler != null && debugHandler.WriteToDebugScreenTexture(ref renderingData.cameraData);
            // Allocate debug screen texture if the debug mode needs it.
            if (resolveToDebugScreen)
            {
                RenderTextureDescriptor colorDesc = renderingData.cameraData.cameraTargetDescriptor;
                DebugHandler.ConfigureColorDescriptorForDebugScreen(ref colorDesc, renderingData.cameraData.pixelWidth, renderingData.cameraData.pixelHeight);
                resourcesData.debugScreenColor = CreateRenderGraphTexture(renderGraph, colorDesc, "_DebugScreenColor", false);

                RenderTextureDescriptor depthDesc = renderingData.cameraData.cameraTargetDescriptor;
                DebugHandler.ConfigureDepthDescriptorForDebugScreen(ref depthDesc, k_DepthStencilFormat, renderingData.cameraData.pixelWidth, renderingData.cameraData.pixelHeight);
                resourcesData.debugScreenDepth = CreateRenderGraphTexture(renderGraph, depthDesc, "_DebugScreenDepth", false);
            }

            // If the debugHandler displays HDR debug views, it needs to redirect (final) post-process output to an intermediate color target (debugScreenTexture)
            // and it will write into the post-process intended output.
            TextureHandle debugHandlerColorTarget = resourcesData.afterPostProcessColor;

            if (applyPostProcessing)
            {
                TextureHandle activeColor = resourcesData.activeColorTexture;
                TextureHandle backbuffer = resourcesData.backBufferColor;
                TextureHandle internalColorLut = resourcesData.internalColorLut;
                TextureHandle overlayUITexture = resourcesData.overlayUITexture;

                bool isTargetBackbuffer = (renderingData.cameraData.resolveFinalTarget && !applyFinalPostProcessing && !hasPassesAfterPostProcessing);
                // if the postprocessing pass is trying to read and write to the same CameraColor target, we need to swap so it writes to a different target,
                // since reading a pass attachment is not possible. Normally this would be possible using temporary RenderGraph managed textures.
                // The reason why in this case we need to use "external" RTHandles is to preserve the results for camera stacking.
                // TODO RENDERGRAPH: Once all cameras will run in a single RenderGraph we can just use temporary RenderGraph textures as intermediate buffer.
                if (!isTargetBackbuffer)
                {
                    ImportResourceParams importColorParams = new ImportResourceParams();
                    importColorParams.clearOnFirstUse = true;
                    importColorParams.clearColor = Color.black;
                    importColorParams.discardOnLastUse = renderingData.cameraData.resolveFinalTarget;  // check if last camera in the stack

                    cameraColor = renderGraph.ImportTexture(nextRenderGraphCameraColorHandle, importColorParams);
                    resourcesData.cameraColor = cameraColor;
                }

                // Desired target for post-processing pass.
                var target = isTargetBackbuffer ? backbuffer : cameraColor;

                // but we may actually render to an intermediate texture if debug views are enabled.
                // In that case, DebugHandler will eventually blit DebugScreenTexture into AfterPostProcessColor.
                if (resolveToDebugScreen && isTargetBackbuffer)
                {
                    debugHandlerColorTarget = target;
                    target = resourcesData.debugScreenColor;
                }

                bool doSRGBEncoding = resolvePostProcessingToCameraTarget && needsColorEncoding;
                m_PostProcessPasses.postProcessPass.RenderPostProcessingRenderGraph(renderGraph, in activeColor, in internalColorLut, in overlayUITexture, in target, ref renderingData, applyFinalPostProcessing, resolveToDebugScreen, doSRGBEncoding);

                if (isTargetBackbuffer)
                {
                    resourcesData.activeColorID = UniversalResourcesData.ActiveID.BackBuffer;
                    resourcesData.activeDepthID = UniversalResourcesData.ActiveID.BackBuffer;
                }
            }

            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.AfterRenderingPostProcessing);

            if (applyFinalPostProcessing)
            {
                TextureHandle backbuffer = resourcesData.backBufferColor;
                TextureHandle overlayUITexture = resourcesData.overlayUITexture;

                // Desired target for post-processing pass.
                TextureHandle target = backbuffer;

                if (resolveToDebugScreen)
                {
                    debugHandlerColorTarget = target;
                    target = resourcesData.debugScreenColor;
                }

                m_PostProcessPasses.finalPostProcessPass.RenderFinalPassRenderGraph(renderGraph, in cameraColor, in overlayUITexture, in target, ref renderingData, needsColorEncoding);

                resourcesData.activeColorID = UniversalResourcesData.ActiveID.BackBuffer;
                resourcesData.activeDepthID = UniversalResourcesData.ActiveID.BackBuffer;
            }

            cameraTargetResolved =
                // final PP always blit to camera target
                applyFinalPostProcessing ||
                // no final PP but we have PP stack. In that case it blit unless there are render pass after PP
                (applyPostProcessing && !hasPassesAfterPostProcessing && !hasCaptureActions);

            // TODO RENDERGRAPH: we need to discuss and decide if RenderPassEvent.AfterRendering injected passes should only be called after the last camera in the stack
            RecordCustomRenderGraphPasses(renderGraph, ref renderingData, RenderPassEvent.AfterRendering);

            if (!isActiveTargetBackBuffer && renderingData.cameraData.resolveFinalTarget && !cameraTargetResolved)
            {
                TextureHandle backbuffer = resourcesData.backBufferColor;
                TextureHandle overlayUITexture = resourcesData.overlayUITexture;
                TextureHandle target = backbuffer;

                if (resolveToDebugScreen)
                {
                    debugHandlerColorTarget = target;
                    target = resourcesData.debugScreenColor;
                }

                // make sure we are accessing the proper camera color in case it was replaced by injected passes
                cameraColor = resourcesData.cameraColor;

                m_FinalBlitPass.Render(renderGraph, ref renderingData, cameraColor, target, overlayUITexture);
                resourcesData.activeColorID = UniversalResourcesData.ActiveID.BackBuffer;
                resourcesData.activeDepthID = UniversalResourcesData.ActiveID.BackBuffer;
            }

            // We can explicitely render the overlay UI from URP when HDR output is not enabled.
            // SupportedRenderingFeatures.active.rendersUIOverlay should also be set to true.
            bool shouldRenderUI = renderingData.cameraData.rendersOverlayUI;
            bool outputToHDR = renderingData.cameraData.isHDROutputActive;
            if (shouldRenderUI && !outputToHDR)
            {
                TextureHandle depthBuffer = resourcesData.backBufferDepth;
                TextureHandle target = resourcesData.backBufferColor;

                if (resolveToDebugScreen)
                {
                    debugHandlerColorTarget = target;
                    target = resourcesData.debugScreenColor;
                    depthBuffer = resourcesData.debugScreenDepth;
                }

                m_DrawOverlayUIPass.RenderOverlay(renderGraph, in target, in depthBuffer, ref renderingData);
            }

            if (debugHandler != null)
            {
                TextureHandle overlayUITexture = resourcesData.overlayUITexture;
                TextureHandle debugScreenTexture = resourcesData.debugScreenColor;

                debugHandler.Setup(ref renderingData);
                debugHandler.Render(renderGraph, ref renderingData, debugScreenTexture, overlayUITexture, debugHandlerColorTarget);
            }

#if UNITY_EDITOR
            if (renderingData.cameraData.isSceneViewCamera || (isGizmosEnabled && renderingData.cameraData.resolveFinalTarget))
            {
                TextureHandle cameraDepthTexture = resourcesData.cameraDepthTexture;
                m_FinalDepthCopyPass.CopyToDepth = true;
                m_FinalDepthCopyPass.MssaSamples = 0;
                m_FinalDepthCopyPass.Render(renderGraph, resourcesData.activeDepthTexture, cameraDepthTexture, ref renderingData, false, "Final Depth Copy");
            }
#endif
            if (drawGizmos)
                DrawRenderGraphGizmos(renderGraph, resourcesData.backBufferColor, resourcesData.activeDepthTexture, GizmoSubset.PostImageEffects, ref renderingData);
        }

        bool RequireDepthPrepass(ref RenderingData renderingData, RenderPassInputSummary renderPassInputs)
        {
            ref var cameraData = ref renderingData.cameraData;
            bool applyPostProcessing = ShouldApplyPostProcessing(ref renderingData);
            // If Camera's PostProcessing is enabled and if there any enabled PostProcessing requires depth texture as shader read resource (Motion Blur/DoF)
            bool cameraHasPostProcessingWithDepth = CameraHasPostProcessingWithDepth(ref renderingData);

            bool forcePrepass = (m_CopyDepthMode == CopyDepthMode.ForcePrepass);
            bool depthPrimingEnabled = IsDepthPrimingEnabled(ref cameraData);

            bool requiresDepthTexture = cameraData.requiresDepthTexture || renderPassInputs.requiresDepthTexture || depthPrimingEnabled;
            bool requiresDepthPrepass = (requiresDepthTexture || cameraHasPostProcessingWithDepth) && (!CanCopyDepth(ref cameraData) || forcePrepass);
            requiresDepthPrepass |= cameraData.isSceneViewCamera;
            // requiresDepthPrepass |= isGizmosEnabled;
            requiresDepthPrepass |= cameraData.isPreviewCamera;
            requiresDepthPrepass |= renderPassInputs.requiresDepthPrepass;
            requiresDepthPrepass |= renderPassInputs.requiresNormalsTexture;

            // Current aim of depth prepass is to generate a copy of depth buffer, it is NOT to prime depth buffer and reduce overdraw on non-mobile platforms.
            // When deferred renderer is enabled, depth buffer is already accessible so depth prepass is not needed.
            // The only exception is for generating depth-normal textures: SSAO pass needs it and it must run before forward-only geometry.
            // DepthNormal prepass will render:
            // - forward-only geometry when deferred renderer is enabled
            // - all geometry when forward renderer is enabled
            if (requiresDepthPrepass && this.renderingModeActual == RenderingMode.Deferred && !renderPassInputs.requiresNormalsTexture)
                requiresDepthPrepass = false;

            requiresDepthPrepass |= depthPrimingEnabled;
            return requiresDepthPrepass;
        }

        bool RequireDepthTexture(ref RenderingData renderingData, RenderPassInputSummary renderPassInputs, bool requiresDepthPrepass)
        {
            bool depthPrimingEnabled = IsDepthPrimingEnabled(ref renderingData.cameraData);
            bool requiresDepthTexture = renderingData.cameraData.requiresDepthTexture || renderPassInputs.requiresDepthTexture || depthPrimingEnabled;
            bool cameraHasPostProcessingWithDepth = CameraHasPostProcessingWithDepth(ref renderingData);

            var createDepthTexture = (requiresDepthTexture || cameraHasPostProcessingWithDepth) && !requiresDepthPrepass;
            createDepthTexture |= !renderingData.cameraData.resolveFinalTarget;
            // Deferred renderer always need to access depth buffer.
            createDepthTexture |= (renderingModeActual == RenderingMode.Deferred && !useRenderPassEnabled);
            // Some render cases (e.g. Material previews) have shown we need to create a depth texture when we're forcing a prepass.
            createDepthTexture |= depthPrimingEnabled;
            // TODO: seems like with mrt depth is not taken from first target. Investigate if this is needed
            createDepthTexture |= m_RenderingLayerProvidesRenderObjectPass;

            return createDepthTexture;
        }

        internal void SetRenderingLayersGlobalTextures(RenderGraph renderGraph)
        {
            ContextContainer frameData = resources.frameData;
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();

            RenderGraphUtils.SetGlobalTexture(renderGraph, m_RenderingLayersTextureName, resourcesData.renderingLayersTexture, "Set Rendering Layers Texture");

            if (renderingModeActual == RenderingMode.Deferred) // As this is requested by render pass we still want to set it
                RenderGraphUtils.SetGlobalTexture(renderGraph, "_CameraRenderingLayersTexture", resourcesData.renderingLayersTexture, "Set Deferred Rendering Layers Texture");
        }

        void CreateCameraDepthCopyTexture(RenderGraph renderGraph, RenderTextureDescriptor descriptor, bool isDepthTexture)
        {
            ContextContainer frameData = resources.frameData;
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();

            var depthDescriptor = descriptor;
            depthDescriptor.msaaSamples = 1;// Depth-Only pass don't use MSAA

            if (isDepthTexture)
            {
                depthDescriptor.graphicsFormat = GraphicsFormat.None;
                depthDescriptor.depthStencilFormat = k_DepthStencilFormat;
                depthDescriptor.depthBufferBits = k_DepthBufferBits;
            }
            else
            {
                depthDescriptor.graphicsFormat = GraphicsFormat.R32_SFloat;
                depthDescriptor.depthStencilFormat = GraphicsFormat.None;
                depthDescriptor.depthBufferBits = 0;
            }

            resourcesData.cameraDepthTexture = CreateRenderGraphTexture(renderGraph, depthDescriptor, "_CameraDepthTexture", true);
        }

        void CreateMotionVectorTextures(RenderGraph renderGraph, RenderTextureDescriptor descriptor)
        {
            ContextContainer frameData = resources.frameData;
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();

            var colorDesc = descriptor;
            colorDesc.graphicsFormat = MotionVectorRenderPass.k_TargetFormat; colorDesc.depthBufferBits = (int)DepthBits.None;
            colorDesc.msaaSamples = 1;  // Disable MSAA, consider a pixel resolve for half left velocity and half right velocity --> no velocity, which is untrue.
            resourcesData.motionVectorColor = CreateRenderGraphTexture(renderGraph, colorDesc, MotionVectorRenderPass.k_MotionVectorTextureName, true);

            var depthDescriptor = descriptor;
            depthDescriptor.graphicsFormat = GraphicsFormat.None;
            //TODO RENDERGRAPH: in some cornercases (f.e. rendering to targetTexture) this is needed. maybe this will be unnece
            depthDescriptor.depthBufferBits = depthDescriptor.depthBufferBits != 0 ? depthDescriptor.depthBufferBits : 32; depthDescriptor.msaaSamples = 1;
            resourcesData.motionVectorDepth = CreateRenderGraphTexture(renderGraph, depthDescriptor, MotionVectorRenderPass.k_MotionVectorDepthTextureName, true);
        }

        void CreateCameraNormalsTexture(RenderGraph renderGraph, RenderTextureDescriptor descriptor)
        {
            ContextContainer frameData = resources.frameData;
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();

            var normalDescriptor = descriptor;
            normalDescriptor.depthBufferBits = 0;
            // Never have MSAA on this depth texture. When doing MSAA depth priming this is the texture that is resolved to and used for post-processing.
            normalDescriptor.msaaSamples = useDepthPriming ? descriptor.msaaSamples : 1;// Depth-Only passes don't use MSAA, unless depth priming is enabled
            // Find compatible render-target format for storing normals.
            // Shader code outputs normals in signed format to be compatible with deferred gbuffer layout.
            // Deferred gbuffer format is signed so that normals can be blended for terrain geometry.
            var normalsName = this.renderingModeActual != RenderingMode.Deferred ? "_CameraNormalsTexture" : DeferredLights.k_GBufferNames[m_DeferredLights.GBufferNormalSmoothnessIndex];
            normalDescriptor.graphicsFormat = this.renderingModeActual != RenderingMode.Deferred ? DepthNormalOnlyPass.GetGraphicsFormat() : m_DeferredLights.GetGBufferFormat(m_DeferredLights.GBufferNormalSmoothnessIndex);
            resourcesData.cameraNormalsTexture = CreateRenderGraphTexture(renderGraph, normalDescriptor, normalsName, true);
        }

        void CreateRenderingLayersTexture(RenderGraph renderGraph, RenderTextureDescriptor descriptor)
        {
            if (m_RequiresRenderingLayer)
            {
                ContextContainer frameData = resources.frameData;
                UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();

                m_RenderingLayersTextureName = "_CameraRenderingLayersTexture";

                // TODO RENDERGRAPH: deferred optimization
                if (renderingModeActual == RenderingMode.Deferred && m_DeferredLights.UseRenderingLayers)
                    m_RenderingLayersTextureName = DeferredLights.k_GBufferNames[m_DeferredLights.GBufferRenderingLayers];

                RenderTextureDescriptor renderingLayersDescriptor = descriptor;
                renderingLayersDescriptor.depthBufferBits = 0;
                if (!m_RenderingLayerProvidesRenderObjectPass)
                    renderingLayersDescriptor.msaaSamples = 1;// Depth-Only pass don't use MSAA

                // Find compatible render-target format for storing normals.
                // Shader code outputs normals in signed format to be compatible with deferred gbuffer layout.
                // Deferred gbuffer format is signed so that normals can be blended for terrain geometry.
                if (renderingModeActual == RenderingMode.Deferred && m_RequiresRenderingLayer)
                    renderingLayersDescriptor.graphicsFormat = m_DeferredLights.GetGBufferFormat(m_DeferredLights.GBufferRenderingLayers); // the one used by the gbuffer.
                else
                    renderingLayersDescriptor.graphicsFormat = RenderingLayerUtils.GetFormat(m_RenderingLayersMaskSize);

                resourcesData.renderingLayersTexture = CreateRenderGraphTexture(renderGraph, renderingLayersDescriptor, m_RenderingLayersTextureName, true);
            }
        }

        void CreateAfterPostProcessTexture(RenderGraph renderGraph, RenderTextureDescriptor descriptor)
        {
            ContextContainer frameData = resources.frameData;
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();

            var desc = PostProcessPass.GetCompatibleDescriptor(descriptor, descriptor.width, descriptor.height, descriptor.graphicsFormat, DepthBits.None);
            resourcesData.afterPostProcessColor = CreateRenderGraphTexture(renderGraph, desc, "_AfterPostProcessTexture", true);
        }

        void DepthNormalPrepassRender(RenderGraph renderGraph, RenderPassInputSummary renderPassInputs, TextureHandle depthTarget, ref RenderingData renderingData)
        {
            UniversalResourcesData resourcesData = renderingData.frameData.Get<UniversalResourcesData>();

            if (m_RenderingLayerProvidesByDepthNormalPass)
            {
                m_DepthNormalPrepass.enableRenderingLayers = true;
                m_DepthNormalPrepass.renderingLayersMaskSize = m_RenderingLayersMaskSize;
            }
            else
            {
                m_DepthNormalPrepass.enableRenderingLayers = false;
            }

            if (renderingModeActual == RenderingMode.Deferred)
            {
                // Only render forward-only geometry, as standard geometry will be rendered as normal into the gbuffer.
                if (RenderPassEvent.AfterRenderingGbuffer <= renderPassInputs.requiresDepthNormalAtEvent &&
                    renderPassInputs.requiresDepthNormalAtEvent <= RenderPassEvent.BeforeRenderingOpaques)
                    m_DepthNormalPrepass.shaderTagIds = k_DepthNormalsOnly;

                // TODO RENDERGRAPH: commented this out since would be equivalent to the current behaviour? Double check
                //if (!m_RenderingLayerProvidesByDepthNormalPass)
                // renderingLayersTexture = resourcesData.gbuffer[m_DeferredLights.GBufferRenderingLayers]; // GBUffer texture here
            }

            TextureHandle normalsTexture = resourcesData.cameraNormalsTexture;
            TextureHandle renderingLayersTexture = resourcesData.renderingLayersTexture;
            m_DepthNormalPrepass.Render(renderGraph, normalsTexture, depthTarget, renderingLayersTexture, ref renderingData);

            if (m_RequiresRenderingLayer)
                SetRenderingLayersGlobalTextures(renderGraph);
        }
    }

    static class RenderGraphUtils
    {
        static private ProfilingSampler s_SetGlobalTextureProfilingSampler = new ProfilingSampler("Set Global Texture");

        internal const int GBufferSize = 7;
        internal const int DBufferSize = 3;
        internal const int LightTextureSize = 4;

        internal static void UseDBufferIfValid(IRasterRenderGraphBuilder builder, FrameResources resources)
        {
            ContextContainer frameData = resources.frameData;
            UniversalResourcesData resourcesData = frameData.Get<UniversalResourcesData>();

            TextureHandle[] dbufferHandles = resourcesData.dBuffer;
            for (int i = 0; i < DBufferSize; ++i)
            {
                TextureHandle dbuffer = dbufferHandles[i];
                if (dbuffer.IsValid())
                    builder.UseTexture(dbuffer);
            }
        }

        private class PassData
        {
            internal TextureHandle texture;
            internal string name;
            internal int nameID;
        }

        /// <summary>
        /// Records a RenderGraph pass that binds a global texture
        /// </summary>
        /// <param name="graph"></param>
        /// <param name="name"></param>
        /// <param name="texture"></param>
        /// <param name="passName"></param>
        public static void SetGlobalTexture(RenderGraph graph, string name, TextureHandle texture, string passName = "Set Global Texture")
        {
            using (var builder = graph.AddRasterRenderPass<PassData>(passName, out var passData, s_SetGlobalTextureProfilingSampler))
            {
                passData.texture = builder.UseTexture(texture, IBaseRenderGraphBuilder.AccessFlags.Read);
                passData.name = name;

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(data.name, data.texture);
                });
            }
        }

        /// <summary>
        /// Records a RenderGraph pass that binds a global texture
        /// </summary>
        /// <param name="graph"></param>
        /// <param name="nameID"></param>
        /// <param name="texture"></param>
        /// <param name="passName"></param>
        public static void SetGlobalTexture(RenderGraph graph, int nameID, TextureHandle texture, string passName = "Set Global Texture")
        {
            using (var builder = graph.AddRasterRenderPass<PassData>(passName, out var passData, s_SetGlobalTextureProfilingSampler))
            {
                passData.texture = builder.UseTexture(texture, IBaseRenderGraphBuilder.AccessFlags.Read);
                passData.nameID = nameID;

                builder.AllowPassCulling(false);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(data.nameID, data.texture);
                });
            }
        }
    }
    class ClearTargetsPass
    {
        static private ProfilingSampler s_ClearProfilingSampler = new ProfilingSampler("Clear Targets");

        private class PassData
        {
            internal TextureHandle color;
            internal TextureHandle depth;

            internal RTClearFlags clearFlags;
            internal Color clearColor;
        }

        internal static void Render(RenderGraph graph, TextureHandle colorHandle, TextureHandle depthHandle,
            CameraData cameraData)
        {
            RTClearFlags clearFlags = RTClearFlags.None;

            if (cameraData.renderType == CameraRenderType.Base)
                clearFlags = RTClearFlags.All;
            else if (cameraData.clearDepth)
                clearFlags = RTClearFlags.Depth;

            if (clearFlags != RTClearFlags.None)
                Render(graph, colorHandle, depthHandle, clearFlags, cameraData.backgroundColor);
        }

        internal static void Render(RenderGraph graph, TextureHandle colorHandle, TextureHandle depthHandle, RTClearFlags clearFlags, Color clearColor)
        {
            using (var builder = graph.AddRasterRenderPass<PassData>("Clear Targets Pass", out var passData, s_ClearProfilingSampler))
            {
                if (colorHandle.IsValid())
                    passData.color = builder.UseTextureFragment(colorHandle, 0, IBaseRenderGraphBuilder.AccessFlags.Write);

                if (depthHandle.IsValid())
                    passData.depth = builder.UseTextureFragmentDepth(depthHandle, IBaseRenderGraphBuilder.AccessFlags.Write);

                passData.clearFlags = clearFlags;
                passData.clearColor = clearColor;

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    context.cmd.ClearRenderTarget(data.clearFlags, data.clearColor, 1, 0);
                });
            }
        }
    }

}
