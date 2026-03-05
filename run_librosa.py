import numpy as np
import librosa

def preemphasis_praat(x, sr, preemph_from_hz=50.0):
    # Praat-style pre-emphasis corresponds to a 1st-order highpass-ish filter:
    # y[n] = x[n] - a*x[n-1], with a = exp(-2*pi*fc/sr)
    a = float(np.exp(-2.0 * np.pi * preemph_from_hz / sr))
    y = np.empty_like(x)
    y[0] = x[0]
    y[1:] = x[1:] - a * x[:-1]
    return y

def lpc_formants_burg_like_praat(
    wav_path,
    formant_ceiling_hz=5500.0,   # Praat "Maximum formant"
    max_formants=5,              # Praat "Maximum number of formants"
    window_length_s=0.025,       # Praat default
    time_s=None,                 # analyze around this time; None -> middle
    preemph_from_hz=50.0,        # Praat default
):
    # Load mono
    x, sr = librosa.load(wav_path, sr=None, mono=True)

    # 1) Resample to 2 * ceiling (Praat does this) :contentReference[oaicite:3]{index=3}
    target_sr = int(round(2.0 * formant_ceiling_hz))
    if sr != target_sr:
        x = librosa.resample(x, orig_sr=sr, target_sr=target_sr)
        sr = target_sr

    # 2) Choose analysis frame
    N = int(round(window_length_s * sr))
    if N < 16:
        raise ValueError("Window too small.")
    if time_s is None:
        center = len(x) // 2
    else:
        center = int(round(time_s * sr))
    start = max(0, center - N // 2)
    frame = x[start:start + N]
    if len(frame) < N:
        frame = np.pad(frame, (0, N - len(frame)))

    # 3) Pre-emphasis (Praat) :contentReference[oaicite:4]{index=4}
    frame = preemphasis_praat(frame, sr, preemph_from_hz)

    # 4) Windowing — Praat uses Gaussian-like window :contentReference[oaicite:5]{index=5}
    # librosa doesn't ship that exact one; Gaussian is closer than Hamming.
    # (Praat's exact shape differs, but this is much closer than no/rect window.)
    n = np.arange(N)
    sigma = 0.4 * (N - 1) / 2.0
    w = np.exp(-0.5 * ((n - (N - 1) / 2.0) / sigma) ** 2)
    frame = frame * w

    # 5) LPC via Burg (librosa.lpc is Burg) :contentReference[oaicite:6]{index=6}
    # Praat: number of poles = 2 * max_formants :contentReference[oaicite:7]{index=7}
    order = int(2 * max_formants)
    a = librosa.lpc(frame, order=order)

    # 6) Roots -> formants + bandwidth
    roots = np.roots(a)
    roots = roots[np.imag(roots) > 0]  # one from each conjugate pair

    freqs = np.angle(roots) * (sr / (2 * np.pi))
    bws = -0.5 * (sr / np.pi) * np.log(np.abs(roots))

    # Keep plausible formants
    keep = (freqs > 50.0) & (freqs < formant_ceiling_hz) & (bws > 0) & (bws < 400)
    freqs = freqs[keep]
    bws = bws[keep]

    idx = np.argsort(freqs)
    freqs = freqs[idx]
    bws = bws[idx]

    # Return up to max_formants like Praat would display
    return freqs[:max_formants], bws[:max_formants], sr

# Example
F, BW, sr_used = lpc_formants_burg_like_praat(
    "C:\\Users\\mihai\\Downloads\\My_record.wav",
    formant_ceiling_hz=5500.0,
    max_formants=5,
    window_length_s=0.025,
    preemph_from_hz=50.0,
)
print("sr_used:", sr_used)
print("Formants (Hz):", F)
print("Bandwidths (Hz):", BW)