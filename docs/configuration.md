# Configuration

## API Keys

### Anthropic (default)

```bash
export ANTHROPIC_API_KEY=sk-ant-...
export ANTHROPIC_BASE_URL=https://your-gateway.example.com  # optional
```

### OpenAI-compatible

```bash
export CC_MINI_PROVIDER=openai
export OPENAI_API_KEY=sk-...
export OPENAI_BASE_URL=https://your-openai-gateway.example.com
```

### Environment Variables

| Variable | Description |
|----------|-------------|
| `CC_MINI_MODEL` | Model name (e.g. `claude-sonnet-4-5`) |
| `CC_MINI_MAX_TOKENS` | Max output tokens |
| `CC_MINI_EFFORT` | Reasoning effort (`low`, `medium`, `high`) |
| `CC_MINI_PROVIDER` | `anthropic` or `openai` |
| `CC_MINI_BUDDY_MODEL` | Model for companion pet reactions |
| `CC_MINI_BUDDY_SEED` | Override buddy seed for specific companion |
| `CC_MINI_PROFILE` | 使用指定的 profile 配置 |
| `CC_MINI_EXTRA_HEADERS` | 自定义 HTTP 请求头，格式: `Key1:Value1,Key2:Value2` |

## CLI Flags

```bash
cc-mini \
  --provider anthropic \
  --base-url https://your-gateway.example.com \
  --api-key sk-ant-... \
  --model claude-sonnet-4 \
  --max-tokens 64000 \
  --profile midea-gpt5 \
  --auto-approve \
  --coordinator \
  --resume 1
```

## TOML Config Files

Loaded in order (later overrides earlier):

1. `~/.config/cc-mini/config.toml`
2. `.cc-mini.toml` in the current working directory

Point to a specific file with `--config`.

### Anthropic example

```toml
provider = "anthropic"

[anthropic]
api_key = "sk-ant-..."
base_url = "https://your-gateway.example.com"
model = "claude-sonnet-4"
```

### OpenAI example

```toml
provider = "openai"

[openai]
api_key = "sk-..."
base_url = "https://your-openai-gateway.example.com/v1"
model = "gpt-4.1-mini"
max_tokens = 8192
effort = "medium"
buddy_model = "gpt-4.1-mini"
```

### OpenRouter (low-cost testing)

```toml
provider = "openai"

[openai]
api_key = "sk-or-..."
base_url = "https://openrouter.ai/api/v1"
model = "qwen/qwen3.6-plus-preview:free"
```

When `provider = "openai"`, `OPENAI_API_KEY` / `OPENAI_BASE_URL` are used. When `provider = "anthropic"`, `ANTHROPIC_API_KEY` / `ANTHROPIC_BASE_URL` are used.

## 企业网关 — 自定义请求头

对于需要自定义 HTTP 请求头的内部 API 网关，可在 provider 节下添加 `extra_headers` 子表：

```toml
provider = "openai"

[openai]
api_key = "unused"
base_url = "https://aimpapi.midea.com/t-aigc/mip-chat-app/openai/standard/v1"
model = "gpt-5"
effort = "medium"

[openai.extra_headers]
Aimp-Biz-Id = "gpt-5"
Authorization = "msk-your-token-here"
AIGC-USER = "your-user-id"
```

也可通过环境变量设置：

```bash
export CC_MINI_EXTRA_HEADERS="Aimp-Biz-Id:gpt-5,Authorization:msk-your-token,AIGC-USER:your-user-id"
```

> **注意**：当 `extra_headers` 包含 `Authorization` 键时，会覆盖 SDK 默认的 `Bearer {api_key}` 头。
> 如果网关不使用标准 Bearer 认证，无需单独配置 `api_key`，系统会自动填充占位值。

## 多 Profile 切换

支持在 TOML 配置文件中定义多组完整的模型配置（profiles），通过名称快速切换：

```toml
# 启动时默认使用的 profile
profile = "midea-gpt5"

[profiles.midea-gpt5]
provider = "openai"
base_url = "https://aimpapi.midea.com/t-aigc/mip-chat-app/openai/standard/v1"
model = "gpt-5"
effort = "medium"
max_tokens = 8192

[profiles.midea-gpt5.extra_headers]
Aimp-Biz-Id = "gpt-5"
Authorization = "msk-480ffe4f47a434e3b657c3ba7b009c908223c484b79e8905fd1d89b282b29281"
AIGC-USER = "zhangfb1"

[profiles.midea-other]
provider = "openai"
base_url = "https://other-gateway.example.com/v1"
model = "other-model"

[profiles.midea-other.extra_headers]
Authorization = "msk-another-token"

[profiles.anthropic-sonnet]
provider = "anthropic"
api_key = "sk-ant-..."
model = "claude-sonnet-4"
```

### 使用方式

```bash
# CLI 启动时指定 profile
cc-mini --profile midea-gpt5

# 环境变量
export CC_MINI_PROFILE=midea-gpt5

# 运行时切换
/profile                    # 查看所有可用 profile
/profile midea-other        # 切换到指定 profile
```

### 配置优先级

```
CLI 参数 (--model, --provider, ...)
  ↓
环境变量 (CC_MINI_MODEL, CC_MINI_PROFILE, ...)
  ↓
TOML profile 节 (profiles.xxx)
  ↓
TOML provider 节 ([openai] / [anthropic])
  ↓
默认值
```
