# PCLX Mixin packages

PCL.Core discovers installed PCLX packages, validates their `plugin.json`, loads the declared
assembly, reads one or more Mixin configuration files, and applies the listed `PCL.Mixin`
classes before window creation.

`plugin.json` uses `entryAssembly`, `pclCoreVersion`, and either `mixinConfig` or
`mixinConfigs`. It may also declare `logo` as a package-relative path or HTTP/HTTPS URL.
A package can register prerequisite plugins with `dependencies`:

```json
{
  "id": "example.python-feature",
  "version": "1.0.0",
  "entryAssembly": "lib/Example.PythonFeature.dll",
  "mixinConfig": "mixins.json",
  "dependencies": [
    { "id": "pcl.bridge.python", "version": ">=1.0.0 <2.0.0" }
  ]
}
```

The marketplace `manifest.json` supports the same `dependencies` list at the top level, and a
single `versions[]` entry may override it for that release. The downloaded `plugin.json` must
match the resolved market declaration. Dependencies must already be installed and enabled; Core
loads them first, detects cycles, propagates prerequisite failures, and shares their public main
assemblies with dependent plugin load contexts.

This allows an ordinary prerequisite plugin to implement a Python, JavaScript, or reusable-service
Bridge. The Bridge owns its runtime and public API; PCL.Core does not restore Jint, a generic script
engine, lifecycle entry points, or a Host API.

A Mixin configuration follows the Sponge-style shape:

```json
{
  "required": true,
  "package": "Example.Plugin.Mixins",
  "mixins": ["LauncherMixin", "DownloadMixin"],
  "priority": 1000,
  "injectors": { "defaultRequire": 1 },
  "plugin": "Example.Plugin.MixinConfigPlugin"
}
```

There is no generic plugin lifecycle, dependency-injected host, event bus, UI API, command API,
or script runtime. A configuration processor may only implement `IMixinConfigPlugin` to decide
whether a Mixin applies and to validate its dedicated pre/post-apply phases.

The package installer retains temporary staging, SHA-256 verification, ZIP traversal protection,
path-boundary checks, update backup, and rollback behavior. Runtime patch rollback is internal and
is used only when a required Mixin fails; third-party Mixins are not hot-unloaded.

## Marketplace sources

The launcher keeps the official `pclnexplugin` GitHub Topic as a built-in client source. The official
Nex_Server `plugin-market.json` and user-added JSON sources only combine developer trust with
manifest and inline-plugin discovery:

```json
{
  "version": 1,
  "name": "Example source",
  "group": "Utilities",
  "tags": ["featured"],
  "developers": [
    { "githubLogin": "example", "displayName": "Example", "level": "trusted" }
  ],
  "manifests": ["https://example.com/plugin/manifest.json"],
  "plugins": []
}
```

A plugin market manifest may declare `logo`, `group`, and `tags`. Relative logos are resolved
against the manifest location. GitHub Topic plugins without a logo use the repository owner avatar.
Git installations persist their repository URL; non-Git installations persist and subscribe to
their manifest/source JSON URL in the local launcher configuration so updates keep using the same source.

The built-in NexDeveloper registry uses the GitHub Raw `plugin-market.json` URL. A user can add
multiple third-party registries through the existing source-management UI or `add-plugin-source` URI Scheme;
market documents must not declare `topics`, because Topic discovery is controlled by the launcher.
no separate developer-source action exists. Developers declared by the built-in registry with
`level: official` receive `Official` identity. Developers declared by a user-added registry receive
`Local` trust only, so a third-party registry cannot grant NexDeveloper official identity. The
existing per-login local trust controls remain available independently.
