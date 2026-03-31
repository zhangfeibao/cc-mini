from argparse import Namespace
from pathlib import Path

import pytest

from mini_claude.config import (
    DEFAULT_MODEL,
    default_max_tokens_for_model,
    load_app_config,
    resolve_model,
)


def _args(**overrides):
    values = {
        "prompt": None,
        "print": False,
        "auto_approve": False,
        "config": None,
        "api_key": None,
        "base_url": None,
        "model": None,
        "max_tokens": None,
    }
    values.update(overrides)
    return Namespace(**values)


def test_resolve_model_keeps_full_model_name():
    assert resolve_model("claude-sonnet-4-20250514") == "claude-sonnet-4-20250514"


def test_default_max_tokens_follow_model_family():
    assert default_max_tokens_for_model("claude-sonnet-4") == 64000
    assert default_max_tokens_for_model("claude-opus-4-1-20250805") == 32000
    assert default_max_tokens_for_model("claude-3-5-haiku-20241022") == 8192


def test_load_app_config_reads_anthropic_section(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    monkeypatch.delenv("ANTHROPIC_BASE_URL", raising=False)
    monkeypatch.delenv("MINI_CLAUDE_MODEL", raising=False)
    monkeypatch.delenv("MINI_CLAUDE_MAX_TOKENS", raising=False)

    config_path = tmp_path / "mini-claude.toml"
    config_path.write_text(
        '[anthropic]\n'
        'api_key = "config-key"\n'
        'base_url = "https://example.test"\n'
        'model = "claude-3.7-sonnet"\n',
        encoding="utf-8",
    )

    config = load_app_config(_args(config=str(config_path)))

    assert config.api_key == "config-key"
    assert config.base_url == "https://example.test"
    assert config.model == "claude-3-7-sonnet"
    assert config.max_tokens == 64000


def test_load_app_config_cli_overrides_env_and_file(tmp_path: Path, monkeypatch: pytest.MonkeyPatch):
    config_path = tmp_path / "mini-claude.toml"
    config_path.write_text(
        'api_key = "file-key"\n'
        'base_url = "https://file.test"\n'
        'model = "claude-3-5-haiku"\n'
        'max_tokens = 2048\n',
        encoding="utf-8",
    )
    monkeypatch.setenv("ANTHROPIC_API_KEY", "env-key")
    monkeypatch.setenv("ANTHROPIC_BASE_URL", "https://env.test")
    monkeypatch.setenv("MINI_CLAUDE_MODEL", "claude-opus-4")
    monkeypatch.setenv("MINI_CLAUDE_MAX_TOKENS", "1234")

    config = load_app_config(
        _args(
            config=str(config_path),
            api_key="cli-key",
            base_url="https://cli.test",
            model="claude-sonnet-4",
            max_tokens=999,
        )
    )

    assert config.api_key == "cli-key"
    assert config.base_url == "https://cli.test"
    assert config.model == "claude-sonnet-4"
    assert config.max_tokens == 999


def test_load_app_config_uses_defaults_when_nothing_is_set(monkeypatch: pytest.MonkeyPatch):
    monkeypatch.delenv("ANTHROPIC_API_KEY", raising=False)
    monkeypatch.delenv("ANTHROPIC_BASE_URL", raising=False)
    monkeypatch.delenv("MINI_CLAUDE_MODEL", raising=False)
    monkeypatch.delenv("MINI_CLAUDE_MAX_TOKENS", raising=False)

    config = load_app_config(_args())

    assert config.api_key is None
    assert config.base_url is None
    assert config.model == DEFAULT_MODEL
    assert config.max_tokens == default_max_tokens_for_model(DEFAULT_MODEL)


def test_load_app_config_rejects_invalid_max_tokens(tmp_path: Path):
    config_path = tmp_path / "mini-claude.toml"
    config_path.write_text('max_tokens = "abc"\n', encoding="utf-8")

    with pytest.raises(ValueError, match="Invalid max_tokens"):
        load_app_config(_args(config=str(config_path)))
