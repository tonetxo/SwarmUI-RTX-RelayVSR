using System;
using System.IO;
using System.Linq;
using FreneticUtilities.FreneticExtensions;
using SwarmUI.Accounts;
using SwarmUI.Core;
using SwarmUI.Media;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using SwarmUI.Builtin_ComfyUIBackend;
using Newtonsoft.Json.Linq;

namespace RelayVSRUpscale;

public class RelayVSRUpscaleExtension : Extension
{
    public static T2IParamGroup RelayVSRGroup;

    public static T2IRegisteredParam<double> RelayVSRScale;
    public static T2IRegisteredParam<double> RelayVSRDetail;
    public static T2IRegisteredParam<string> RelayVSRDownscaleMethod;

    /// <summary>Internal parameter set by the history button.</summary>
    public static T2IRegisteredParam<string> RelayVSRVideoFile;

    public override void OnInit()
    {
        // RelayVSRConditioning and VSRDecode are native ComfyUI nodes (comfy_extras/nodes_wan.py).
        ComfyUIBackendExtension.NodeToFeatureMap["RelayVSRConditioning"] = "relayvsr";
        ComfyUIBackendExtension.NodeToFeatureMap["VSRDecode"] = "relayvsr";

        ScriptFiles.Add("assets/relayvsr_upscale.js");

        RelayVSRGroup = new(
            Name: "RelayVSR Upscale",
            Description: "Video super resolution with RelayVSR (one-step diffusion model plus a Flash decoder, temporally stable).\n" +
                         "Needs the x2 and/or x4 models from huggingface.co/Comfy-Org/RelayVSR: relayvsr_x2/x4 in diffusion_models and relayvsr_flash_vae_x2/x4 in VAE.\n" +
                         "Can be enabled for video generation or used via the 'RelayVSR Upscale' history button.",
            Toggles: true,
            Open: false,
            OrderPriority: 8.2,
            IsAdvanced: false
        );
        double priority = 0;
        RelayVSRScale = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "RelayVSR Scale",
            Description: "Final scale. RelayVSR natively upscales x2 or x4 (each needs its own model and Flash decoder).\n" +
                         "Up to 2 uses the x2 model, above 2 the x4 model; other values downscale the native output (e.g. 1.5 = x2 then 0.75), which keeps more detail than upscaling less.",
            Default: "2",
            Min: 1.0,
            Max: 4.0,
            Step: 0.25,
            Group: RelayVSRGroup,
            OrderPriority: priority++,
            FeatureFlag: "relayvsr",
            DoNotPreview: true
        ));
        RelayVSRDetail = T2IParamTypes.Register<double>(new T2IParamType(
            Name: "RelayVSR Detail",
            Description: "How much of the detail the decoder adds on top of a bicubic upscale is kept. 1 is the full model output, lower values look softer.",
            Default: "1",
            Min: 0.0,
            Max: 1.0,
            Step: 0.05,
            ViewType: ParamViewType.SLIDER,
            Group: RelayVSRGroup,
            OrderPriority: priority++,
            FeatureFlag: "relayvsr",
            DoNotPreview: true
        ));
        RelayVSRDownscaleMethod = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "RelayVSR Downscale Method",
            Description: "Filter used when the scale is not exactly x2 or x4. 'area' averages pixels and is the cleanest for downscaling.",
            Default: "area",
            Group: RelayVSRGroup,
            OrderPriority: priority++,
            FeatureFlag: "relayvsr",
            DoNotPreview: true,
            IsAdvanced: true,
            GetValues: _ => ["area", "bicubic", "lanczos", "bilinear"]
        ));
        RelayVSRVideoFile = T2IParamTypes.Register<string>(new(
            "RelayVSR Video File",
            "Internal parameter for video upscaling from history.",
            "",
            IgnoreIf: "",
            FeatureFlag: "relayvsr",
            ChangeWeight: 2,
            VisibleNormally: false,
            DoNotSave: true,
            Nonreusable: true,
            HideFromMetadata: true
        ));

        // Priority -1: standalone upscale of a history video.
        WorkflowGenerator.AddStep(GenerateHistoryVideoWorkflow, -1);
        // Priority 91: after RTX frame generation (80) and RTX upscale (90), before the final save (100).
        WorkflowGenerator.AddStep(GenerateVideoGenerationWorkflow, 91);
    }

    /// <summary>Resolves an 'Output/...' or 'View/(user)/...' history path to a file inside the user's own output root, rejecting anything else.</summary>
    private static string ResolveVideoPath(User user, string videoFile)
    {
        if (user is null)
        {
            throw new SwarmUserErrorException("RelayVSR: processing a history video requires a user session.");
        }
        videoFile = videoFile.Replace('\\', '/');
        string relativePath = null;
        if (videoFile.StartsWith("Output/"))
        {
            relativePath = videoFile["Output/".Length..];
        }
        else if (videoFile.StartsWith("View/"))
        {
            string afterView = videoFile["View/".Length..];
            int slashIndex = afterView.IndexOf('/');
            if (slashIndex > 0)
            {
                relativePath = afterView[(slashIndex + 1)..];
            }
        }
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new SwarmUserErrorException("RelayVSR: only videos from your own output history can be processed.");
        }
        string root = Path.GetFullPath(Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, user.OutputDirectory));
        string full = Path.GetFullPath(Utilities.CombinePathWithAbsolute(root, relativePath));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar))
        {
            Logs.Warning($"RelayVSR: rejected path '{videoFile}' outside of the user output root.");
            throw new SwarmUserErrorException("RelayVSR: only videos from your own output history can be processed.");
        }
        return UserImageHistoryHelper.GetRealPathFor(user, full, root: root);
    }

    /// <summary>Finds a model by file name prefix (case-insensitive) in a Swarm model set.</summary>
    private static T2IModel FindModel(string modelSet, string prefix)
    {
        return Program.T2IModelSets[modelSet].Models.Values
            .Where(m => m.Name.AfterLast('/').ToLowerFast().StartsWith(prefix))
            .OrderBy(m => m.Name)
            .FirstOrDefault();
    }

    /// <summary>Adds the RelayVSR nodes after 'images' and returns the upscaled frames.</summary>
    private static JArray AddRelayVSR(WorkflowGenerator g, JArray images)
    {
        if (!g.Features.Contains("relayvsr"))
        {
            throw new SwarmUserErrorException("RelayVSR needs a ComfyUI version with the native RelayVSRConditioning and VSRDecode nodes: update ComfyUI.");
        }
        double scale = g.UserInput.Get(RelayVSRScale, 2.0);
        double detail = g.UserInput.Get(RelayVSRDetail, 1.0);
        // The UNet decides the factor and the Flash decoder must match it, so models are picked as a pair.
        int factor = scale > 2 ? 4 : 2;
        T2IModel unet = FindModel("Stable-Diffusion", $"relayvsr_x{factor}");
        T2IModel vae = FindModel("VAE", $"relayvsr_flash_vae_x{factor}");
        if (unet is null || vae is null)
        {
            factor = factor == 2 ? 4 : 2;
            unet = FindModel("Stable-Diffusion", $"relayvsr_x{factor}");
            vae = FindModel("VAE", $"relayvsr_flash_vae_x{factor}");
        }
        if (unet is null || vae is null)
        {
            throw new SwarmUserErrorException("RelayVSR models not found: put relayvsr_x2/x4 in diffusion_models and relayvsr_flash_vae_x2/x4 in VAE (huggingface.co/Comfy-Org/RelayVSR), then refresh the models.");
        }
        Logs.Info($"RelayVSR: x{factor} with {unet.Name} + {vae.Name}, final scale {scale}, detail {detail}");
        string unetNode = g.CreateNode("UNETLoader", new JObject()
        {
            ["unet_name"] = unet.ToString(g.ModelFolderFormat),
            ["weight_dtype"] = "default"
        });
        string vaeNode = g.CreateNode("VAELoader", new JObject()
        {
            ["vae_name"] = vae.ToString(g.ModelFolderFormat)
        });
        string condNode = g.CreateNode("RelayVSRConditioning", new JObject()
        {
            ["model"] = new JArray() { unetNode, 0 },
            ["images"] = images
        });
        // RelayVSR samples a single step at cfg 1, the seed does not matter.
        string samplerNode = g.CreateNode("KSampler", new JObject()
        {
            ["model"] = new JArray() { unetNode, 0 },
            ["positive"] = new JArray() { condNode, 0 },
            ["negative"] = new JArray() { condNode, 0 },
            ["latent_image"] = new JArray() { condNode, 1 },
            ["seed"] = 42,
            ["steps"] = 1,
            ["cfg"] = 1,
            ["sampler_name"] = "euler",
            ["scheduler"] = "simple",
            ["denoise"] = 1
        });
        string decodeNode = g.CreateNode("VSRDecode", new JObject()
        {
            ["vae"] = new JArray() { vaeNode, 0 },
            ["samples"] = new JArray() { samplerNode, 0 },
            ["images"] = images,
            ["detail"] = detail
        });
        JArray output = new() { decodeNode, 0 };
        if (Math.Abs(scale - factor) > 1e-6)
        {
            string scaleNode = g.CreateNode("ImageScaleBy", new JObject()
            {
                ["image"] = output,
                ["upscale_method"] = g.UserInput.Get(RelayVSRDownscaleMethod, "area"),
                ["scale_by"] = scale / factor
            });
            output = new JArray() { scaleNode, 0 };
        }
        return output;
    }

    /// <summary>Generates a complete workflow that upscales a video from history.</summary>
    public static void GenerateHistoryVideoWorkflow(WorkflowGenerator g)
    {
        if (!g.UserInput.TryGet(RelayVSRVideoFile, out string videoFile) || string.IsNullOrEmpty(videoFile))
        {
            return;
        }
        // The history button sends the full UI state: remove the file so it does not leak into metadata or later steps.
        g.UserInput.Remove(RelayVSRVideoFile);
        string resolvedPath = ResolveVideoPath(g.UserInput.SourceSession?.User, videoFile);
        if (!File.Exists(resolvedPath))
        {
            throw new SwarmUserErrorException($"RelayVSR: video file not found: {resolvedPath}");
        }
        Logs.Info($"RelayVSR: upscaling history video '{resolvedPath}'");
        VideoFile sourceVideo = new(File.ReadAllBytes(resolvedPath), MediaType.GetByExtension(resolvedPath.AfterLast('.').ToLowerFast()));
        WGNodeData videoData = g.LoadVideo(sourceVideo, "${relayvsr_source_video}", false);
        JArray upscaled = AddRelayVSR(g, videoData.Path);
        string createVideoNode = g.CreateNode("CreateVideo", new JObject()
        {
            ["images"] = upscaled,
            ["audio"] = videoData.AttachedAudio?.Path,
            ["fps"] = videoData.FPS
        });
        g.CreateNode("SaveVideo", new JObject()
        {
            ["video"] = new JArray() { createVideoNode, 0 },
            ["filename_prefix"] = "video/RelayVSR_upscaled",
            ["format"] = "mp4",
            ["codec"] = "auto"
        });
        g.CurrentMedia = new WGNodeData(upscaled, g, WGNodeData.DT_IMAGE, g.CurrentCompat());
        g.SkipFurtherSteps = true;
    }

    private static bool IsVideoGenerationRequest(WorkflowGenerator g)
    {
        return g.IsVideoModel()
            || g.UserInput.TryGet(T2IParamTypes.VideoModel, out _)
            || g.UserInput.Get(T2IParamTypes.Prompt, "").Contains("<extend:")
            || (g.CurrentMedia is not null && (g.CurrentMedia.DataType == WGNodeData.DT_VIDEO || g.CurrentMedia.DataType == WGNodeData.DT_LATENT_VIDEO || g.CurrentMedia.DataType == WGNodeData.DT_LATENT_AUDIOVIDEO));
    }

    /// <summary>Upscales the frames of a video generation (Priority 91).</summary>
    public static void GenerateVideoGenerationWorkflow(WorkflowGenerator g)
    {
        // RelayVSRScale is only present when the 'RelayVSR Upscale' group is enabled.
        if (!g.UserInput.TryGet(RelayVSRScale, out _) || !IsVideoGenerationRequest(g) || g.CurrentMedia is null)
        {
            return;
        }
        g.CurrentMedia = g.CurrentMedia.AsRawImage(g.CurrentVae);
        JArray decodedFrames = g.CurrentMedia.Path;
        JArray upscaled = AddRelayVSR(g, decodedFrames);
        g.CurrentMedia = g.CurrentMedia.WithPath(upscaled);
        // Saves already wired to the decoded frames now take the upscaled ones.
        string originalNodeId = decodedFrames[0].ToString();
        int originalOutputIndex = decodedFrames[1].Value<int>();
        foreach (string saveClass in new[] { "SwarmSaveAnimationWS", "SwarmVideoBoomerang" })
        {
            g.RunOnNodesOfClass(saveClass, (nodeId, nodeData) =>
            {
                if (nodeData["inputs"]?["images"] is JArray imagesInput && imagesInput[0].ToString() == originalNodeId && imagesInput[1].Value<int>() == originalOutputIndex)
                {
                    nodeData["inputs"]["images"] = upscaled;
                }
            });
        }
    }
}
