# Plugins / 插件

## Installing and changing versions / 安装与切换

Open **Plugins**, import a ZIP, review its name, version and claimed publisher, and confirm the source. The app checks its inventory, host/API compatibility, managed entry point and local game connection component in an isolated child process. Loading checks do not connect to the game or consume daily attempts. Failed checks keep the previous selection.

在「插件」页导入或拖入 ZIP，核对名称、版本及声明的发布者后确认。文件清单、主程序/API 兼容性、程序入口和本机游戏接口均通过后，才选择新版本。主程序没有预装或宣传外部插件的具体能力。

- Import/update: a new immutable version is stored separately. No running files are overwritten.
- Enable/disable/revert: changes apply after restarting Dustweave. The game does not need to restart. Active tasks and isolated account windows must finish/close before restarting the host.
- Remove: unregisters the selected version and disables it on the next start if active. Payloads are retained so existing processes are not broken; this is not a disk-cleanup action.
- One active package is supported by the current API. Installing another version keeps previous versions available for rollback.
- Names and host UI support simplified Chinese, traditional Chinese and English, with English then plugin ID as metadata fallbacks.

## Package contract / 包结构

The ZIP root contains `plugin.json` and exactly the files listed by it. Archives with links, traversal, Windows device/stream names, duplicate entries or unlisted files are rejected. Limits: 256 MiB archive, 768 MiB expanded, 4,096 entries. SHA-256 proves integrity, **not publisher identity**. Publisher signatures and an online plugin feed are not implemented; only import packages from a source you trust. Plugins execute local code and are not sandboxed.

```json
{
  "id": "example.extension",
  "version": "1.0.0",
  "publisher": "Example",
  "names": {"zh-CN": "示例扩展", "zh-TW": "範例擴充", "en-US": "Example extension"},
  "minHostVersion": "0.9.12",
  "maxHostVersion": "0.9.99",
  "apiVersion": 4,
  "bridgeExtensionApi": 1,
  "runtime": "net8.0-windows-x64",
  "entryAssembly": "managed/Example.dll",
  "entryType": "Example.Extension",
  "capabilities": ["sample_task"],
  "hookSources": ["hook/Extension.cs"],
  "bindingContract": "data/client-bindings.json",
  "files": [
    {"path": "managed/Example.dll", "sha256": "..."},
    {"path": "hook/Extension.cs", "sha256": "..."},
    {"path": "data/client-bindings.json", "sha256": "..."}
  ]
}
```

This is a structural example, not an executable sample. API 4 uses `IDailyExtension` from the host Core assembly; arbitrary capability names do not create new task definitions. Existing task integrations keep using the shared account identity, queue, control ownership, transport and recovery workflow.

`bindingContract` is optional for components without renamed client interfaces. Components that use them should ship a hashed contract captured from their matching source baseline. `generate-plugin-contract <baseline Managed> <output>` captures only plugin source references. The base and extension contracts are adapted separately; private interface descriptions never need to be embedded in the public host. Updating the plugin changes only the on-demand daily module identity, not the common observer.

## Storage and lifecycle / 存储与生命周期

Under the existing application data root:

```text
extensions/
  selection.json                 atomic desired selection and previous version
  versions/<manifest-sha256>/    immutable payload
  staging/<transaction>/         temporary extraction
  checks/<transaction>/          isolated load/component reports
  last-error.json                latest manager failure
```

`DailyPlugin.Current` pins the session's version. Shared connection helpers and isolated account workers receive that same root. Changing the desired selection does not retarget the running queue. A corrupt selection disables plugins instead of falling back to an unrelated local version. OTA preflight checks the selected plugin against the incoming host version, and does not overwrite this data directory.

`DUSTWEAVE_PLUGIN=<directory>` remains an explicit developer override; `none` disables plugins. Without a managed selection, the optional legacy local path is `plugins/extension` next to the app. Merely importing a package does not disable an existing local installation.

Managed plugin dependencies use a separate `AssemblyLoadContext`, sharing host contract assemblies. This isolates dependency identities; it does **not** isolate native crashes. Preflight uses a child process and stops it on timeout (90 seconds). No UI or game action is sent by the host during preflight.

## Next API / 后续接口

This delivery completes the local installation lifecycle. A smaller standalone SDK, plugin-declared tasks/settings/translations, multiple concurrent packages, a dedicated heavy-computation worker, publisher signatures and independent online update feeds are separate follow-up work. Do not claim those capabilities from API 4 or distribute private implementation/data in the public repository.

Game execution after module handoff still needs live acceptance. Offline loading, adaptation and synthetic recovery tests are not evidence that a real daily task completed.
