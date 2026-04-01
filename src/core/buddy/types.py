"""Buddy type definitions and constants.

Port of claude-code-main/src/buddy/types.ts
"""
from __future__ import annotations

from dataclasses import dataclass, field

# ---------------------------------------------------------------------------
# Rarities
# ---------------------------------------------------------------------------

RARITIES = ('common', 'uncommon', 'rare', 'epic', 'legendary')

RARITY_WEIGHTS: dict[str, int] = {
    'common': 60,
    'uncommon': 25,
    'rare': 10,
    'epic': 4,
    'legendary': 1,
}

RARITY_STARS: dict[str, str] = {
    'common': '\u2605',
    'uncommon': '\u2605\u2605',
    'rare': '\u2605\u2605\u2605',
    'epic': '\u2605\u2605\u2605\u2605',
    'legendary': '\u2605\u2605\u2605\u2605\u2605',
}

# Mapped to rich style names (original uses theme keys)
RARITY_COLORS: dict[str, str] = {
    'common': 'dim',
    'uncommon': 'green',
    'rare': 'blue',
    'epic': 'magenta',
    'legendary': 'yellow',
}

RARITY_FLOOR: dict[str, int] = {
    'common': 5,
    'uncommon': 15,
    'rare': 25,
    'epic': 35,
    'legendary': 50,
}

# ---------------------------------------------------------------------------
# Species
# ---------------------------------------------------------------------------

SPECIES = (
    'duck', 'goose', 'blob', 'cat', 'dragon', 'octopus', 'owl', 'penguin',
    'turtle', 'snail', 'ghost', 'axolotl', 'capybara', 'cactus', 'robot',
    'rabbit', 'mushroom', 'chonk',
)

# Bonus species — only available via CC_MINI_BUDDY_SEED, not in random pool
BONUS_SPECIES = ('pikachu',)
ALL_SPECIES = SPECIES + BONUS_SPECIES

# ---------------------------------------------------------------------------
# Appearance
# ---------------------------------------------------------------------------

EYES = ('\u00b7', '\u2726', '\u00d7', '\u25c9', '@', '\u00b0')
# ·  ✦  ×  ◉  @  °

HATS = ('none', 'crown', 'tophat', 'propeller', 'halo', 'wizard', 'beanie', 'tinyduck')

# ---------------------------------------------------------------------------
# Stats
# ---------------------------------------------------------------------------

STAT_NAMES = ('DEBUGGING', 'PATIENCE', 'CHAOS', 'WISDOM', 'SNARK')

# ---------------------------------------------------------------------------
# Dataclasses
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class CompanionBones:
    """Deterministic parts — derived from hash(userId)."""
    rarity: str
    species: str
    eye: str
    hat: str
    shiny: bool
    stats: dict[str, int] = field(default_factory=dict)


@dataclass(frozen=True)
class CompanionSoul:
    """Model-generated soul — stored in config after first hatch."""
    name: str
    personality: str


@dataclass(frozen=True)
class StoredCompanion:
    """What actually persists on disk."""
    name: str
    personality: str
    hatched_at: int  # ms since epoch


@dataclass(frozen=True)
class StoredCompanionWithSeed(StoredCompanion):
    """Stored companion that also remembers the seed used to generate bones."""
    seed: str = ''


@dataclass(frozen=True)
class Companion:
    """Full companion = bones + soul + metadata."""
    # Bones
    rarity: str
    species: str
    eye: str
    hat: str
    shiny: bool
    stats: dict[str, int]
    # Soul
    name: str
    personality: str
    # Metadata
    hatched_at: int
