#!/usr/bin/env python3
"""Perigee Fuel Co. — the whole soundscape from arithmetic (GDD §18: ambient synth score, engine by air density, SFX list).

Music: four original ambient layers, all in the same key and tempo so any mix of them is a piece:
    music_pad     the calm orbit pad (always under a flight)
    music_swell   a rising swell for launches
    music_pulse   a low pulse for docking
    music_storm   a warning motif for storms
Engine: two loops mixed by air pressure — a full-band roar in air, a muffled structural rumble in vacuum.
SFX: staging bang, docking clamps, undock, pump flow (loop), radio beeps, reentry crackle (loop), parachute snap,
     legs, touchdown thud, crash, cash chime, warning tone, UI click.

    uv run --with numpy python3 tools/audio/build.py           # writes assets/audio/*.ogg + manifest.json
    uv run --with numpy python3 tools/audio/build.py --check   # same seed twice → identical bytes

Needs ffmpeg (libvorbis). Deterministic: seeded, so the files are reproducible. Everything here is original; nothing is licensed.
"""
from __future__ import annotations

import hashlib
import json
import math
import os
import subprocess
import sys
import tempfile
import wave

import numpy as np

SR = 44100
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, "assets", "audio")
KEY = 45  # A2: everything sits on an A minor drone
BPM = 60
LOOP_S = 32.0  # music loops are 32 s = 8 bars at 60 bpm


def hz(midi: float) -> float:
    return 440.0 * 2 ** ((midi - 69) / 12)


def t_axis(seconds: float) -> np.ndarray:
    return np.arange(int(seconds * SR)) / SR


def lowpass(x: np.ndarray, cutoff: float) -> np.ndarray:
    """FFT brick-wall with a soft shoulder (no scipy on this box)."""
    n = len(x)
    spec = np.fft.rfft(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    gain = 1 / (1 + (f / max(cutoff, 1.0)) ** 4)
    return np.fft.irfft(spec * gain, n)


def highpass(x: np.ndarray, cutoff: float) -> np.ndarray:
    n = len(x)
    spec = np.fft.rfft(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    gain = 1 / (1 + (max(cutoff, 1.0) / np.maximum(f, 1e-3)) ** 4)
    return np.fft.irfft(spec * gain, n)


def normalize(x: np.ndarray, peak: float = 0.85) -> np.ndarray:
    m = float(np.max(np.abs(x))) if len(x) else 0.0
    return x * (peak / m) if m > 1e-9 else x


def stereo(mono: np.ndarray, width: float = 0.0, rng: np.random.Generator | None = None) -> np.ndarray:
    """Mono → stereo; a little decorrelated width from a short delay on one side."""
    if width <= 0:
        return np.stack([mono, mono], axis=1)
    d = int(0.012 * SR)
    right = np.concatenate([np.zeros(d), mono[:-d]]) if len(mono) > d else mono
    return np.stack([mono, (1 - width) * mono + width * right], axis=1)


def seamless(x: np.ndarray, fade: float = 0.5) -> np.ndarray:
    """Make a loop: cross-fade the tail into the head so the seam is inaudible."""
    n = int(fade * SR)
    if n * 2 >= len(x):
        return x
    head, tail = x[:n].copy(), x[-n:].copy()
    ramp = np.linspace(0, 1, n)
    if x.ndim == 2:
        ramp = ramp[:, None]
    x[:n] = head * ramp + tail * (1 - ramp)
    return x[:-n]


def write_wav(path: str, buf: np.ndarray) -> None:
    data = np.clip(buf, -1, 1)
    pcm = (data * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


def encode(wav: str, ogg: str) -> None:
    subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-c:a", "libvorbis", "-q:a", "5", ogg], check=True)


# ---------------------------------------------------------------------------------------------
# Instruments
# ---------------------------------------------------------------------------------------------


def pad_voice(freq: float, seconds: float, rng: np.random.Generator, detune: float = 0.4, breath: float = 0.15) -> np.ndarray:
    """A soft detuned-saw pad through a slow low-pass, with a touch of breath noise."""
    t = t_axis(seconds)
    sig = np.zeros(len(t))
    for cents in (-detune * 8, 0, detune * 8):
        f = freq * 2 ** (cents / 1200)
        ph = rng.uniform(0, 2 * math.pi)
        # band-limited saw from 6 partials
        for k in range(1, 7):
            sig += (1 / k) * np.sin(2 * math.pi * f * k * t + ph * k)
    sig /= 3
    noise = rng.standard_normal(len(t)) * breath
    sig = lowpass(sig + lowpass(noise, 900), 1200 + 400 * np.sin(2 * math.pi * 0.05 * t[0]))
    return sig


def swell_env(t: np.ndarray, period: float) -> np.ndarray:
    return 0.5 - 0.5 * np.cos(2 * math.pi * t / period)


def pluck(freq: float, seconds: float, rng: np.random.Generator) -> np.ndarray:
    t = t_axis(seconds)
    sig = np.sin(2 * math.pi * freq * t) + 0.3 * np.sin(2 * math.pi * freq * 2 * t) + 0.12 * np.sin(2 * math.pi * freq * 3 * t)
    return sig * np.exp(-t / (seconds * 0.35))


# ---------------------------------------------------------------------------------------------
# Music layers (each a 32 s loop in A minor at 60 bpm; any combination mixes)
# ---------------------------------------------------------------------------------------------


def music_pad(rng: np.random.Generator) -> np.ndarray:
    t = t_axis(LOOP_S)
    out = np.zeros(len(t))
    # Am — F — C — G over 8 bars (two bars each), voiced low and wide.
    chords = [[0, 3, 7, 12], [-4, 0, 3, 8], [-9, -5, 0, 7], [-2, 2, 5, 9]]
    bar = LOOP_S / 8
    for ci, ch in enumerate(chords):
        seg = np.zeros(len(t))
        for iv in ch:
            seg += pad_voice(hz(KEY + 12 + iv), LOOP_S, rng) * 0.5
        env = np.clip(1 - np.abs((t - (ci * 2 + 1) * bar) / (1.15 * bar)), 0, 1) ** 0.8
        out += seg * env
    out = lowpass(out, 1800)
    out *= 0.6 + 0.4 * swell_env(t, LOOP_S / 2)
    return normalize(seamless(stereo(out, 0.35, rng)), 0.55)


def music_swell(rng: np.random.Generator) -> np.ndarray:
    """Rising fifths and octaves with a slow filter open — the launch layer."""
    t = t_axis(LOOP_S)
    out = np.zeros(len(t))
    for i, iv in enumerate([0, 7, 12, 19, 24]):
        v = pad_voice(hz(KEY + 24 + iv), LOOP_S, rng, detune=0.6, breath=0.05)
        out += v * (0.9 - 0.12 * i) * (0.3 + 0.7 * np.clip((t - i * 2.5) / 12, 0, 1))
    shimmer = np.sin(2 * math.pi * hz(KEY + 48) * t) * 0.08 * swell_env(t, 8)
    out = highpass(out, 120) + shimmer
    out *= 0.4 + 0.6 * swell_env(t, LOOP_S)
    return normalize(seamless(stereo(out, 0.5, rng)), 0.5)


def music_pulse(rng: np.random.Generator) -> np.ndarray:
    """A low pulse on every beat with a soft sub, for docking."""
    t = t_axis(LOOP_S)
    out = np.zeros(len(t))
    beat = 60 / BPM
    n_hit = int(0.5 * SR)
    th = np.arange(n_hit) / SR
    hit = np.sin(2 * math.pi * hz(KEY - 12) * th + 6 * np.exp(-th * 30)) * np.exp(-th * 7)
    for b in range(int(LOOP_S / beat)):
        i = int(b * beat * SR)
        amp = 1.0 if b % 4 == 0 else 0.55
        out[i : i + n_hit] += hit[: len(out) - i] * amp
    sub = np.sin(2 * math.pi * hz(KEY - 24) * t) * 0.25
    out = lowpass(out + sub, 500)
    return normalize(seamless(stereo(out, 0.1, rng)), 0.6)


def music_storm(rng: np.random.Generator) -> np.ndarray:
    """A tritone warning motif that returns every four bars over a dark tremolo pad."""
    t = t_axis(LOOP_S)
    out = pad_voice(hz(KEY), LOOP_S, rng, detune=0.9, breath=0.3) * (0.6 + 0.4 * np.sin(2 * math.pi * 5.5 * t))
    motif = [0, 6, 0, 6, 1, 0]
    for rep in range(2):
        for i, iv in enumerate(motif):
            at = rep * 16 + 1 + i * 0.75
            n = int(2.5 * SR)
            tone = pluck(hz(KEY + 24 + iv), 2.5, rng) * 0.35
            j = int(at * SR)
            out[j : j + n] += tone[: len(out) - j]
    out = lowpass(out, 2500)
    return normalize(seamless(stereo(out, 0.3, rng)), 0.55)


# ---------------------------------------------------------------------------------------------
# Engine
# ---------------------------------------------------------------------------------------------


def engine_air(rng: np.random.Generator) -> np.ndarray:
    """Full-band roar: shaped noise with a 30 Hz combustion flutter and a bright crackle."""
    t = t_axis(6.0)
    n = rng.standard_normal(len(t))
    body = lowpass(n, 900) * (1 + 0.25 * np.sin(2 * math.pi * 31 * t)) + 0.25 * highpass(n, 3000) * (0.5 + 0.5 * rng.random(len(t)) ** 8)
    sub = np.sin(2 * math.pi * 44 * t + 3 * np.sin(2 * math.pi * 0.7 * t)) * 0.3
    return normalize(seamless(stereo(body + sub, 0.4, rng), 0.8), 0.7)


def engine_vac(rng: np.random.Generator) -> np.ndarray:
    """Muffled rumble carried through the structure: everything under 150 Hz, slow beating."""
    t = t_axis(6.0)
    n = rng.standard_normal(len(t))
    body = lowpass(n, 140) * (0.7 + 0.3 * np.sin(2 * math.pi * 1.3 * t))
    hum = np.sin(2 * math.pi * 55 * t) * 0.25 + np.sin(2 * math.pi * 82.5 * t) * 0.12
    return normalize(seamless(stereo(body + hum, 0.2, rng), 0.8), 0.55)


# ---------------------------------------------------------------------------------------------
# SFX
# ---------------------------------------------------------------------------------------------


def env_exp(n: int, tau: float) -> np.ndarray:
    return np.exp(-np.arange(n) / SR / tau)


def sfx(rng: np.random.Generator) -> dict[str, tuple[np.ndarray, bool]]:
    s: dict[str, tuple[np.ndarray, bool]] = {}

    def add(name: str, mono: np.ndarray, loop: bool = False, width: float = 0.0, peak: float = 0.8) -> None:
        buf = stereo(normalize(mono, peak), width, rng)
        if loop:
            buf = seamless(buf, 0.15)
        s[name] = (buf, loop)

    # staging bang: a pyro crack with a metallic ring
    n = int(0.9 * SR); t = np.arange(n) / SR
    crack = rng.standard_normal(n) * env_exp(n, 0.035)
    ring = sum(np.sin(2 * math.pi * f * t) * np.exp(-t * d) for f, d in ((640, 9), (1170, 14), (2210, 20))) * 0.3
    add("stage", highpass(crack, 300) + lowpass(crack, 400) * 0.6 + ring)

    # docking clamps: two metal clunks a fifth of a second apart
    n = int(0.8 * SR); t = np.arange(n) / SR
    clunk = np.zeros(n)
    for at, f in ((0.0, 210), (0.18, 160)):
        i = int(at * SR); m = n - i; tt = np.arange(m) / SR
        clunk[i:] += (np.sin(2 * math.pi * f * tt) * np.exp(-tt * 18) + rng.standard_normal(m) * np.exp(-tt * 60) * 0.5)
    add("dock", lowpass(clunk, 2500))

    # undock: a spring release and a soft push
    n = int(0.6 * SR); t = np.arange(n) / SR
    add("undock", lowpass(rng.standard_normal(n) * env_exp(n, 0.08), 1200) + np.sin(2 * math.pi * 330 * t) * np.exp(-t * 12) * 0.5)

    # pump flow (loop): filtered noise with a slow gurgle
    n = int(2.5 * SR); t = np.arange(n) / SR
    add("pump", lowpass(rng.standard_normal(n), 700) * (0.6 + 0.4 * np.sin(2 * math.pi * 2.3 * t)) + np.sin(2 * math.pi * 95 * t) * 0.15, loop=True, width=0.2, peak=0.5)

    # radio beeps: three short square blips
    n = int(0.55 * SR); t = np.arange(n) / SR
    beep = np.zeros(n)
    for k, at in enumerate((0.0, 0.16, 0.32)):
        i = int(at * SR); m = int(0.09 * SR); tt = np.arange(m) / SR
        beep[i : i + m] += np.sign(np.sin(2 * math.pi * (1180 if k < 2 else 1480) * tt)) * np.exp(-tt * 25)
    add("beep", lowpass(beep, 4000), peak=0.45)

    # reentry crackle (loop): dense random pops over a hiss
    n = int(3.0 * SR)
    pops = (rng.random(n) < 0.0025).astype(float) * rng.standard_normal(n)
    add("reentry", lowpass(rng.standard_normal(n), 3500) * 0.3 + highpass(np.convolve(pops, env_exp(int(0.004 * SR), 0.0015), "same"), 800), loop=True, width=0.5, peak=0.6)

    # parachute snap: a whip crack plus a canopy flap
    n = int(0.9 * SR); t = np.arange(n) / SR
    add("chute", highpass(rng.standard_normal(n) * env_exp(n, 0.02), 900) + lowpass(rng.standard_normal(n), 300) * (t > 0.08) * np.exp(-(t - 0.08) * 6) * 0.8)

    # legs: a short servo whine ending in a click
    n = int(0.7 * SR); t = np.arange(n) / SR
    servo = np.sin(2 * math.pi * (400 + 250 * t) * t) * (t < 0.5) * 0.5
    click = rng.standard_normal(n) * env_exp(n, 0.01)
    click = np.concatenate([np.zeros(int(0.5 * SR)), click[: n - int(0.5 * SR)]])
    add("legs", servo + click, peak=0.5)

    # touchdown thud and a crash
    n = int(0.8 * SR); t = np.arange(n) / SR
    add("touchdown", lowpass(rng.standard_normal(n) * env_exp(n, 0.06), 260) + np.sin(2 * math.pi * 60 * t) * np.exp(-t * 10) * 0.6)
    n = int(1.6 * SR); t = np.arange(n) / SR
    add("crash", lowpass(rng.standard_normal(n) * env_exp(n, 0.25), 1800) + sum(np.sin(2 * math.pi * f * t) * np.exp(-t * 6) for f in (90, 133, 210)) * 0.4)

    # cash chime: a bright two-note bell
    n = int(1.2 * SR); t = np.arange(n) / SR
    chime = sum(np.sin(2 * math.pi * hz(m) * t) * np.exp(-t * 4) for m in (81, 88)) * 0.5 + np.sin(2 * math.pi * hz(93) * t) * np.exp(-t * 5) * 0.2
    add("cash", chime, peak=0.5)

    # warning tone: a slow two-tone siren burst
    n = int(1.4 * SR); t = np.arange(n) / SR
    tone = np.sin(2 * math.pi * (520 + 180 * (np.sin(2 * math.pi * 2 * t) > 0)) * t) * (0.5 - 0.5 * np.cos(2 * math.pi * t / 1.4))
    add("warn", lowpass(tone, 3000), peak=0.45)

    # UI click
    n = int(0.08 * SR); t = np.arange(n) / SR
    add("click", highpass(rng.standard_normal(n) * env_exp(n, 0.006), 1500) + np.sin(2 * math.pi * 900 * t) * np.exp(-t * 90) * 0.4, peak=0.35)
    return s


# ---------------------------------------------------------------------------------------------


def build(check: bool) -> None:
    os.makedirs(OUT, exist_ok=True)
    manifest: dict = {"music": [], "engine": [], "sfx": []}
    rng = np.random.default_rng(20260923)
    files: dict[str, tuple[np.ndarray, bool]] = {
        "music_pad": (music_pad(rng), True),
        "music_swell": (music_swell(rng), True),
        "music_pulse": (music_pulse(rng), True),
        "music_storm": (music_storm(rng), True),
        "engine_air": (engine_air(rng), True),
        "engine_vac": (engine_vac(rng), True),
    }
    for name, (buf, loop) in sfx(rng).items():
        files["sfx_" + name] = (buf, loop)
    digest = hashlib.sha256()
    with tempfile.TemporaryDirectory() as tmp:
        for name, (buf, loop) in files.items():
            wav = os.path.join(tmp, name + ".wav")
            write_wav(wav, buf)
            digest.update(open(wav, "rb").read())
            if not check:
                encode(wav, os.path.join(OUT, name + ".ogg"))
            group = "music" if name.startswith("music") else "engine" if name.startswith("engine") else "sfx"
            manifest[group].append({"name": name, "loop": loop, "seconds": round(len(buf) / SR, 3), "peak": round(float(np.max(np.abs(buf))), 3)})
    if check:
        print(digest.hexdigest())
        return
    manifest["sha256_wav"] = digest.hexdigest()
    with open(os.path.join(OUT, "manifest.json"), "w") as f:
        json.dump(manifest, f, indent=2)
    print(f"wrote {len(files)} files to {OUT}")


if __name__ == "__main__":
    build("--check" in sys.argv)
