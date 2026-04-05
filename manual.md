cc-mini 目前内置支持两种 provider：anthropic 和 openai。
                                                                                                                                                                                                                                      
  当前支持情况
                                                                                                                                                                                                                                        ┌───────────┬────────────────────────────┬──────────────────┐                                                                                                                                                                         │ Provider  │         支持的模型         │       说明       │
  ├───────────┼────────────────────────────┼──────────────────┤                                                                                                                                                                       
  │ anthropic │ Claude 系列                │ 默认 provider    │
  ├───────────┼────────────────────────────┼──────────────────┤
  │ openai    │ GPT-5、GPT-4o、o1/o3/o4 等 │ 需安装 openai 包 │
  └───────────┴────────────────────────────┴──────────────────┘

  如何使用 DeepSeek、GLM 等模型

  DeepSeek、GLM（智谱）等国产模型都提供 OpenAI 兼容 API，所以可以通过 openai provider + 自定义 base_url 来使用。

  方式一：环境变量

  set CC_MINI_PROVIDER=openai
  set CC_MINI_MODEL=deepseek-chat
  set OPENAI_API_KEY=你的deepseek-api-key
  set OPENAI_BASE_URL=https://api.deepseek.com/v1

  方式二：TOML 配置文件

  在项目目录创建 .cc-mini.toml 或全局 ~/.config/cc-mini/config.toml：

  provider = "openai"

  [openai]
  api_key = "你的api-key"
  base_url = "https://api.deepseek.com/v1"
  model = "deepseek-chat"

  智谱 GLM 类似：

  provider = "openai"

  [openai]
  api_key = "你的api-key"
  base_url = "https://open.bigmodel.cn/api/paas/v4"
  model = "glm-4-plus"

  方式三：CLI 参数

  cc-mini --provider openai --model deepseek-chat --base-url https://api.deepseek.com/v1 --api-key 你的key

  限制

  当前代码中 validate_provider() 只接受 "anthropic" 和 "openai" 两个值（llm.py:26）。所以无法直接添加一个叫 "deepseek" 的 provider——必须统一走 openai provider 并修改 base_url。这对于所有 OpenAI 兼容 API 的服务都适用。

  