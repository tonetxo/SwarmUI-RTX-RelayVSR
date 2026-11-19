# Video Upscale Extensions for SwarmUI

Video upscaling and frame interpolation for [SwarmUI](https://github.com/mcmonkeyprojects/SwarmUI). The folder holds two independent extensions, each with its own parameter group and history buttons:

- **RTX Video Upscale**: NVIDIA RTX Video Super Resolution and RTX Frame Generation.
- **RelayVSR Upscale**: diffusion-based video super resolution with RelayVSR.

## RTX Video Upscale & Frame Generation

NVIDIA RTX Video Super Resolution (VSR) and RTX Video Frame Generation (interpolation), using TensorRT and the NVIDIA RTX Video Effects SDK. Needs [Nvidia_RTX_Nodes_ComfyUI](https://github.com/Comfy-Org/Nvidia_RTX_Nodes_ComfyUI).

### Features

- **RTX Video Super Resolution (VSR)**:
  - Hardware-accelerated upscaling via NVIDIA RTX tensor cores.
  - Full support for 19 SDK quality levels, including `HIGHBITRATE_ULTRA` (recommended for clean AI video output), `HIGHBITRATE_HIGH`, `ULTRA`, `HIGH`, `DENOISE_*`, and `DEBLUR_*`.
  - Resize modes: Scale multiplier (1.0x - 4.0x) or exact target dimensions (Width x Height).
  - Configurable effect strength and color precision (8-bit RGB or 10-bit RGB).
- **RTX Video Frame Generation**:
  - Intermediate frame interpolation directly on NVIDIA RTX hardware.
  - Multiplier support (2x to 16x frame rate expansion).
  - FPS scaling to preserve original video duration while achieving maximum smoothness.
  - Quality modes: `LOW`, `MEDIUM`, `HIGH`.
  - Automatic shot change detection to prevent blending across hard cuts.
- **Interactive Output History Actions**:
  - `RTX Upscale` button in video context menus to upscale existing videos.
  - `RTX Interpolate` button in video context menus to interpolate existing videos.

## RelayVSR Upscale

Video super resolution with [RelayVSR](https://github.com/kopperx/RelayVSR) through the native ComfyUI nodes `RelayVSRConditioning` and `VSRDecode` (one diffusion step plus the RelayVSR Flash decoder). Temporally stable, and lighter on VRAM than SeedVR2.

### Models

From [Comfy-Org/RelayVSR](https://huggingface.co/Comfy-Org/RelayVSR):

| Factor | `diffusion_models` | `VAE` |
|---|---|---|
| x2 | `relayvsr_x2_lightx2v_int8_convrot.safetensors` | `relayvsr_flash_vae_x2_fp32.safetensors` |
| x4 | `relayvsr_x4_int8_convrot.safetensors` | `relayvsr_flash_vae_x4_fp32.safetensors` |

The UNet sets the factor and the Flash decoder must match it, so the extension always loads them as a pair (found by file name). Install at least one pair.

### Use

- **During video generation**: enable the **RelayVSR Upscale** group. It runs after RTX Frame Generation / RTX Upscale, if those are enabled too.
- **On an existing video**: the **RelayVSR Upscale** button in the history menu, with the panel values (the group toggle can stay off).

Parameters:

- **Scale** (1–4): up to 2 uses x2, above 2 uses x4. Other values downscale the native output (1.5 = x2 then 0.75), which keeps more detail than upscaling less.
- **Detail** (0–1): how much of what the decoder adds over a bicubic upscale is kept.
- **Downscale Method** (advanced): filter for non-native scales, `area` by default.

Requires a ComfyUI recent enough to include the RelayVSR nodes.
