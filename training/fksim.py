"""ctypes bridge to the C# simulation (training/native/fksim.so)."""
import ctypes
from pathlib import Path

import numpy as np

KINDS = ["base", "farmer", "builder", "guard", "scout", "thief", "hunter", "healer", "hoarder", "all"]

_lib = ctypes.CDLL(str(Path(__file__).parent / "native" / "fksim.so"))
_f = ctypes.POINTER(ctypes.c_float)
_i = ctypes.POINTER(ctypes.c_int)
_b = ctypes.POINTER(ctypes.c_uint8)

for name in ["fk_obs_size", "fk_num_moves", "fk_num_actions", "fk_weight_count"]:
    getattr(_lib, name).restype = ctypes.c_int
_lib.fk_hidden.argtypes = [ctypes.c_int]
_lib.fk_create.argtypes = [ctypes.c_int, ctypes.c_int, ctypes.c_int]
_lib.fk_create.restype = ctypes.c_int
_lib.fk_destroy.argtypes = [ctypes.c_int]
_lib.fk_reset.argtypes = [ctypes.c_int, _f]
_lib.fk_step.argtypes = [ctypes.c_int, _i, _i, _f, _f, _b]
_lib.fk_ctx.argtypes = [ctypes.c_int, _b]
_lib.fk_set_context.argtypes = [ctypes.c_int, ctypes.c_float]
_lib.fk_stats.argtypes = [ctypes.c_int, _i]
_lib.fk_stats.restype = ctypes.c_double
_lib.fk_forward.argtypes = [_f, _f, _f]
_lib.fk_eval.argtypes = [_f, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int]
_lib.fk_eval.restype = ctypes.c_double

_lib.fk_load_traits.argtypes = [ctypes.c_char_p]
_lib.fk_trial.argtypes = [_f, ctypes.c_int, _f, ctypes.c_char_p, ctypes.c_int]
_lib.fk_trial.restype = ctypes.c_int
_lib.fk_load_traits(str(Path(__file__).resolve().parent.parent / "data" / "traits.json").encode())

AXES = ["Courage", "Aggression", "Greed", "Loyalty", "Discipline", "Curiosity",
        "Patience", "Teamwork", "Cunning", "Diligence", "Territoriality", "Compassion"]

OBS = _lib.fk_obs_size()
MOVES = _lib.fk_num_moves()
ACTIONS = _lib.fk_num_actions()
H1 = _lib.fk_hidden(1)
H2 = _lib.fk_hidden(2)
WEIGHTS = _lib.fk_weight_count()


def _p(a, t):
    return a.ctypes.data_as(t)


class VecEnv:
    def __init__(self, n, kind, seed=0):
        self.n = n
        self.h = _lib.fk_create(n, KINDS.index(kind), seed)
        self.obs = np.zeros((n, OBS), np.float32)
        self.rew = np.zeros(n, np.float32)
        self.done = np.zeros(n, np.uint8)

    def reset(self):
        _lib.fk_reset(self.h, _p(self.obs, _f))
        return self.obs.copy()

    def step(self, moves, actions):
        m = np.ascontiguousarray(moves, np.int32)
        a = np.ascontiguousarray(actions, np.int32)
        _lib.fk_step(self.h, _p(m, _i), _p(a, _i), _p(self.obs, _f), _p(self.rew, _f), _p(self.done, _b))
        return self.obs.copy(), self.rew.copy(), self.done.astype(bool)

    def set_context_chance(self, chance):
        """Share of episodes that contain the specialist trigger (applies from the next reset)."""
        _lib.fk_set_context(self.h, chance)

    def ctx(self):
        """Per env: is the specialist trigger present in the episode the latest observation belongs to."""
        out = np.zeros(self.n, np.uint8)
        _lib.fk_ctx(self.h, _p(out, _b))
        return out.astype(bool)

    def stats(self):
        c = ctypes.c_int(0)
        mean = _lib.fk_stats(self.h, ctypes.byref(c))
        return mean, c.value

    def close(self):
        _lib.fk_destroy(self.h)


def forward(weights, obs):
    w = np.ascontiguousarray(weights, np.float32)
    o = np.ascontiguousarray(obs, np.float32)
    out = np.zeros(MOVES + ACTIONS, np.float32)
    _lib.fk_forward(_p(w, _f), _p(o, _f), _p(out, _f))
    return out


def trial(weights, seed=12345):
    """Returns (scores dict, trait list, erratic flag) using the same Trial code as the game."""
    w = np.ascontiguousarray(weights, np.float32)
    scores = np.zeros(len(AXES), np.float32)
    buf = ctypes.create_string_buffer(512)
    erratic = _lib.fk_trial(_p(w, _f), seed, _p(scores, _f), buf, 512)
    traits = [t for t in buf.value.decode().split(",") if t]
    return dict(zip(AXES, scores.round(1).tolist())), traits, bool(erratic)


_lib.fk_es_new.argtypes = [_f, ctypes.c_int, ctypes.c_int]
_lib.fk_es_new.restype = ctypes.c_int
_lib.fk_es_step.argtypes = [ctypes.c_int]
_lib.fk_es_step.restype = ctypes.c_float
_lib.fk_es_weights.argtypes = [ctypes.c_int, _f]
_lib.fk_es_free.argtypes = [ctypes.c_int]
_lib.fk_es_config.argtypes = [ctypes.c_int, ctypes.c_float, ctypes.c_float, ctypes.c_int]


class Evolution:
    """The in-game Training Grounds trainer (C#), driven from Python for testing."""

    def __init__(self, weights, drill, seed=0):
        w = np.ascontiguousarray(weights, np.float32)
        self.h = _lib.fk_es_new(_p(w, _f), KINDS.index(drill), seed)

    def config(self, sigma, lr, pop):
        _lib.fk_es_config(self.h, sigma, lr, pop)

    def step(self):
        return _lib.fk_es_step(self.h)

    def weights(self):
        out = np.zeros(WEIGHTS, np.float32)
        _lib.fk_es_weights(self.h, _p(out, _f))
        return out

    def close(self):
        _lib.fk_es_free(self.h)


def evaluate(weights, kind, episodes=64, seed=1, greedy=False, context=-1, special_only=False):
    """context: -1 random as in training, 0 trigger absent, 1 trigger present."""
    w = np.ascontiguousarray(weights, np.float32)
    return _lib.fk_eval(_p(w, _f), KINDS.index(kind), episodes, seed, int(greedy), context, int(special_only))
