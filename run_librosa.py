from __future__ import annotations

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
    a = lpc(frame, order=order)

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
    return freqs[:max_formants], bws[:max_formants], sr, a
    
class LibrosaError(Exception):
    """Root exception class (mirrors librosa.util.exceptions.LibrosaError)."""


class ParameterError(LibrosaError):
    """Exception for malformed inputs (mirrors librosa.util.exceptions.ParameterError)."""


def is_positive_int(x) -> bool:
    """Return True iff `x` is an integer >= 1 (mirrors librosa.util.is_positive_int)."""
    return isinstance(x, (int, np.integer)) and (x > 0)


def valid_audio(y: np.ndarray) -> bool:
    """Validate audio buffer (subset of librosa.util.valid_audio).

    Conditions:
    - y is a numpy.ndarray
    - y is floating-point
    - y is at least 1D
    - y is finite everywhere
    """
    if not isinstance(y, np.ndarray):
        raise ParameterError("Audio data must be of type numpy.ndarray")

    if not np.issubdtype(y.dtype, np.floating):
        raise ParameterError("Audio data must be floating-point")

    if y.ndim == 0:
        raise ParameterError(
            f"Audio data must be at least one-dimensional, given y.shape={y.shape}"
        )

    if not np.isfinite(y).all():
        raise ParameterError("Audio buffer is not finite everywhere")

    return True


def tiny(x) -> float:
    """Smallest positive usable number for x's dtype (mirrors librosa.util.tiny)."""
    x = np.asarray(x)

    if np.issubdtype(x.dtype, np.floating) or np.issubdtype(x.dtype, np.complexfloating):
        dtype = x.dtype
    else:
        dtype = np.dtype(np.float32)

    return float(np.finfo(dtype).tiny)


# --- Burg LPC (adapted from librosa.core.audio.lpc) ------------------------

def lpc(y: np.ndarray, *, order: int, axis: int = -1) -> np.ndarray:
    """Linear Prediction Coefficients via Burg's method.

    Parameters
    ----------
    y : np.ndarray [shape=(..., n)]
        Time series to fit. Multi-channel is supported.
    order : int > 0
        Order of the linear prediction filter.
    axis : int
        Axis along which to compute the coefficients.

    Returns
    -------
    a : np.ndarray [shape=(..., order + 1)]
        LP prediction error coefficients (AR denominator polynomial).
        Length along `axis` will be `order + 1`.

    Raises
    ------
    ParameterError
        - If `y` is not valid (see `valid_audio`)
        - If `order` is not a positive integer
    FloatingPointError
        - If numerical instability is detected
    """

    if not is_positive_int(order):
        raise ParameterError(f"order={order} must be an integer > 0")

    valid_audio(y)

    # Move the target axis to the front
    y_swapped = np.swapaxes(y, axis, 0)

    n = y_swapped.shape[0]
    if n < order + 1:
        raise ParameterError(
            f"Input is too short for order={order}. "
            f"Need at least {order+1} samples along axis {axis}, got {n}."
        )

    dtype = y_swapped.dtype

    # Allocate coefficient arrays: shape (order+1, ...)
    out_shape = (order + 1,) + y_swapped.shape[1:]
    ar_coeffs = np.zeros(out_shape, dtype=dtype)
    ar_coeffs[0] = 1
    ar_coeffs_prev = ar_coeffs.copy()

    # Reflection coefficient and denominator are shape (1, ...)
    rc_shape = (1,) + y_swapped.shape[1:]
    reflect_coeff = np.zeros(rc_shape, dtype=dtype)
    den = np.zeros(rc_shape, dtype=dtype)

    epsilon = tiny(den)

    # Forward/backward prediction errors
    fwd_pred_error = y_swapped[1:]
    bwd_pred_error = y_swapped[:-1]

    # DEN_M from Marple eqn 16
    den[0] = np.sum(fwd_pred_error**2 + bwd_pred_error**2, axis=0)

    for i in range(order):
        # Reflection coefficient (Marple eqn 15)
        reflect_coeff[0] = np.sum(bwd_pred_error * fwd_pred_error, axis=0)
        reflect_coeff[0] *= -2
        reflect_coeff[0] /= den[0] + epsilon

        # Levinson-Durbin recursion (Marple eqn 5)
        ar_coeffs_prev, ar_coeffs = ar_coeffs, ar_coeffs_prev
        for j in range(1, i + 2):
            ar_coeffs[j] = (
                ar_coeffs_prev[j] + reflect_coeff[0] * ar_coeffs_prev[i - j + 1]
            )

        # Update forward/backward prediction errors (Marple eqn 13/14)
        fwd_pred_error_tmp = fwd_pred_error
        fwd_pred_error = fwd_pred_error + reflect_coeff * bwd_pred_error
        bwd_pred_error = bwd_pred_error + reflect_coeff * fwd_pred_error_tmp

        # DEN recursion (Marple eqn 17)
        q = 1.0 - reflect_coeff[0] ** 2
        den[0] = q * den[0] - bwd_pred_error[-1] ** 2 - fwd_pred_error[0] ** 2

        # A light numerical sanity check
        if not np.isfinite(den).all():
            raise FloatingPointError(
                "numerical error in Burg recursion; input ill-conditioned?"
            )

        # Shift errors for next order
        fwd_pred_error = fwd_pred_error[1:]
        bwd_pred_error = bwd_pred_error[:-1]

    # Swap axis back
    return np.swapaxes(ar_coeffs, 0, axis)


__all__ = ["lpc", "ParameterError", "LibrosaError"]

# Example
F, BW, sr_used, a = lpc_formants_burg_like_praat(
    "C:\\Users\\mihai\\Downloads\\My_record.wav",
    formant_ceiling_hz=5500.0,
    max_formants=5,
    window_length_s=0.025,
    preemph_from_hz=50.0,
)
print("sr_used:", sr_used)
print("Formants (Hz):", F)
print("Bandwidths (Hz):", BW)
print("A:", a)