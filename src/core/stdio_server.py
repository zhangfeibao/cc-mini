"""Stdio JSON protocol server for IDE / WPF integration.

Launches cc-mini in a long-running process that communicates via
stdin/stdout using NDJSON (one JSON object per line).

Usage::

    cc-mini --stdio [--auto-approve]

Protocol
--------
**Requests** (client → stdin, one JSON per line)::

    {"id": "1", "method": "submit", "params": {"prompt": "hello"}}
    {"id": "2", "method": "abort"}
    {"id": "3", "method": "permission_response", "params": {"allow": true}}

**Events** (stdout → client, one JSON per line)::

    {"id": "1", "event": "text", "data": {"chunk": "Hi"}}
    {"id": "1", "event": "tool_call", "data": {"name": "Read", "input": {...}}}
    {"id": "1", "event": "tool_result", "data": {"name": "Read", "content": "...", "is_error": false}}
    {"id": "1", "event": "usage", "data": {"input_tokens": 100, "output_tokens": 50}}
    {"id": "1", "event": "done", "data": {}}
    {"id": "1", "event": "error", "data": {"message": "..."}}
    {"event": "permission_request", "data": {"tool": "Bash", "input": {"command": "ls"}}}
"""

from __future__ import annotations

import json
import queue
import sys
import threading
from typing import Any, TYPE_CHECKING

from .permissions import PermissionChecker, PermissionBehavior
from .tools.base import Tool

if TYPE_CHECKING:
    from .config import AppConfig


class StdioPermissionChecker(PermissionChecker):
    """Permission checker that communicates via stdin/stdout JSON protocol."""

    def __init__(
        self,
        auto_approve: bool = False,
        emit_fn: Any = None,
        request_queue: queue.Queue | None = None,
    ):
        super().__init__(auto_approve=auto_approve)
        self._emit = emit_fn
        self._request_queue: queue.Queue = request_queue or queue.Queue()

    def _prompt_user(self, tool: Tool, inputs: dict) -> PermissionBehavior:
        # Send permission request to client
        self._emit(None, "permission_request", {
            "tool": tool.name,
            "input": _safe_inputs(inputs),
        })

        # Block until client responds
        while True:
            try:
                msg = self._request_queue.get(timeout=300)
            except queue.Empty:
                return "deny"

            if msg.get("method") == "permission_response":
                params = msg.get("params", {})
                allow = params.get("allow", False)
                if allow:
                    always = params.get("always", False)
                    if always:
                        self._always_allow.add(tool.name)
                    return "allow"
                return "deny"
            # Not a permission response — put it back (shouldn't normally happen)
            self._request_queue.put(msg)


def _safe_inputs(inputs: dict) -> dict:
    """Truncate long values for JSON serialization."""
    result = {}
    for k, v in inputs.items():
        s = str(v)
        result[k] = s[:2000] + ("..." if len(s) > 2000 else "")
    return result


def _emit_event(stream_lock: threading.Lock, request_id: str | None, event: str, data: dict) -> None:
    """Write one NDJSON line to stdout."""
    obj: dict[str, Any] = {"event": event, "data": data}
    if request_id is not None:
        obj["id"] = request_id
    line = json.dumps(obj, ensure_ascii=False) + "\n"
    with stream_lock:
        sys.stdout.buffer.write(line.encode("utf-8"))
        sys.stdout.buffer.flush()


def _stdin_reader(
    inbox: queue.Queue,
    permission_queue: queue.Queue,
    shutdown: threading.Event,
) -> None:
    """Background thread: read JSON lines from stdin, dispatch to queues."""
    for raw_line in sys.stdin:
        if shutdown.is_set():
            break
        raw_line = raw_line.strip()
        if not raw_line:
            continue
        try:
            msg = json.loads(raw_line)
        except json.JSONDecodeError:
            continue

        method = msg.get("method", "")
        if method == "permission_response":
            permission_queue.put(msg)
        else:
            inbox.put(msg)

    # Signal EOF
    inbox.put(None)


def run_stdio(app_config: AppConfig) -> None:
    """Main entry point for --stdio mode."""
    from pathlib import Path
    from .context import build_system_prompt
    from .engine import Engine
    from .cost_tracker import CostTracker
    from .session import SessionStore
    from .tools.file_read import FileReadTool
    from .tools.glob_tool import GlobTool
    from .tools.grep_tool import GrepTool
    from .tools.file_edit import FileEditTool
    from .tools.file_write import FileWriteTool
    from .tools.bash import BashTool

    cwd = str(Path.cwd())
    stream_lock = threading.Lock()
    inbox: queue.Queue = queue.Queue()
    permission_queue: queue.Queue = queue.Queue()
    shutdown = threading.Event()

    def emit(request_id: str | None, event: str, data: dict) -> None:
        _emit_event(stream_lock, request_id, event, data)

    permissions = StdioPermissionChecker(
        auto_approve=app_config.auto_approve if hasattr(app_config, "auto_approve") else False,
        emit_fn=emit,
        request_queue=permission_queue,
    )

    tools = [
        FileReadTool(), GlobTool(), GrepTool(),
        FileEditTool(), FileWriteTool(),
        BashTool(),
    ]

    memory_dir = app_config.memory_dir
    system_prompt = build_system_prompt(cwd=cwd, memory_dir=memory_dir)
    cost_tracker = CostTracker()
    session_store = SessionStore(cwd=cwd, model=app_config.model)

    engine = Engine(
        tools=tools,
        system_prompt=system_prompt,
        permission_checker=permissions,
        provider=app_config.provider,
        api_key=app_config.api_key,
        base_url=app_config.base_url,
        model=app_config.model,
        max_tokens=app_config.max_tokens,
        effort=app_config.effort,
        session_store=session_store,
        cost_tracker=cost_tracker,
    )

    # Start stdin reader thread
    reader_thread = threading.Thread(
        target=_stdin_reader,
        args=(inbox, permission_queue, shutdown),
        daemon=True,
    )
    reader_thread.start()

    # Emit ready event
    emit(None, "ready", {
        "provider": app_config.provider,
        "model": app_config.model,
    })

    # Main loop: process requests from inbox
    while True:
        msg = inbox.get()
        if msg is None:
            break  # EOF

        method = msg.get("method", "")
        request_id = msg.get("id")
        params = msg.get("params", {})

        if method == "submit":
            prompt = params.get("prompt", "")
            if not prompt:
                emit(request_id, "error", {"message": "Empty prompt"})
                continue
            _handle_submit(engine, prompt, request_id, emit)

        elif method == "abort":
            engine.abort()
            emit(request_id, "aborted", {})

        elif method == "get_messages":
            messages = engine.get_messages()
            emit(request_id, "messages", {"messages": messages})

        elif method == "get_cost":
            emit(request_id, "cost", {
                "total_cost_usd": cost_tracker.total_cost_usd,
                "summary": cost_tracker.format_cost(),
            })

        else:
            emit(request_id, "error", {"message": f"Unknown method: {method}"})

    shutdown.set()


def _handle_submit(engine: Any, prompt: str, request_id: str | None, emit: Any) -> None:
    """Run engine.submit() and emit events."""
    try:
        for event in engine.submit(prompt):
            event_type = event[0]

            if event_type == "text":
                emit(request_id, "text", {"chunk": event[1]})

            elif event_type == "tool_call":
                emit(request_id, "tool_call", {
                    "name": event[1],
                    "input": _safe_inputs(event[2]) if isinstance(event[2], dict) else {},
                    "activity": event[3] if len(event) > 3 else None,
                })

            elif event_type == "tool_executing":
                emit(request_id, "tool_executing", {
                    "name": event[1],
                    "input": _safe_inputs(event[2]) if isinstance(event[2], dict) else {},
                    "activity": event[3] if len(event) > 3 else None,
                })

            elif event_type == "tool_result":
                result = event[3] if len(event) > 3 else event[2]
                content = getattr(result, "content", str(result))
                is_error = getattr(result, "is_error", False)
                emit(request_id, "tool_result", {
                    "name": event[1],
                    "content": content[:5000] + ("..." if len(content) > 5000 else ""),
                    "is_error": is_error,
                })

            elif event_type == "usage":
                usage = event[1]
                emit(request_id, "usage", {
                    "input_tokens": getattr(usage, "input_tokens", 0),
                    "output_tokens": getattr(usage, "output_tokens", 0),
                    "cache_read_input_tokens": getattr(usage, "cache_read_input_tokens", 0),
                    "cache_creation_input_tokens": getattr(usage, "cache_creation_input_tokens", 0),
                })

            elif event_type == "waiting":
                emit(request_id, "waiting", {})

            elif event_type == "error":
                emit(request_id, "error", {"message": event[1] if len(event) > 1 else "Unknown error"})

        emit(request_id, "done", {})

    except Exception as exc:
        emit(request_id, "error", {"message": str(exc)})
        emit(request_id, "done", {})
