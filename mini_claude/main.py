from __future__ import annotations

import argparse
import sys
from pathlib import Path

from prompt_toolkit import PromptSession
from prompt_toolkit.history import FileHistory
from rich.console import Console

from .config import load_app_config
from .context import build_system_prompt
from .engine import Engine
from .permissions import PermissionChecker
from .tools.bash import BashTool
from .tools.file_edit import FileEditTool
from .tools.file_read import FileReadTool
from .tools.glob_tool import GlobTool
from .tools.grep_tool import GrepTool

console = Console()
_HISTORY_FILE = Path.home() / ".mini_claude_history"


def _tool_preview(tool_name: str, tool_input: dict) -> str:
    if tool_name == "Bash":
        cmd = tool_input.get("command", "")
        return cmd[:80] + ("…" if len(cmd) > 80 else "")
    if tool_name in ("Read", "Edit"):
        fp = tool_input.get("file_path", "")
        return fp[-60:] if len(fp) > 60 else fp
    if tool_name in ("Glob", "Grep"):
        return tool_input.get("pattern", "")
    return ""


def run_query(engine: Engine, user_input: str, print_mode: bool) -> None:
    for event in engine.submit(user_input):
        if event[0] == "text":
            if print_mode:
                print(event[1], end="", flush=True)
            else:
                console.print(event[1], end="", markup=False)

        elif event[0] == "tool_result":
            _, tool_name, tool_input, result = event
            status = "[red]✗[/red]" if result.is_error else "[green]✓[/green]"
            preview = _tool_preview(tool_name, tool_input)
            console.print(f"\n[dim]↳ {tool_name}({preview}) {status}[/dim]")
            if result.is_error:
                console.print(f"  [red]{result.content[:300]}[/red]")

    if not print_mode:
        console.print()


def main() -> None:
    parser = argparse.ArgumentParser(prog="mini-claude",
                                     description="Minimal Python Claude Code")
    parser.add_argument("prompt", nargs="?", help="Prompt to send (optional)")
    parser.add_argument("-p", "--print", action="store_true",
                        help="Non-interactive: print response and exit")
    parser.add_argument("--auto-approve", action="store_true",
                        help="Auto-approve all tool permissions (dangerous)")
    parser.add_argument("--config", help="Path to a TOML config file")
    parser.add_argument("--api-key", help="Anthropic API key")
    parser.add_argument("--base-url", help="Anthropic-compatible API base URL")
    parser.add_argument("--model", help="Model name, e.g. claude-sonnet-4")
    parser.add_argument("--max-tokens", type=int,
                        help="Maximum output tokens for each model response")
    args = parser.parse_args()

    try:
        app_config = load_app_config(args)
    except ValueError as exc:
        parser.error(str(exc))

    tools = [FileReadTool(), GlobTool(), GrepTool(), FileEditTool(), BashTool()]
    system_prompt = build_system_prompt()
    permissions = PermissionChecker(auto_approve=args.auto_approve)
    engine = Engine(
        tools=tools,
        system_prompt=system_prompt,
        permission_checker=permissions,
        api_key=app_config.api_key,
        base_url=app_config.base_url,
        model=app_config.model,
        max_tokens=app_config.max_tokens,
    )

    # Non-interactive / piped
    if args.print or args.prompt:
        prompt_text = args.prompt or sys.stdin.read()
        run_query(engine, prompt_text, print_mode=args.print)
        return

    # Interactive REPL
    config_note = f"[dim]{app_config.model} · max_tokens={app_config.max_tokens}[/dim]"
    console.print("[bold cyan]Mini Claude Code[/bold cyan]  "
                  f"{config_note}  "
                  "[dim]type 'exit' or Ctrl+C to quit[/dim]\n")
    session: PromptSession = PromptSession(history=FileHistory(str(_HISTORY_FILE)))

    while True:
        try:
            user_input = session.prompt("\n> ").strip()
        except (KeyboardInterrupt, EOFError):
            console.print("\n[dim]Goodbye.[/dim]")
            break

        if not user_input:
            continue
        if user_input.lower() in ("exit", "quit", "/exit", "/quit"):
            console.print("[dim]Goodbye.[/dim]")
            break

        run_query(engine, user_input, print_mode=False)


if __name__ == "__main__":
    main()
