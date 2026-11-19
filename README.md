# RelayVSR Upscale for SwarmUI

Video super resolution with [RelayVSR](https://github.com/kopperx/RelayVSR) through the native ComfyUI nodes `RelayVSRConditioning` and `VSRDecode` (one diffusion step plus the RelayVSR Flash decoder). Temporally stable, and lighter on VRAM than SeedVR2.

## Models

From [Comfy-Org/RelayVSR](https://huggingface.co/Comfy-Org/RelayVSR):

| Factor | `diffusion_models` | `VAE` |
|---|---|---|
| x2 | `relayvsr_x2_lightx2v_int8_convrot.safetensors` | `relayvsr_flash_vae_x2_fp32.safetensors` |
| x4 | `relayvsr_x4_int8_convrot.safetensors` | `relayvsr_flash_vae_x4_fp32.safetensors` |

The UNet sets the factor and the Flash decoder must match it, so the extension always loads them as a pair (found by file name). Install at least one pair.

## Use

- **During video generation**: enable the **RelayVSR Upscale** group. It runs after RTX Frame Generation / RTX Upscale, if those are enabled too.
- **On an existing video**: the **RelayVSR Upscale** button in the history menu, with the panel values (the group toggle can stay off).

Parameters:

- **Scale** (1–4): up to 2 uses x2, above 2 uses x4. Other values downscale the native output (1.5 = x2 then 0.75), which keeps more detail than upscaling less.
- **Detail** (0–1): how much of what the decoder adds over a bicubic upscale is kept.
- **Downscale Method** (advanced): filter for non-native scales, `area` by default.

Requires a ComfyUI recent enough to include the RelayVSR nodes.
