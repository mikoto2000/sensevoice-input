# 公開準備ファイルの検証記録

実施日: 2026-09-24

## 実施した検証

- PowerShell スクリプト2本の構文解析: 成功。
- 未入力の `identityName` に対する `-ValidateOnly`: 意図どおりエラーで停止。
- 検証専用 ID による設定・manifest XML・画像寸法の検証: 成功。
- .NET SDK 10.0.401 / .NET runtime 10.0.12 で自己完結型 Release publish: 成功。
- Windows SDK 10.0.26100.0 の MakeAppx による manifest 検証と MSIX 作成: 成功。
- 作成した ZIP 形式の MSIX 内に、manifest、block map、実行ファイル、CoreCLR、WPF、実際に使用した .NET runtime pack のライセンス・第三者通知があることを確認。
- パッケージの SHA256 と `build-info.json` の一致: 成功。
- 既存のアイコンから生成した300×300の掲載用画像を目視確認。
- `git diff --check`: 成功。

## 検証専用パッケージ

この ID は Partner Center で予約したものではありません。**Store 提出には使用しないでください。**

- Identity: `SenseVoiceInput.LocalValidation`
- Publisher: `CN=SenseVoiceInput Local Validation`
- Version: `1.0.0.0`
- 署名: なし
- サイズ: 約297.1 MiB（モデルと NVIDIA ランタイムは含まない）
- 出力: `artifacts/store-validation-build-03/SenseVoiceInput-1.0.0.0-x64.msix`
- SHA256: `9ed3278e52617e0462f6b28fb8cf5b4fa50c4a6feaf85928b6b378f09dc9eb07`
- ベースコミット: `58b6aaf` + この作業の未コミット変更

正式な ID での再生成、WACK、署名後のインストール・更新・削除、GPU なしの PC での実認識は未実施です。MakeAppx の成功をこれらの検証の代わりには扱いません。公開前の残作業は [submission-checklist.md](submission-checklist.md) を参照してください。
