# Store アイコン原本

`app-source.png` は既存のアプリアイコンの元画像です。リポジトリ外の作業用出力から複製し、Store 用資産の原本として管理します。

次のコマンドで既存デザインをサイズ変換します。

```powershell
.\scripts\create-store-assets.ps1
```

出力先は `packaging/msix/Assets/` です。

- `StoreLogo.png`: 50×50
- `Square44x44Logo.png`: 44×44
- `Square150x150Logo.png`: 150×150
- `AppTile300.png`: 300×300、Partner Center のアプリアイコン候補（パッケージには含めない）

最小構成の画像を用意しています。高 DPI 表示は提出前に確認し、必要に応じて scale 別・targetsize 別の資産を追加してください。スクリーンショットは `store/screenshots/` に保存します。
