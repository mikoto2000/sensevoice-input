# プライバシーポリシーの公開

公開 URL: https://mikoto2000.github.io/sensevoice-input/privacy/

本文の原本は `store/privacy-policy.ja.md` です。このフォルダーには HTML テンプレートと CSS だけを置きます。本文を二重管理しません。

GitHub の Settings → Pages → Build and deployment → Source を **GitHub Actions** にします。`main` に対象ファイルを push すると `.github/workflows/pages.yml` がビルドと公開を行います。Actions から手動実行もできます。PR ではビルドのみ行います。`release.yml` の ZIP 配布とは独立しています。

PowerShell 7 でローカル生成:

```powershell
./scripts/build-pages.ps1
```

出力は Git 管理外の `artifacts/pages/` です。再生成するときは `-OutputDirectory artifacts/pages-preview-2` のように未使用の出力先を指定します。本文の未置換プレースホルダー、施行日の欠落、HTTPS 公開 URL の欠落を検出するとビルドは停止します。

公開するのは生成したページと CSS だけです。JavaScript、外部フォント、広告、アクセス解析は使いません。ポリシーを更新するときは原本と施行日を変更し、公開後に上記 URL を確認してください。
