## Verification Build

このリリースは GitHub Actions の `release.yml` ワークフロー動作確認用です。
`workflow_dispatch` 経由で draft Release を作成し、以下を検証します:

- `build-release.ps1` が CI ランナー (windows-latest) で完走するか
- 2 種類の ZIP (self-contained / framework-dependent) が生成されるか
- `releases/release_v224.md` の内容が Release body として正しく反映されるか
- ZIP 2本が Release Assets としてアップロードされるか

検証完了後、この draft Release は削除する想定です。
