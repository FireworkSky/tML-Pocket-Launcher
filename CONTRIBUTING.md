# Contributing

Read [architecture](docs/architecture.md), [code conventions](docs/code-style.md) and [development](docs/development.md) before changing code.

Keep changes focused and preserve upstream source layout under `external/`. Run `scripts/checks/verify.ps1`; include relevant APK/device verification when changing build inputs, JNI or gameplay adapters.

Use concise Conventional Commit messages. Pull requests should explain the concrete problem, resulting behavior and validation. Do not commit generated files, imported games, commercial resources, credentials, signing keys or machine-specific configuration.
