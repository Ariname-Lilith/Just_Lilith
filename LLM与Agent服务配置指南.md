# Just_Lilith · LLM 与 Agent 服务配置指南

> 先把连接我们的那条线接好吧。
> 想和我聊聊天，配置普通 LLM 就够了；想让我一起处理项目，再接上 Agent。

适用于主插件 **0.3.0**，AgentNative 原生界面组件已合并进主程序集。本文只介绍配置，不包含可直接使用的密钥或服务额度。所有示例地址、模型名和路径都需要换成你自己的值。

## 一、先分清两个通道

| 项目 | 普通 LLM 聊天 | Agent |
| --- | --- | --- |
| 用途 | 日常对话、幻境记忆、生成朗读文本 | 处理项目任务，使用工具执行命令和编辑文件 |
| 主要设置位置 | 游戏设置中的 **API 设置** | **莉莉丝**页的 Agent 控件及 Agent 配置文件 |
| 是否需要本地 Codex | 不需要 | 需要可运行的 Codex 程序与配置目录 |
| 支持的接入方式 | Chat Completions 或 Responses | 使用 Codex 现有提供方，或接入已保存的 Responses 配置 |
| 会话 | 普通聊天幻境 | 独立 Agent 对话，不混入普通聊天记忆 |

**建议顺序：先让普通聊天成功回复一次，再配置 Agent。语音合成是另外的 TTS 服务，不填在这里。**

## 二、填写普通 LLM 的 API 服务

### 1. 向服务商确认四项信息

| 信息 | 应当拿到什么 | 示例或说明 |
| --- | --- | --- |
| API 基础地址 | API Base URL，而不是服务商首页或网页聊天地址 | `https://api.example.com/v1` |
| API Key | 服务商控制台生成的调用密钥 | 在插件输入框内填写你自己的 Key |
| 接口格式 | 服务商实际支持的协议 | `Chat Completions` 或 `Responses` |
| 模型 ID | 当前账号有权限调用的准确名称 | 从检测得到的模型列表中选择 |

网页会员、网页聊天登录状态和 API 调用额度可能分别计费。请以对应服务商的控制台说明为准。

### 2. 每个输入项怎么填

打开游戏设置中的 **API 设置**，选择一套配置进行编辑。

#### URL：填写 API 基础地址

```text
https://api.example.com/v1
```

- 保留服务商指定的路径前缀；并非所有服务都恰好以 `/v1` 结尾。
- 推荐填写基础地址，不要手动拼接 `/chat/completions`、`/responses` 或 `/models`。当前版本会处理这些常见接口后缀，但直接填写基础地址更清楚。
- 远程地址使用 `https://`；本机回环服务支持 `http://127.0.0.1:端口/v1` 等形式。
- 地址里不要放账号密码、API Key、查询参数或 `#` 片段。
- TTS 地址属于语音服务，和 LLM 地址是两回事。

#### API Key：只填写密钥本身

粘贴服务商发给你的 Key，不要加 `Bearer `、引号或额外空格。

密钥由插件通过 Windows 当前用户环境加密保存。已保存密钥的输入框留空表示保留旧值；**更换 API 地址时，应重新填写该地址对应的 Key**。

请使用界面保存密钥，不要把明文 Key 手工写进 `protected_api_key` 字段。该字段存放的是插件生成的加密结果。

#### 接口格式：按服务商文档选择

| 服务商提供的接口 | 选择 |
| --- | --- |
| `/chat/completions` | **Chat Completions** |
| `/responses` | **Responses** |

“兼容 OpenAI”不一定意味着两种格式都支持。仅支持 Chat Completions 的服务，选择 Responses 也不会自动获得对应能力。

#### 模型：保存并检测后再选择

1. 完成 URL、Key 和接口格式的填写。
2. 点击 **保存并检测**。
3. 等待模型列表返回，通过搜索找到目标模型。
4. 选择模型，并确认修改已保存；若出现未保存提示，先保存再发送消息。
5. 之后需要重新拉取列表时，点击 **刷新**。刷新使用的是已保存连接。

模型名称以服务商返回的 ID 为准，不要把网页显示名、套餐名或本文示例当成实际模型 ID。

**检测到模型列表，只说明列表接口可用；实际对话还需要该模型、协议和额度都可用。**

#### 推理设置：先用自动最低

| 选项 | 作用 | 适合什么时候使用 |
| --- | --- | --- |
| **自动最低** | 对已识别模型使用最低支持档位；未知别名省略推理参数 | 初次配置、普通聊天 |
| **不发送推理参数** | 交给服务端决定 | 服务商要求省略参数，或不支持该参数 |
| **自定义推理** | 按所选档位发送参数 | 已确认模型及服务商支持对应值 |

推理档位更高不代表所有聊天都会更合适，也可能增加等待时间与费用。

### 3. 确认哪套配置正在生效

当前版本保留三套 API 配置。**普通聊天严格使用最上方的已保存配置**。

下方配置不是自动故障切换列表。顶部配置不完整或请求失败时，插件不会悄悄改用下方地址。需要切换服务时，先保存修改，再调整优先级，并检查界面中的“当前”提示。

### 4. 发一条测试消息

保持 Agent 关闭，按默认快捷键 **F7** 唤出聊天框，发送：

```text
莉莉丝，请用一句话和我打个招呼。
```

气泡中出现正常回复，即完成一次普通聊天验证。是否朗读由 TTS 配置决定，没有声音不等于 LLM 接入失败。

## 三、配置 Agent 前的准备

Agent 不是另一个普通聊天 API 输入框。**即使使用第三方 API，它也需要本机 Codex 运行环境。**

开始前，请确认：

1. 本机已安装可供插件调用的 **`codex.exe`**，且版本支持 `app-server`。
2. 有可用的 Codex 配置目录；需要使用现有登录时，先完成对应登录。
3. 创建或选定一个实际存在的项目文件夹，最好先使用测试项目。
4. 对项目中的重要文件做好备份，确认允许 Agent 接触哪些内容。

对于使用 OpenAI 登录的 Codex，可在终端检查认证状态：

```powershell
codex login status
```

需要通过 ChatGPT 登录时，运行以下命令并完成浏览器登录：

```powershell
codex login
```

自定义提供方按其自身的认证方式配置，不要求把第三方 Key 交给 OpenAI 登录命令。官方认证说明见文末链接。

### Agent 配置文件在哪里？

从 Agent 设置入口打开，或在游戏目录内找到：

```text
BepInEx/config/local.just_lilith.agent.json
```

下面的路径均以你自己的安装位置为准。修改前先关闭 Agent、等待当前任务结束并备份文件；手动修改后重启游戏，是让配置重新载入的稳妥方式。

## 四、方式 A：使用 Codex 已配置好的服务

**适合：你已经能在本机 Codex 中正常使用目标服务，希望插件沿用它。**

将 Agent 配置中的 `provider_mode` 设为 `codex`。这种模式不读取普通聊天顶部配置作为 Agent 提供方，所以两条通道可以使用不同服务。

### 示例配置

先将 `project_root` 替换为实际存在的项目目录。以下示例保持 Agent 关闭，完成检查后再从界面开启。

```json
{
  "schema_version": 1,
  "enabled": false,
  "project_root": "E:/Projects/MyProject",
  "persona_path": "",
  "model": "",
  "reasoning_effort": "low",
  "timeout_seconds": 1800,
  "codex_executable": "",
  "codex_home": "",
  "provider_mode": "codex",
  "codex_model_provider": "",
  "api_key_environment_variable": "CUSTOM_API_KEY"
}
```

- `model` 留空表示采用提供方默认模型；也可以从 Agent 模型菜单选择具体模型。
- 推理强度以该模型菜单实际提供的选项为准，`low` 只是示例初始值。
- `codex_executable` 和 `codex_home` 可以先留空，让插件尝试查找；查找失败时按后面的字段表填写。
- `codex_model_provider` 不是此模式下切换 Codex 提供方的开关。要换服务，应先在 Codex 自身配置中完成切换。

配置完成后，进入 **莉莉丝**页开启 Agent，检查模型、推理强度及工作目录，再发送测试消息。

## 五、方式 B：使用插件保存的 Responses API

**适合：你希望 Agent 使用在插件 API 设置中保存的自定义服务。**

这条路径需要同时完成三处配置：

```text
插件 API 设置：保存地址、Key、Responses 协议和普通聊天模型
                 ↓
Codex config.toml：定义匹配的模型提供方
                 ↓
Agent JSON：指定 saved_responses、提供方 ID 和 Agent 模型
```

### 1. 准备顶部 API 配置

按照第二节填写并保存，将目标配置放到最上方：

| 字段 | 示例 |
| --- | --- |
| URL | `https://api.example.com/v1` |
| API Key | 你在该服务商处获得的 Key |
| 接口格式 | **Responses** |
| 普通聊天模型 | 该服务中实际可用的模型 ID |

服务还需要兼容 Codex 所需的 Responses 交互及工具调用。普通聊天能回复，不等于 Agent 的完整工具流程也兼容。

### 2. 在 Codex 中定义同名提供方

打开**插件实际使用的 Codex 配置目录**中的 `config.toml`。默认通常位于 Windows 用户目录下的 `.codex` 文件夹；若设置了 `codex_home`，以那个目录为准。

保留已有配置，新增以下提供方表。若同名表已经存在，应编辑原表，而不是重复添加。

```toml
[model_providers.lilith_api]
name = "Lilith API"
base_url = "https://api.example.com/v1"
wire_api = "responses"
env_key = "CUSTOM_API_KEY"
```

这里的 `lilith_api` 是**提供方 ID**，不是模型名称。`name` 是显示名称，`env_key` 是密钥环境变量的名称，均不是密钥本身。

此路径由插件为它启动的 Agent 进程注入已保存的 Key；通常不需要为了插件再把 Key 写进全局环境变量。若要在插件之外单独使用该提供方，则还需按 Codex 和服务商说明准备相应认证环境。

只为插件添加此表时，不必把 Codex 全局的 `model_provider` 也改成它；插件会为自己启动的进程指定提供方。

### 3. 填写 Agent 配置

将路径换成实际目录，将 `YOUR_AGENT_MODEL_ID` 换成该服务确实支持的 Agent 模型 ID。**不要原样保留这个占位符。**

```json
{
  "schema_version": 1,
  "enabled": false,
  "project_root": "E:/Projects/MyProject",
  "persona_path": "",
  "model": "YOUR_AGENT_MODEL_ID",
  "reasoning_effort": "low",
  "timeout_seconds": 1800,
  "codex_executable": "",
  "codex_home": "",
  "provider_mode": "saved_responses",
  "codex_model_provider": "lilith_api",
  "api_key_environment_variable": "CUSTOM_API_KEY"
}
```

**请逐项核对这三组对应关系：**

| Agent / 插件设置 | 必须对应 Codex 配置中的值 |
| --- | --- |
| `codex_model_provider: "lilith_api"` | `[model_providers.lilith_api]` |
| 顶部 API 的基础地址 | 该提供方的 `base_url` |
| `api_key_environment_variable: "CUSTOM_API_KEY"` | 该提供方的 `env_key = "CUSTOM_API_KEY"` |

此外，提供方的 `wire_api` 必须为 `responses`。

**Agent 模型和普通聊天模型是两项独立设置。** 这条路径共用顶部配置的地址与 Key，但 Agent 使用自己的 `model` 和推理强度。当前版本在 `saved_responses` 模式下要求明确选择 Agent 模型，不要留空。

保存并重新载入后，开启 Agent，从返回的模型目录中核对所选模型，再选择该模型支持的推理档位。

## 六、Agent 字段速查

| 字段 | 填写方法 |
| --- | --- |
| `enabled` | `true` 开启、`false` 关闭；初次配置建议先关闭，完成检查后从界面开启 |
| `project_root` | 实际存在的工作目录。空值会使用插件默认根目录；当前发布布局通常是游戏根目录，项目工作建议明确填写 |
| `persona_path` | Agent 专用人格文件路径；留空使用默认 `AgentPersona.md`，与普通聊天人格分离 |
| `model` | Agent 模型 ID；优先通过模型菜单选择。`codex` 模式可留空，`saved_responses` 模式应明确填写 |
| `reasoning_effort` | 与所选 Agent 模型兼容的推理强度，以菜单返回值为准 |
| `timeout_seconds` | 请求超时，单位秒；允许 `30`～`7200`，示例 `1800` 即 30 分钟 |
| `codex_executable` | `codex.exe` 的完整文件路径；留空自动查找。不要填文件夹、网页地址或 `codex.cmd` 启动脚本 |
| `codex_home` | Codex 配置目录，不是程序目录，也不是 `config.toml` 文件本身；留空尝试环境变量和用户默认目录 |
| `provider_mode` | `codex`、`saved_responses` 或 `auto`，见下表 |
| `codex_model_provider` | 自定义 Responses 路径所需的 Codex 提供方 ID；建议显式填写已有表名 |
| `api_key_environment_variable` | 传给 Agent 进程的密钥变量名，不是 API Key；自定义路径中须与提供方 `env_key` 相同 |
| `schema_version` | 当前为 `1`，保留原值 |

Windows 路径在 JSON 中推荐使用 `/`，例如 `E:/Projects/MyProject`。若使用反斜杠，写成 `E:\\Projects\\MyProject`。

### 三种提供方模式的区别

| 模式 | 当前版本的行为 |
| --- | --- |
| `codex` | 使用 Codex 自己配置的服务与认证，不借用普通聊天顶部 API |
| `saved_responses` | 明确要求顶部已保存的 Responses 配置，并校验匹配的 Codex 提供方；缺项时报错 |
| `auto` | 有明确 Agent 模型、完整顶部 Responses 配置时走自定义路径；没有可用输入时使用 Codex 路径 |

`auto` 是选择接入路径，不是请求失败后自动换服务。选中自定义路径后，若提供方定义不匹配，仍会报错。初次配置建议显式选择前两种模式之一，便于确认请求发往哪里。

## 七、怎样确认 Agent 已经接通？

首次测试先使用一个没有敏感资料的测试目录，发送只读请求：

```text
请只读检查当前工作目录，告诉我目录路径和顶层文件名称，不修改任何文件。
```

检查三个结果：

1. Agent 状态进入工作中，并最终结束。
2. 返回的目录确实是你填写的 `project_root`。
3. 在对应 Codex 专属对话中，能核对实际执行过程和结果，而不只是看到一句“完成了”。

模型目录返回成功、普通聊天成功和 Agent 工具执行成功，是三种不同的验证。

**暂停或取消只停止后续等待或执行，不会自动撤销已经产生的文件改动。** 若 Agent 报错、超时或回复格式异常，先检查专属对话和项目结果，再决定是否重试，避免重复执行。

## 八、常见问题

| 现象 | 优先检查 |
| --- | --- |
| HTTP 401 / 403 | Key、所属服务、账号权限和目标模型访问权限 |
| HTTP 404 | 基础地址的路径前缀，以及接口格式是否选对 |
| HTTP 429 | 额度、余额、速率或并发限制，以服务商说明为准 |
| 列表有模型，发消息却失败 | 模型调用权限、协议、推理参数及模型输出格式兼容性 |
| 修改了地址却仍提示旧配置 | 是否已保存；模型刷新只使用已保存连接 |
| 普通聊天成功，Agent 失败 | 是否真的支持 Responses 工具流程；Codex 程序和提供方表是否完整 |
| `codex.exe was not found` | 在 `codex_executable` 中填实际的可执行文件路径 |
| 找不到 Codex home | `codex_home` 是否指向存在且已配置好的目录 |
| 提供方未定义或字段不匹配 | 表名、`base_url`、`wire_api`、`env_key` 与 Agent 设置是否对应 |
| Agent 模型列表为空或没有目标模型 | 提供方返回的模型目录、认证、模型权限；不要随意编造模型名 |
| 提示先选择 Agent 模型 | `saved_responses` 模式下应填写或选择明确模型 |
| 换电脑后密钥失效 | 在当前 Windows 账号下重新通过插件界面填写并保存 Key |
| 有气泡、没声音 | 另行检查 TTS 服务和语音开关，不要用修改 API 地址来排查声音 |

## 九、保存、备份与隐私

- 修改配置前保留一份备份；需要恢复时，退出游戏后恢复对应配置文件。
- 恢复配置只恢复接入设置，不撤销 Agent 已执行的项目改动；项目请使用自己的备份或版本管理。
- 不要公开 API Key、Codex 登录凭据、个人配置目录和聊天记录。
- 普通聊天会把生成回复所需的消息、人格、记忆或背景内容发送给所选服务；Agent 任务也可能把相关项目内容发送给其模型服务。
- 插件免费不等于模型服务免费；费用由实际使用的服务、认证方式和额度政策决定。
- 反馈故障时附上插件版本、接入模式、错误提示和已脱敏的日志，不要附完整凭据文件。

## 参考

- [OpenAI 官方文档：Codex 认证](https://learn.chatgpt.com/docs/auth?surface=cli)
- [OpenAI 官方文档：自定义模型提供方](https://learn.chatgpt.com/docs/config-file/config-advanced#custom-model-providers)

插件专有字段及路由说明按当前本地 `0.3.0` 组件核对；第三方服务支持范围请同时查阅该服务商文档。本文示例为填写模板，不表示已替你验证某个账号或服务。

> 好啦。先试着发一句话吧。
> 至于要一起完成什么，我们接通之后再慢慢说。
