"""Tiny synthesizer: every sound and every note of music in Fusion Kingdoms is made here from scratch.

Run: training/.venv/bin/python tools/audio/synth.py  -> assets/audio/*.wav
"""
import os
import wave

import numpy as np

SR = 22050
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "assets", "audio")
rng = np.random.default_rng(7)


def t_of(dur):
    return np.arange(int(SR * dur)) / SR


def hz(note):
    """MIDI note -> frequency."""
    return 440.0 * 2 ** ((note - 69) / 12)


# ---------- oscillators ----------

def phase(freq, dur):
    f = np.broadcast_to(np.asarray(freq, dtype=float), (int(SR * dur),))
    return np.cumsum(f) / SR


def sine(freq, dur):
    return np.sin(2 * np.pi * phase(freq, dur))


def square(freq, dur, duty=0.5):
    return np.where((phase(freq, dur) % 1.0) < duty, 1.0, -1.0)


def tri(freq, dur):
    p = (phase(freq, dur) + 0.75) % 1.0  # starts at zero, so un-enveloped notes don't click
    return 4 * np.abs(p - 0.5) - 1


def saw(freq, dur):
    return 2 * (phase(freq, dur) % 1.0) - 1


def noise(dur):
    return rng.uniform(-1, 1, int(SR * dur))


# ---------- shaping ----------

def env(n, a=0.005, d=0.05, s=0.6, r=0.1, hold=None):
    """ADSR envelope over n samples. hold = sustain time; default fills the gap."""
    a_n, d_n, r_n = int(a * SR), int(d * SR), int(r * SR)
    h_n = max(0, n - a_n - d_n - r_n) if hold is None else int(hold * SR)
    e = np.concatenate([
        np.linspace(0, 1, a_n, endpoint=False),
        np.linspace(1, s, d_n, endpoint=False),
        np.full(h_n, s),
        np.linspace(s, 0, r_n),
    ])
    return np.pad(e, (0, max(0, n - len(e))))[:n]


def decay(n, k):
    return np.exp(-np.arange(n) / SR * k)


def lowpass(x, cutoff):
    """One-pole lowpass; cutoff may be an array (sweeps)."""
    c = np.broadcast_to(np.asarray(cutoff, dtype=float), x.shape)
    a = 1 - np.exp(-2 * np.pi * c / SR)
    y = np.empty_like(x)
    acc = 0.0
    for i in range(len(x)):
        acc += a[i] * (x[i] - acc)
        y[i] = acc
    return y


def highpass(x, cutoff):
    return x - lowpass(x, cutoff)


def reverb(x, mix=0.25, room=0.5):
    """A few feedback combs + allpass: enough to put sounds in a space."""
    y = np.zeros(len(x) + int(SR * 1.2))
    y[:len(x)] = x
    wet = np.zeros_like(y)
    for ms, g in [(29.7, 0.77), (37.1, 0.75), (41.1, 0.73), (43.7, 0.71)]:
        d = int(SR * ms / 1000)
        buf = y.copy()
        fb = g * room + 0.2
        for i in range(d, len(buf), d):
            buf[i:i + d] += buf[i - d:i][:len(buf[i:i + d])] * fb
        wet += buf - y
    wet = lowpass(wet / 4, 3500)
    return y * (1 - mix) + wet * mix


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[:len(p)] += p
    return out


def at(x, start, total=None):
    """Place x at time `start` inside silence."""
    s = int(start * SR)
    out = np.zeros(max(total and int(total * SR) or 0, s + len(x)))
    out[s:s + len(x)] += x
    return out


def normalize(x, peak=0.85):
    m = np.max(np.abs(x)) or 1
    return x / m * peak


def write(name, x, peak=0.85, trim=True):
    if trim:
        x = highpass(x, 25)  # pulse waves carry a DC offset
    else:
        x = x - x.mean()
    x = normalize(x, peak)
    if trim:
        nz = np.nonzero(np.abs(x) > 0.002)[0]
        x = x[:nz[-1] + 1] if len(nz) else x
        fade = min(len(x), int(SR * 0.01))
        x[-fade:] *= np.linspace(1, 0, fade)
    os.makedirs(OUT, exist_ok=True)
    data = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    print(f"  {name}.wav  {len(x) / SR:.2f}s")


# ---------- sound effects ----------

def blip(note, dur, wave_fn=square, duty=0.5, a=0.002, d=0.04, s=0.4, r=0.06, slide=0.0):
    f = hz(note) * (2 ** (slide * np.linspace(0, 1, int(SR * dur)) / 12))
    w = wave_fn(f, dur, duty) if wave_fn is square else wave_fn(f, dur)
    return w * env(len(w), a, d, s, r)


def sfx():
    # UI
    write("click", lowpass(blip(84, 0.05, square, 0.25, d=0.02, s=0.2, r=0.02), 6000), 0.5)
    write("hover", lowpass(blip(91, 0.03, sine, d=0.01, s=0.2, r=0.015), 8000), 0.25)
    write("error", mix(blip(52, 0.12, square, 0.5, slide=-2), at(blip(47, 0.16, square, 0.5, slide=-3), 0.1)) * 0.7, 0.5)
    write("select", mix(blip(76, 0.05, square, 0.125), at(blip(83, 0.07, square, 0.125), 0.045)), 0.45)
    write("order", mix(blip(72, 0.04, tri), at(blip(79, 0.06, tri), 0.035)), 0.55)

    # building
    def thunk(note, dur=0.18):
        body = sine(hz(note) * (1 + 1.5 * decay(int(SR * dur), 40)), dur) * decay(int(SR * dur), 22)
        click = lowpass(noise(0.02), 3000) * decay(int(SR * 0.02), 120)
        return mix(body, click * 0.6)

    write("place", reverb(mix(thunk(45), at(thunk(50), 0.11)), 0.2), 0.7)
    hammer = [at(mix(thunk(57, 0.12), lowpass(noise(0.05), 5000) * decay(int(SR * 0.05), 70) * 0.5), i * 0.16) for i in range(3)]
    write("hammer", reverb(mix(*hammer), 0.18), 0.6)
    done = mix(*[at(blip(n, 0.18, tri, d=0.05, s=0.5, r=0.12), i * 0.08) for i, n in enumerate([67, 71, 74, 79])])
    write("built", reverb(mix(done, thunk(43, 0.3) * 0.6), 0.3, 0.6), 0.7)

    # gathering
    chop = mix(lowpass(noise(0.07), 2200) * decay(int(SR * 0.07), 55), thunk(52, 0.08) * 0.7)
    write("chop", reverb(chop, 0.15), 0.55)
    swish = lowpass(highpass(noise(0.16), 1500), 6000) * env(int(SR * 0.16), 0.04, 0.04, 0.5, 0.08)
    write("harvest", swish, 0.4)
    ting = mix(sine(hz(88), 0.25) * decay(int(SR * 0.25), 18), sine(hz(88) * 2.76, 0.25) * decay(int(SR * 0.25), 30) * 0.4)
    write("mine", reverb(mix(ting, lowpass(noise(0.03), 4000) * 0.7), 0.2), 0.5)
    shimmer = mix(*[at(sine(hz(n), 0.4) * decay(int(SR * 0.4), 9), i * 0.05) for i, n in enumerate([84, 88, 91, 96])])
    write("lumen", reverb(shimmer, 0.4, 0.7), 0.4)
    coin = mix(blip(88, 0.06, square, 0.5, d=0.02, s=0.5, r=0.03), at(blip(95, 0.18, square, 0.5, d=0.05, s=0.4, r=0.12), 0.06))
    write("coin", lowpass(coin, 7000), 0.4)

    # conflict
    def scuffle(dur):
        n = int(SR * dur)
        thumps = np.zeros(n)
        for k in range(int(dur * 9)):
            start = rng.uniform(0, dur - 0.08)
            thumps = mix(thumps, at(thunk(rng.integers(38, 48), 0.09) * rng.uniform(0.5, 1), start))[:n]
        dust = lowpass(noise(dur), 900) * env(n, 0.05, 0.1, 0.6, 0.2)
        return mix(thumps, dust * 0.5)

    write("tussle", scuffle(0.7), 0.7)
    down = mix(thunk(36, 0.3), at(blip(76, 0.5, tri, slide=-14, d=0.1, s=0.5, r=0.3), 0.05) * 0.6,
               *[at(blip(n, 0.08, sine, s=0.3), 0.2 + i * 0.07) * 0.4 for i, n in enumerate([96, 100, 103])])
    write("subdue", reverb(down, 0.2), 0.7)
    cage = mix(*[at(thunk(n, 0.12), i * 0.09) for i, n in enumerate([50, 47])],
               at(ting * 0.6, 0.18))
    write("capture", reverb(cage, 0.25, 0.6), 0.65)
    rise = blip(60, 0.5, tri, a=0.01, d=0.1, s=0.7, r=0.15, slide=12)
    write("rescue", reverb(mix(rise, at(blip(84, 0.25, sine), 0.35) * 0.6), 0.3), 0.6)
    steal = mix(lowpass(noise(0.12), 2500) * env(int(SR * 0.12), 0.02, 0.03, 0.4, 0.05), at(coin, 0.06) * 0.6)
    write("steal", steal, 0.45)
    write("heal", reverb(mix(*[at(blip(n, 0.3, sine, a=0.03, s=0.6, r=0.2), i * 0.09) for i, n in enumerate([72, 76, 79, 84])]), 0.4, 0.7), 0.45)
    write("sabotage", reverb(mix(lowpass(noise(0.6), np.linspace(3000, 300, int(SR * 0.6))) * decay(int(SR * 0.6), 6), thunk(33, 0.4)), 0.3), 0.7)

    # minds
    n = int(SR * 1.6)
    t = t_of(1.6)
    sweep = np.linspace(0, 1, n)
    a = sine(hz(60) * (1 + sweep), 1.6)
    b = sine(hz(67) * (1 + sweep) * 1.003, 1.6)
    sparkle = mix(*[at(sine(hz(n2), 0.3) * decay(int(SR * 0.3), 12), 0.2 + i * 0.11) for i, n2 in enumerate([84, 88, 91, 95, 100, 103, 108, 103, 100])])
    fusion = mix((a + b) * env(n, 0.3, 0.3, 0.7, 0.6) * 0.5, sparkle * 0.5, at(blip(72, 0.8, tri, a=0.01, s=0.6, r=0.5), 1.1) * 0.5)
    write("fusion", reverb(fusion * (0.8 + 0.2 * np.sin(2 * np.pi * 6 * t[:len(fusion)] if len(fusion) <= n else 1)), 0.45, 0.8), 0.75)
    wobble = sine(hz(55) * (1 + 0.08 * np.sin(2 * np.pi * 7 * t_of(1.0))), 1.0) * env(int(SR * 1.0), 0.1, 0.2, 0.6, 0.4)
    weird = mix(wobble, square(hz(61) * (1 + 0.05 * np.sin(2 * np.pi * 11 * t_of(1.0))), 1.0, 0.3) * env(int(SR * 1.0), 0.2, 0.2, 0.3, 0.4) * 0.3)
    write("mutation", reverb(lowpass(weird, 2500), 0.4, 0.7), 0.6)
    cry = mix(blip(84, 0.15, sine, s=0.6), at(blip(91, 0.25, sine, s=0.5, r=0.15, slide=-2), 0.12))
    write("birth", reverb(mix(cry, at(blip(79, 0.4, tri, a=0.02), 0.0) * 0.3), 0.35, 0.6), 0.5)

    # alerts and big moments
    horn_n = int(SR * 0.9)
    horn = lowpass(mix(saw(hz(50), 0.9), saw(hz(50) * 1.005, 0.9), saw(hz(57), 0.9) * 0.6), 1200) * env(horn_n, 0.08, 0.1, 0.8, 0.3)
    write("alert", reverb(mix(horn, at(horn * 0.8, 0.95)), 0.3, 0.6), 0.7)
    fan = [(67, 0, 0.15), (72, 0.15, 0.15), (76, 0.3, 0.15), (79, 0.45, 0.6)]
    brass = mix(*[at(lowpass(mix(square(hz(nn), d, 0.25), saw(hz(nn), d) * 0.5), 3000) * env(int(SR * d), 0.01, 0.05, 0.7, 0.1), s) for nn, s, d in fan])
    drum = mix(*[at(thunk(40, 0.2), s) for s in (0, 0.45, 0.6)])
    write("ageup", reverb(mix(brass, drum * 0.8), 0.3, 0.7), 0.75)
    vic = [(72, 0, .15), (72, .15, .15), (72, .3, .15), (76, .45, .45), (74, .9, .2), (77, 1.1, .2), (79, 1.3, .8)]
    vbr = mix(*[at(lowpass(mix(square(hz(nn), d, 0.25), saw(hz(nn - 12), d) * 0.4), 3500) * env(int(SR * d), 0.01, 0.05, 0.7, 0.1), s) for nn, s, d in vic])
    write("victory", reverb(mix(vbr, *[at(thunk(38, 0.3), s) for s in (0, 0.45, 1.3)]), 0.3, 0.7), 0.75)
    dft = [(64, 0, .4), (63, .4, .4), (62, .8, .4), (61, 1.2, 1.2)]
    dbr = mix(*[at(lowpass(tri(hz(nn), d) + square(hz(nn - 12), d, 0.5) * 0.3, 1500) * env(int(SR * d), 0.03, 0.1, 0.7, 0.2), s) for nn, s, d in dft])
    write("defeat", reverb(dbr, 0.4, 0.8), 0.7)


# ---------- music ----------

class Song:
    def __init__(self, bpm, bars, beats=4):
        self.beat = 60 / bpm
        self.len = bars * beats * self.beat
        self.buf = np.zeros(int(SR * self.len) + SR * 2)

    def add(self, x, beat, gain=1.0):
        s = int(beat * self.beat * SR)
        x = x[:max(0, len(self.buf) - s)]
        self.buf[s:s + len(x)] += x * gain

    def finish(self, wet, room):
        # Reverb first, then wrap the ringing tail back over the start so the loop is seamless.
        full = reverb(self.buf, wet, room)
        n = int(SR * self.len)
        out = full[:n].copy()
        for k in range(n, len(full), n):
            tail = full[k:k + n]
            out[:len(tail)] += tail
        return out


def note_tone(kind, note, dur):
    n = int(SR * dur)
    if kind == "lead":
        f = hz(note) * (1 + 0.004 * np.sin(2 * np.pi * 5.5 * t_of(dur)) * np.clip(t_of(dur) * 4 - 0.4, 0, 1))
        w = square(f, dur, 0.25) * 0.6 + square(f * 1.003, dur, 0.5) * 0.25
        return lowpass(w, 3200) * env(n, 0.01, 0.08, 0.6, min(0.12, dur * 0.4))
    if kind == "flute":
        f = hz(note) * (1 + 0.006 * np.sin(2 * np.pi * 5 * t_of(dur)))
        w = tri(f, dur) + sine(f * 2, dur) * 0.15 + lowpass(noise(dur), 2000) * 0.04
        return w * env(n, 0.06, 0.1, 0.75, min(0.25, dur * 0.5))
    if kind == "bass":
        return lowpass(tri(hz(note), dur) + square(hz(note), dur, 0.5) * 0.15, 900) * env(n, 0.005, 0.1, 0.7, 0.05)
    if kind == "pluck":
        w = square(hz(note), dur, 0.125) * decay(n, 7) + tri(hz(note) * 2, dur) * decay(n, 12) * 0.3
        return lowpass(w * np.clip(np.arange(n) / (SR * 0.004), 0, 1), np.linspace(4000, 800, n))
    if kind == "pad":
        w = sum(saw(hz(note) * (1 + d), dur) for d in (-0.006, 0, 0.007)) / 3
        return lowpass(w, 1100) * env(n, dur * 0.3, 0.1, 0.8, dur * 0.4)
    if kind == "bell":
        w = sine(hz(note), dur) * decay(n, 3) + sine(hz(note) * 2.76, dur) * decay(n, 7) * 0.3 + sine(hz(note) * 5.4, dur) * decay(n, 14) * 0.1
        return w
    raise ValueError(kind)


def drum(kind):
    if kind == "kick":
        d = 0.22
        n = int(SR * d)
        return sine(55 * (1 + 2.5 * decay(n, 30)), d) * decay(n, 14)
    if kind == "snare":
        d = 0.18
        n = int(SR * d)
        return highpass(noise(d), 900) * decay(n, 22) * 0.7 + tri(190, d) * decay(n, 30) * 0.4
    if kind == "hat":
        d = 0.05
        return highpass(noise(d), 6000) * decay(int(SR * d), 70) * 0.35
    if kind == "shaker":
        d = 0.08
        return highpass(noise(d), 5000) * env(int(SR * d), 0.02, 0.02, 0.4, 0.03) * 0.25
    raise ValueError(kind)


def chords_to_bass(song, chords, beats_per, pattern, gain):
    for i, root in enumerate(chords):
        for off, dur, octave in pattern:
            song.add(note_tone("bass", root + octave, dur * song.beat), i * beats_per + off, gain)


def melody(song, kind, notes, gain, start=0.0):
    """notes: list of (note or None, beats)."""
    b = start
    for nn, d in notes:
        if nn is not None:
            song.add(note_tone(kind, nn, d * song.beat * 0.95), b, gain)
        b += d


def day_theme():
    """A bright pastoral loop in D major: plucked arpeggios, a whistled tune, a walking bass."""
    s = Song(bpm=104, bars=16)
    D, G, A, Bm, Em = 50, 55, 57, 59, 52
    prog = [D, G, D, A, Bm, G, Em, A] * 2
    triads = {D: [0, 4, 7], G: [0, 4, 7], A: [0, 4, 7], Bm: [0, 3, 7], Em: [0, 3, 7]}
    chords_to_bass(s, prog, 4, [(0, 1, 0), (1.5, 0.5, 0), (2, 1, 7), (3, 1, 12)], 0.55)
    for i, root in enumerate(prog):
        tri_ = triads[root]
        arp = [root + 24 + tri_[k % 3] + 12 * (k // 3) for k in (0, 1, 2, 3, 2, 1, 0, 1)]
        for k, nn in enumerate(arp):
            s.add(note_tone("pluck", nn, 0.5 * s.beat), i * 4 + k * 0.5, 0.22)
    tune_a = [(74, 1), (76, 0.5), (78, 0.5), (79, 1), (78, 1), (76, 1.5), (74, 0.5), (73, 1), (69, 1),
              (71, 1), (74, 1), (76, 1), (78, 1), (76, 3), (None, 1),
              (78, 1), (79, 0.5), (81, 0.5), (83, 1), (81, 1), (79, 1), (78, 1), (76, 1), (74, 1),
              (71, 1), (73, 1), (74, 1), (76, 1), (74, 3), (None, 1)]
    melody(s, "flute", tune_a, 0.42, start=0)
    tune_b = [(None, 2), (81, 1), (83, 1), (86, 2), (83, 2), (81, 2), (78, 2), (79, 2), (81, 2),
              (83, 1), (81, 1), (79, 1), (78, 1), (76, 4),
              (78, 1), (76, 1), (74, 2), (73, 2), (74, 2)]
    melody(s, "lead", tune_b, 0.26, start=32)
    for bar in range(16):
        for k in range(8):
            s.add(drum("shaker"), bar * 4 + k * 0.5, 0.6 if k % 2 else 0.9)
        s.add(drum("kick"), bar * 4, 0.55)
        s.add(drum("kick"), bar * 4 + 2.5, 0.35)
        if bar >= 8:
            s.add(drum("snare"), bar * 4 + 1, 0.35)
            s.add(drum("snare"), bar * 4 + 3, 0.35)
    return s.finish(0.22, 0.6)


def night_theme():
    """A slow, hushed loop in A minor: pads, music-box bells, soft low pulse."""
    s = Song(bpm=72, bars=16)
    Am, F, C, G, Dm, E = 45, 41, 48, 43, 50, 40
    prog = [Am, F, C, G, Am, Dm, F, E] * 2
    q = {Am: [0, 3, 7], F: [0, 4, 7], C: [0, 4, 7], G: [0, 4, 7], Dm: [0, 3, 7], E: [0, 4, 7]}
    for i, root in enumerate(prog):
        for iv in q[root]:
            s.add(note_tone("pad", root + 12 + iv, 4 * s.beat), i * 4, 0.12)
        s.add(note_tone("bass", root, 3.5 * s.beat), i * 4, 0.45)
        for k, iv in enumerate([0, 7, 12, 7]):
            s.add(note_tone("pluck", root + 24 + (q[root][k % 3] if k != 2 else 12), 1 * s.beat), i * 4 + k, 0.08)
    bells = [(76, 2), (72, 1), (74, 1), (76, 2), (79, 2), (77, 3), (76, 1), (74, 4),
             (72, 2), (71, 1), (72, 1), (74, 2), (76, 2), (68, 4), (None, 4),
             (81, 2), (79, 1), (77, 1), (76, 2), (72, 2), (74, 3), (76, 1), (77, 4),
             (76, 2), (74, 2), (72, 2), (71, 2), (69, 4), (None, 4)]
    melody(s, "bell", bells, 0.3)
    for bar in range(16):
        s.add(drum("kick"), bar * 4, 0.25)
        if bar % 2 == 1:
            s.add(drum("shaker"), bar * 4 + 2, 0.5)
    return s.finish(0.38, 0.85)


def battle_theme():
    """Tense loop for when the kingdom is under raid: driving bass, minor stabs, toms."""
    s = Song(bpm=132, bars=8)
    Em, C, D, B = 40, 36, 38, 35
    prog = [Em, Em, C, D, Em, Em, C, B]
    for i, root in enumerate(prog):
        for k in range(8):
            s.add(note_tone("bass", root + (12 if k % 4 == 3 else 0), 0.5 * s.beat), i * 4 + k * 0.5, 0.5)
        for st in (0, 1.5, 3):
            for iv in ([0, 3, 7] if root != B and root != D and root != C else [0, 4, 7]):
                s.add(note_tone("lead", root + 24 + iv, 0.3 * s.beat), i * 4 + st, 0.09)
        s.add(drum("kick"), i * 4, 0.6)
        s.add(drum("kick"), i * 4 + 2, 0.6)
        s.add(drum("snare"), i * 4 + 1, 0.4)
        s.add(drum("snare"), i * 4 + 3, 0.4)
        s.add(drum("snare"), i * 4 + 3.5, 0.2)
        for k in range(8):
            s.add(drum("hat"), i * 4 + k * 0.5, 0.6)
    line = [(76, 1.5), (74, 0.5), (71, 2), (72, 1), (74, 1), (76, 2), (79, 1.5), (78, 0.5), (76, 2), (74, 1), (71, 1), (72, 2),
            (76, 1.5), (74, 0.5), (71, 2), (72, 1), (74, 1), (76, 2), (79, 1), (81, 1), (83, 2), (78, 2), (75, 2)]
    melody(s, "lead", line, 0.24)
    return s.finish(0.18, 0.5)


def menu_theme():
    """The title screen: a stately, hopeful loop in F major."""
    s = Song(bpm=88, bars=8)
    F, C, Dm, Bb = 41, 48, 50, 46
    prog = [F, C, Dm, Bb, F, C, Bb, C]
    q = {F: [0, 4, 7], C: [0, 4, 7], Dm: [0, 3, 7], Bb: [0, 4, 7]}
    for i, root in enumerate(prog):
        for iv in q[root]:
            s.add(note_tone("pad", root + 12 + iv, 4 * s.beat), i * 4, 0.1)
        s.add(note_tone("bass", root, 2 * s.beat), i * 4, 0.45)
        s.add(note_tone("bass", root + 7, 2 * s.beat), i * 4 + 2, 0.35)
        for k in range(4):
            s.add(note_tone("pluck", root + 24 + q[root][k % 3] + (12 if k == 3 else 0), s.beat), i * 4 + k, 0.12)
        s.add(drum("kick"), i * 4, 0.35)
        s.add(drum("snare"), i * 4 + 2, 0.18)
    tune = [(72, 2), (77, 1), (79, 1), (81, 3), (79, 1), (77, 2), (76, 1), (74, 1), (72, 4),
            (74, 2), (77, 2), (79, 2), (81, 1), (82, 1), (81, 2), (79, 2), (77, 4)]
    melody(s, "flute", tune, 0.38)
    melody(s, "bell", [(None, 16), (84, 4), (81, 4), (82, 4), (79, 4)], 0.18)
    return s.finish(0.3, 0.75)


# ---------- ambience and variants ----------

def variants():
    """Two extra takes of the most repeated work sounds, so a busy kingdom doesn't sound like a loop."""
    for k in (1, 2):
        pitch = 1.0 + 0.08 * (k - 1.5) * 2
        body = sine(hz(50 + k * 3) * (1 + 1.5 * decay(int(SR * 0.08), 40)), 0.08) * decay(int(SR * 0.08), 22)
        chop = mix(lowpass(noise(0.07 + 0.01 * k), 1800 + 500 * k) * decay(int(SR * (0.07 + 0.01 * k)), 50 + 10 * k), body * 0.7)
        write(f"chop_{k}", reverb(chop, 0.15), 0.55)
        f = hz(86 + 2 * k)
        n = int(SR * 0.25)
        ting = mix(sine(f, 0.25) * decay(n, 16 + 4 * k), sine(f * 2.7, 0.25) * decay(n, 28) * 0.4)
        write(f"mine_{k}", reverb(mix(ting, lowpass(noise(0.03), 3500 + 600 * k) * 0.7), 0.2), 0.5)
        swish = lowpass(highpass(noise(0.15 + 0.03 * k), 1200 + 400 * k), 5000 + 1500 * k) * env(int(SR * (0.15 + 0.03 * k)), 0.03, 0.04, 0.5, 0.08)
        write(f"harvest_{k}", swish, 0.4)


def chirp(f0, f1, dur):
    n = int(SR * dur)
    f = np.linspace(f0, f1, n)
    return sine(f, dur) * env(n, 0.005, dur * 0.3, 0.6, dur * 0.4)


def ambient_day(seconds=24.0):
    """Wind in the grass and birds, looping."""
    n = int(SR * seconds)
    wind = lowpass(noise(seconds), 500) * 0.25
    wind *= 0.6 + 0.4 * np.sin(np.linspace(0, 2 * np.pi * 3, n)) ** 2
    out = wind.copy()
    t = 0.3
    while t < seconds - 1.0:
        kind = rng.integers(3)
        if kind == 0:   # quick warble
            song = mix(*[at(chirp(hz(88 + rng.integers(-3, 4)), hz(93 + rng.integers(-3, 4)), 0.06), j * 0.08) for j in range(rng.integers(3, 7))])
        elif kind == 1:  # two-note call
            song = mix(chirp(hz(91), hz(86), 0.18), at(chirp(hz(86), hz(84), 0.22), 0.25))
        else:            # trill
            song = mix(*[at(chirp(hz(96), hz(98), 0.03), j * 0.04) for j in range(12)])
        out = mix(out, at(song * rng.uniform(0.15, 0.35), t))[:n]
        t += rng.uniform(0.8, 2.6)
    out = out[:n]
    fade = int(SR * 0.5)
    out[:fade] *= np.linspace(0, 1, fade)
    out[-fade:] *= np.linspace(1, 0, fade)
    return reverb(out, 0.3, 0.7)[:n]


def ambient_night(seconds=20.0):
    """Crickets and a distant owl."""
    n = int(SR * seconds)
    out = lowpass(noise(seconds), 300) * 0.12
    for c in range(3):
        f = hz(100 + c * 2)
        rate = 18 + c * 4
        t = t_of(seconds)
        pulse = (np.sin(2 * np.pi * rate * t) > 0.3).astype(float) * (np.sin(2 * np.pi * (0.4 + 0.1 * c) * t + c) > -0.2)
        out += sine(f, seconds) * pulse * 0.05
    for t0 in (4.0, 13.5):
        hoot = mix(chirp(hz(62), hz(60), 0.35), at(chirp(hz(62), hz(59), 0.5), 0.45))
        out = mix(out, at(lowpass(hoot, 900) * 0.25, t0))[:n]
    out = out[:n]
    fade = int(SR * 0.5)
    out[:fade] *= np.linspace(0, 1, fade)
    out[-fade:] *= np.linspace(1, 0, fade)
    return reverb(out, 0.35, 0.8)[:n]


def rain(seconds=12.0):
    n = int(SR * seconds)
    hiss = lowpass(highpass(noise(seconds), 800), 6000) * 0.35
    drops = np.zeros(n)
    for _ in range(int(seconds * 40)):
        p = int(rng.uniform(0, n - 400))
        d = int(SR * 0.012)
        drops[p:p + d] += highpass(noise(0.012), 2500) * decay(d, 300) * rng.uniform(0.2, 0.7)
    out = hiss + drops
    fade = int(SR * 0.3)
    out[:fade] *= np.linspace(0.6, 1, fade)
    out[-fade:] *= np.linspace(1, 0.6, fade)
    return out


def thunder():
    d = 3.5
    n = int(SR * d)
    crack = highpass(noise(0.15), 1500) * decay(int(SR * 0.15), 25)
    rumble = lowpass(noise(d), np.linspace(900, 120, n)) * env(n, 0.05, 0.3, 0.8, 2.0)
    return reverb(mix(crack * 0.8, rumble * 1.2), 0.4, 0.9)


if __name__ == "__main__":
    print("sound effects:")
    sfx()
    variants()
    write("ambient_day", ambient_day(), 0.5, trim=False)
    write("ambient_night", ambient_night(), 0.5, trim=False)
    write("rain", rain(), 0.5, trim=False)
    write("thunder", thunder(), 0.8)
    print("music:")
    write("music_day", day_theme(), 0.7, trim=False)
    write("music_night", night_theme(), 0.7, trim=False)
    write("music_battle", battle_theme(), 0.7, trim=False)
    write("music_menu", menu_theme(), 0.7, trim=False)
