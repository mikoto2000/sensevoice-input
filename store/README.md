# Microsoft Store 公開準備

このフォルダーは Partner Center に転記する資料の原本です。公開するアプリは Windows 11 x64 / 日本語 / WPF の MSIX 版を想定しています。
**資料とパッケージ作成手順を用意した段階です。審査通過・公開可能を意味しません。**

## 格納場所

| 場所 | 内容 | Git 管理 |
|---|---|---|
| `store/` | 公開設定、掲載文、審査メモ、ポリシー原稿、チェックリスト | する |
| `store/assets/` | アイコン原本、掲載画像 | する |
| `store/screenshots/` | 公開版の実画面と撮影指示 | する |
| `packaging/msix/` | パッケージ manifest のテンプレート | する |
| `scripts/package-store.ps1` | .NET 同梱 MSIX 作成 | する |
| `artifacts/store/` | publish 出力、MSIX、検証結果 | しない |

秘密鍵・証明書のパスワード・Partner Center の認証情報は保存しません。Package Name / Publisher は秘密情報ではなく、確定後に設定ファイルへ記録できます。

## 最初に埋める項目

1. Partner Center で開発者登録とアプリ名予約を行う。
2. `store-config.json` の `identityName`、`publisher`、`publisherDisplayName` を Product identity の値で埋める。`publisher` は `CN=...` を含む文字列を正確にコピーする。
3. 表示名・初回バージョン・価格・公開地域を決定する。`1.0.0.0` は初回候補であり、既存公開版がある場合はより大きいバージョンを使う。
4. `privacy-policy.ja.md` の発行者 `mikoto2000` と連絡先 `mikoto2000@gmail.com` を確認する。施行日は 2026年9月24日。
5. ポリシーは GitHub Pages の `https://mikoto2000.github.io/sensevoice-input/privacy/` で公開する。`privacyPolicyUrl` に設定済み。公開手順は [site/README.md](../site/README.md) を参照し、デプロイ成功後にログイン不要で閲覧できることを確認する。
6. 問い合わせ先は `supportContact` にメールアドレスを記録済み。Partner Center のサポート連絡先欄へ転記する。Web のサポート窓口を設ける場合は `supportUrl` に記録する。

ポリシーの音声処理・保存・通信の説明は現実装を基にしています。「お問い合わせで提供される情報」の保持・削除・第三者提供と「ポリシーの変更」は運用方針の案です。公開前に実際の運用と一致することを確認してください。公開先が決まったら、掲載サイト自体が行うアクセス解析などの有無も確認します。

## パッケージ作成

Windows SDK（MakeAppx.exe）と `global.json` 指定の .NET SDK が必要です。リポジトリルートで実行します。

```powershell
# 初期状態では未入力の identity を検出して停止します。
.\scripts\package-store.ps1 -ValidateOnly

# 未使用の出力フォルダーに自己完結型の未署名 MSIX を作成。
.\scripts\package-store.ps1
```

SDK を自動検出できない場合は `-MakeAppxPath 'C:\...\makeappx.exe'` を指定します。
出力済みのフォルダーは上書きしません。再作成時は `-OutputDirectory artifacts/store/別の名前` を指定します。
`-ValidateOnly` は設定・manifest・画像の事前確認であり、WACK や実機検証の代替ではありません。

Store 提出用 MSIX は Microsoft が署名します。このスクリプトでは署名・証明書生成・インストール・アップロードを行いません。
ローカルインストール検証は、隔離したテスト環境でテスト用証明書の署名・信頼設定を行うか、Partner Center のテスト配布を利用します。

## 提出前に残っている作業

- `LoginStartupService` のレジストリ Run 登録を、パッケージ版では `windows.startupTask` と連携させる。現 manifest には未動作の自動起動拡張を宣言していない。
- .NET 未導入・NVIDIA GPU なしの Windows 11 で、生成 MSIX の初回セットアップと CPU 認識を確認する。
- MSIX 内からのモデル保存、CUDA DLL 読み込み、グローバルキー、通知領域、クリップボード、更新・削除を確認する。
- 実画面のスクリーンショットを取得する。`screenshots/README.md` に撮影一覧を用意している。
- 発行者・URL を確定し、ポリシーを公開する。自己完結型で同梱する .NET の通知文書もパッケージへ収録する。
- `submission-checklist.md` を完了し、`certification-notes.en.md` を最終版の挙動に合わせる。

本体の ZIP 配布手順は変更しません。Store 版だけ .NET を同梱するため、掲載文の動作条件も分けています。

今回実施したビルド・パッケージ確認の結果は [validation.md](validation.md) に記録しています。検証専用 ID の出力は提出用ではありません。

## 公式資料

確認日: 2026-09-24。要件は提出時にも再確認してください。ポリシーページには将来の施行日を持つ改定が掲載される場合があります。

- [MSIX の要件・署名・バージョン](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [提出項目](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/create-app-submission)
- [スクリーンショット・画像](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)
- [Store ポリシー](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies)
- [デスクトップアプリの自動起動拡張](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/desktop-to-uwp-extensions)
