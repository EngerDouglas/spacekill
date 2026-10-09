"""
Generates the seamless detail textures used by the planet ground shader (OrbitRush/PlanetGround):
Ground_Forest.png, Ground_Desert.png, Ground_Asphalt.png (1024x1024). They are mid-grey "overlay" textures (mean ~0.5):
the shader multiplies the planet's base colour by 2*detail, so they add grain and variation without changing the overall colour.
All noise is built in the Fourier domain, so every texture tiles perfectly. Run with:  python make_ground_textures.py <outdir>
"""
import sys, os
import numpy as np, cv2

N = 1024

def band_noise(seed, fmin, fmax, power=1.0):
    """Periodic noise with energy between fmin and fmax cycles per tile (white noise through a band-pass filter)."""
    rng = np.random.default_rng(seed)
    spec = np.fft.rfft2(rng.standard_normal((N, N)))
    fy = np.fft.fftfreq(N)[:, None] * N
    fx = np.fft.rfftfreq(N)[None, :] * N
    f = np.sqrt(fx * fx + fy * fy)
    filt = ((f >= fmin) & (f <= fmax)) * (1.0 / np.maximum(f, 1.0)) ** power
    out = np.fft.irfft2(spec * filt, s=(N, N))
    return (out - out.mean()) / (out.std() + 1e-9)

def norm01(a, lo=-2.5, hi=2.5): return np.clip((a - lo) / (hi - lo), 0, 1)

def to_img(rgb):
    return (np.clip(rgb, 0, 1) * 255).astype(np.uint8)

def forest():
    big = band_noise(1, 2, 7, 0.6)            # large patches
    mid = band_noise(2, 8, 40, 0.8)           # grass tufts / dirt patches
    fine = band_noise(3, 60, 260, 0.3)        # grain
    leaf = band_noise(4, 30, 120, 0.5)
    grass = norm01(big * 0.8 + mid * 0.6)     # 0 = bare dirt, 1 = grass
    lum = 0.5 + 0.055 * big + 0.05 * mid + 0.035 * fine
    r = lum * (1.0 + 0.07 * (1 - grass)) + 0.02 * np.clip(leaf, 0, 3) * (1 - grass)
    g = lum * (1.0 + 0.05 * grass)
    b = lum * (1.0 - 0.06 * grass - 0.02)
    return np.stack([r, g, b], -1)

def desert():
    warp = band_noise(5, 1, 5, 0.8)
    big = band_noise(6, 2, 8, 0.7)
    ys, xs = np.mgrid[0:N, 0:N] / N
    ripples = np.sin(2 * np.pi * (18 * xs + 11 * ys + 0.45 * warp))            # integer cycle counts per axis keep it seamless
    ripples2 = np.sin(2 * np.pi * (5 * xs - 6 * ys + 0.3 * band_noise(7, 1, 5, 0.8)))
    grain = band_noise(8, 120, 380, 0.2)
    lum = 0.5 + 0.035 * ripples + 0.03 * ripples2 + 0.05 * big + 0.04 * grain
    return np.stack([lum * 1.03, lum * 1.0, lum * 0.96], -1)

def asphalt():
    grain = band_noise(9, 90, 400, 0.15)
    blotch = band_noise(10, 3, 18, 0.8)
    ridge = 1 - np.abs(band_noise(11, 4, 30, 1.0)) * 0.8
    cracks = np.clip((ridge - 0.86) * 9, 0, 1)                     # thin dark lines where the ridged noise peaks
    pebbles = np.clip(band_noise(12, 150, 420, 0.1) - 1.2, 0, 2)
    lum = 0.5 + 0.06 * grain + 0.07 * blotch + 0.05 * pebbles - 0.30 * cracks
    return np.stack([lum, lum * 0.99, lum * 0.97], -1)

if __name__ == "__main__":
    outdir = sys.argv[1]
    os.makedirs(outdir, exist_ok=True)
    for name, fn in (("Ground_Forest", forest), ("Ground_Desert", desert), ("Ground_Asphalt", asphalt)):
        img = to_img(fn())
        cv2.imwrite(os.path.join(outdir, name + ".png"), cv2.cvtColor(img, cv2.COLOR_RGB2BGR))
        # seam check: difference across the wrap edge vs a normal neighbouring column
        edge = np.abs(img[:, 0].astype(float) - img[:, -1].astype(float)).mean()
        mid = np.abs(img[:, N // 2].astype(float) - img[:, N // 2 + 1].astype(float)).mean()
        print(f"{name}: mean={img.mean():.1f} wrap-edge diff={edge:.2f} vs neighbour diff={mid:.2f}")
