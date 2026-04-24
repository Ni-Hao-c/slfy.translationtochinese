# translationToChinese

`translationToChinese` 是一个用于 s&box 编辑器汉化的库项目。  
它通过 Harmony 在编辑器进程内拦截常见的 UI 文本显示路径，再从 JSON 语言包中读取翻译结果。

这个库目前主要面向：

- s&box 编辑器界面汉化
- Inspector / 菜单 / 设置页 / 部分工具提示汉化
- API 文档标题、描述、摘要的本地化

## 项目结构

```text
translationToChinese/
  Assets/
    3rd/
      harmony/
        0Harmony.dll
  Code/
    translationtochinese.csproj
  Editor/
    SboxChinesePatch.cs
    SboxChineseDictionary.cs
    SboxTranslationFiles.cs
    translationtochinese.editor.csproj
  Localization/
    settings.json
    ui.zh-CN.json
    api.json
    api.full.zh-CN.json
  UnitTests/
  translationtochinese.sbproj
```

### 目录说明

- `Assets/3rd/harmony`
  - Harmony 依赖。
  - 编辑器补丁安装时会用到它。

- `Editor/SboxChinesePatch.cs`
  - Harmony patch 安装入口。
  - 负责菜单、按钮、原生 Qt 文本、Inspector 元数据 getter 等补丁。
  - 也负责 `Translation` 菜单项和 UI 重建逻辑。

- `Editor/SboxChineseDictionary.cs`
  - 运行时翻译入口。
  - 负责查词典、收集 missing 文本、做少量标准化处理。

- `Editor/SboxTranslationFiles.cs`
  - 负责加载语言文件。
  - 支持从项目根目录、`Libraries/translationToChinese`、以及输出目录回退读取翻译资源。

- `Localization/settings.json`
  - 当前语言、回退语言、missing 收集开关。

- `Localization/ui.zh-CN.json`
  - 普通 UI 文本翻译。
  - 适合菜单、按钮、面板标题、工具描述、短句。

- `Localization/api.json`
  - 原始 API 数据源。
  - 用来和完整翻译版做 `DocId` 对齐。

- `Localization/api.full.zh-CN.json`
  - 完整 API 翻译文件。
  - 适合 `Name`、`Title`、`Description`、`Summary` 等文档层内容。

## 使用方式

## 1. 在另一个项目里引入

最稳的方式是直接复制整个库目录：

```text
你的项目/
  Libraries/
    translationToChinese/
```

至少要保留：

- `Assets/3rd/harmony`
- `Editor`
- `Localization`
- `translationtochinese.sbproj`

然后让目标项目的 editor 工程引用：

- `Libraries/translationToChinese/Editor/translationtochinese.editor.csproj`

如果你是从已有模板项目复制，这步通常已经接好了。

## 2. 打开项目并验证

启动 s&box 编辑器后，菜单栏会出现：

- `Translation`

可用菜单包括：

- `Apply Chinese Patch`
- `Reload Translation Files`
- `Print Missing Texts`
- `Clear Missing Texts`
- `Restart Editor UI`

建议初次验证时按这个顺序：

1. 打开项目
2. 点击 `Translation -> Apply Chinese Patch`
3. 点击 `Translation -> Reload Translation Files`
4. 如有必要，点击 `Translation -> Restart Editor UI`

如果加载成功，Console 里通常会看到类似：

```text
SboxTranslationFiles Loaded translations. Current=zh-CN, Fallback=en, UI=..., API=...
```

## 3. 语言文件加载顺序

为了兼顾“项目可覆盖”和“库自带默认值”，当前实现会按下面顺序寻找翻译文件：

1. `项目根目录/Localization`
2. `项目根目录/Libraries/translationToChinese/Localization`
3. `程序集目录/translationtochinese/Localization`
4. `程序集目录/.vs/output/translationtochinese/Localization`

Harmony 也会按类似思路寻找：

1. `项目根目录/Assets/3rd/harmony/0Harmony.dll`
2. `项目根目录/Libraries/translationToChinese/Assets/3rd/harmony/0Harmony.dll`
3. 输出目录和 `.vs/output` 回退路径

这样做的原因是：s&box 在 library 模式下，运行目录不总是项目根目录，不能只依赖单一路径。

## 如何新增或更新其他语言

当前这套结构天然支持多语言。

假设你要增加日语：

1. 在 `Localization/settings.json` 中设置：

```json
{
  "CurrentLanguage": "ja",
  "FallbackLanguage": "en",
  "CollectMissingTexts": true
}
```

2. 新增 UI 词典：

```text
Localization/ui.ja.json
```

3. 新增 API 翻译文件：

```text
Localization/api.full.ja.json
```

4. 保留原始 `api.json` 作为对齐源文件

5. 在编辑器中点击：

- `Translation -> Reload Translation Files`

如有必要，再点：

- `Translation -> Restart Editor UI`

### 推荐分工

- `ui.<locale>.json`
  - 短 UI 文本
  - 菜单项
  - 面板标题
  - 常见工具说明

- `api.full.<locale>.json`
  - API 文档层文本
  - 属性名、类型显示名
  - 描述、摘要、返回值、参数说明

## 如何补充未翻译内容

## 1. 收集 missing

在编辑器中：

1. 点击 `Translation -> Clear Missing Texts`
2. 去操作你想覆盖的界面
3. 点击 `Translation -> Print Missing Texts`

Console 会输出：

```text
SboxChinesePatch [missing] Some Text
```

## 2. 判断应该补到哪里

优先按下面规则补：

- 普通界面短文本 -> `ui.zh-CN.json`
- API / Inspector / 类型描述 / 属性文档 -> `api.full.zh-CN.json`

### 一般建议

补到 `ui.zh-CN.json`：

- 按钮
- 菜单
- 面板标题
- 工具说明
- 选项项名

补到 `api.full.zh-CN.json`：

- 属性名
- 组件名
- `Name`
- `Title`
- `Description`
- `Summary`
- `Return`
- `Params`

## 3. 重载验证

修改 JSON 后：

1. `Translation -> Reload Translation Files`
2. 如有必要，`Translation -> Restart Editor UI`

## Harmony 使用说明

本项目使用了 Harmony。

用途是：

- patch s&box 编辑器中的 C# / Qt 文本显示路径
- 在不改引擎源码的前提下实现运行时汉化

### 会不会影响其他项目

结论：**通常不会影响其他项目文件，但会影响当前编辑器进程中的方法行为。**

更准确地说：

- Harmony patch 是“进程内”的，不是“项目文件修改”
- 它不会改写其他项目磁盘上的代码或资源
- 但它会在当前 s&box 编辑器进程里 patch 目标方法

这意味着：

- 在当前项目中启用库时，编辑器 UI 文本路径会被拦截
- 如果同一个编辑器进程中还有别的库也 patch 了相同方法，可能产生冲突
- 冲突通常表现为：
  - 文本不生效
  - 文本被别的 patch 覆盖
  - 某些路径重复翻译

### 风险评估

风险总体可控，但不是零。

主要风险点：

1. patch 目标较通用
   - 例如按钮文本、菜单文本、原生 Qt setter
   - 如果别的 editor 插件也 patch 同一方法，可能互相叠加

2. Harmony 是进程级生效
   - 不是某个单独面板局部生效

3. s&box 更新后，某些方法签名可能变化
   - 这会导致 patch 漏挂或部分失效

### 当前设计如何降低影响

当前库已经做了几件事来尽量收敛影响：

- 使用自己的 Harmony ID
- 安装前会尝试清理本库旧 patch
- 翻译文件优先读取项目本地覆盖
- 未命中时回退原文，不强行替换空值

### 实际建议

如果你在多个项目里复用这个库，建议：

1. 每个项目都使用同一份库逻辑，不要维护多份分叉
2. 遇到文本异常时先检查是否有其他插件也 patch 了相同 UI 路径
3. s&box 更新后，优先验证：
   - Harmony 是否加载成功
   - `Loaded translations...` 是否正常
   - 核心菜单/Inspector 是否仍然能翻译

## 迁移到其他机器或项目时的注意事项

这个库的 editor csproj 目前依赖本机 s&box 安装路径和输出路径。  
如果你换机器，通常需要确认：

- `Editor/translationtochinese.editor.csproj`

里的引用路径是否仍然有效，例如：

- `D:/Game/steams/steamapps/common/sbox/...`

如果新机器上的 s&box 安装目录不同，需要同步调整这些路径，或者重新生成对应项目。

## 建议的维护流程

推荐把日常维护分成三步：

1. 补覆盖率
   - 通过 missing 收集新词条

2. 补语言包
   - 优先改 JSON，不改代码

3. 只在必要时补 patch
   - 当文本根本没走翻译入口时，再改 `Editor/*.cs`

这样可以尽量让后续工作保持数据驱动，减少代码层面的维护成本。
