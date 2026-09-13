"""
generate_skybox.py
------------------------------------------------------------------
Generates a stylized "yellow sky, red clouds" skybox for Repsyche as a 2:1
equirectangular panorama.

This synthesizes the image directly as pixel data (via numpy, bundled with
Blender's Python) rather than rendering a World shader through a panoramic
camera. That was the first approach tried here, but Blender's Texture
Coordinate outputs (both "Generated" and "Window") turned out to behave
strangely for EQUIRECTANGULAR panoramic cameras specifically - the resulting
cloud noise only varied across roughly the left/right thirds of the image
and stayed flat through the middle, regardless of which coordinate type fed
the noise node. Direct pixel synthesis sidesteps that entirely and also
guarantees an exact seamless wrap at the left/right edge (built from
sine terms whose horizontal frequency is an integer number of full cycles
across the image width, so azimuth 0 and azimuth 360 mathematically
produce the identical value).

USAGE
    blender --background --python generate_skybox.py

Writes: Assets/Textures/Skybox_YellowRedClouds.png
------------------------------------------------------------------
"""

import bpy
import numpy as np

EXPORT_PATH = "J:/Repsyche/Repsyche/Assets/Textures/Skybox_YellowRedClouds.png"
RESOLUTION_X = 2048
RESOLUTION_Y = 1024
SEED = 77

HORIZON_COLOR = np.array([1.0, 0.30, 0.0])   # hot orange-red near the horizon
ZENITH_COLOR = np.array([1.0, 0.80, 0.0])    # saturated golden yellow overhead
NADIR_COLOR = np.array([0.35, 0.08, 0.0])    # dark red-brown below the horizon
CLOUD_COLOR = np.array([0.55, 0.0, 0.0])     # deep, saturated blood-red


def build_sky_gradient(height):
    """Vertical gradient: nadir (bottom) -> horizon (~40% up) -> zenith (top)."""
    t = np.linspace(0.0, 1.0, height)[:, None]  # 0 at top row, 1 at bottom row
    elevation = 1.0 - t  # 1 = zenith (top row), 0 = horizon-ish, negative below

    horizon_level = 0.35
    above = np.clip((elevation - horizon_level) / (1.0 - horizon_level), 0.0, 1.0)
    below = np.clip((horizon_level - elevation) / horizon_level, 0.0, 1.0)

    gradient = (
        ZENITH_COLOR[None, :] * above
        + HORIZON_COLOR[None, :] * (1.0 - above) * (1.0 - below)
        + NADIR_COLOR[None, :] * below
    )
    return np.broadcast_to(gradient[:, None, :], (height, 1, 3))


def build_cloud_mask(width, height, seed):
    """Fractal sum of sine terms, periodic in X by construction (integer horizontal
    frequencies), so the image wraps seamlessly for a 360-degree skybox."""
    rng = np.random.default_rng(seed)
    x = np.linspace(0.0, 2.0 * np.pi, width, endpoint=False)[None, :]
    y = np.linspace(0.0, 1.0, height)[:, None]

    mask = np.zeros((height, width))
    n_octaves = 10
    amplitude = 1.0
    total_amplitude = 0.0
    for i in range(n_octaves):
        fx = rng.integers(2, 5) * (i + 1)          # integer -> exact horizontal periodicity
        fy = rng.uniform(0.5, 2.5) * (i + 1) * 0.6
        phase_x = rng.uniform(0, 2 * np.pi)
        phase_y = rng.uniform(0, 2 * np.pi)
        mask += amplitude * np.sin(fx * x + phase_x) * np.sin(fy * y * 2 * np.pi + phase_y)
        total_amplitude += amplitude
        amplitude *= 0.55
    mask /= total_amplitude
    mask = (mask + 1.0) * 0.5  # normalize to 0..1

    # Sharpen into patchy clouds rather than a smooth wave field.
    threshold = 0.58
    mask = np.clip((mask - threshold) / (1.0 - threshold), 0.0, 1.0)
    mask = mask ** 0.6
    return mask


def build_image():
    sky = build_sky_gradient(RESOLUTION_Y)
    sky = np.broadcast_to(sky, (RESOLUTION_Y, RESOLUTION_X, 3)).copy()

    cloud_mask = build_cloud_mask(RESOLUTION_X, RESOLUTION_Y, SEED)

    # Fade clouds out toward the nadir so the lower hemisphere (rarely seen, but part
    # of the sphere) stays a clean gradient instead of red streaks underground.
    t = np.linspace(0.0, 1.0, RESOLUTION_Y)[:, None]
    elevation = 1.0 - t
    nadir_fade = np.clip((elevation + 0.1) / 0.5, 0.0, 1.0)
    cloud_mask *= nadir_fade

    cloud_mask3 = cloud_mask[:, :, None]
    final = sky * (1.0 - cloud_mask3) + CLOUD_COLOR[None, None, :] * cloud_mask3
    final = np.clip(final, 0.0, 1.0)
    return final


def save_image(rgb):
    height, width = rgb.shape[:2]
    rgba = np.ones((height, width, 4), dtype=np.float32)
    rgba[:, :, :3] = rgb

    # Blender images are stored bottom-row-first.
    rgba = np.flipud(rgba)

    img = bpy.data.images.new("Skybox_YellowRedClouds", width=width, height=height, alpha=False)
    img.pixels.foreach_set(rgba.ravel())
    img.filepath_raw = EXPORT_PATH
    img.file_format = "PNG"
    img.save()
    print("[generate_skybox] Wrote {0}x{1} panorama to {2}".format(width, height, EXPORT_PATH))


def main():
    rgb = build_image()
    save_image(rgb)


if __name__ == "__main__":
    main()
