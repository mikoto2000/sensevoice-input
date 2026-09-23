# 開発ガイド

利用者向けの導入・操作説明は [README.md](README.md)、第三者コンポーネントの条件は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を参照してください。この文書のコマンドは、特記がなければリポジトリのルートで実行します。

## 開発環境とビルド

Windows 11 x64 と .NET SDK 10.0.401（`global.json` で指定）を使用します。実マイクのテストにはマイク、CUDA のテストには対応する NVIDIA GPU とドライバーが必要です。

```powershell
dotnet restore --locked-mode
dotnet build -c Release
.\scripts\run.ps1 -Settings
```

CPU で起動する場合:

```powershell
.\scripts\run.ps1 -Settings -Backend CPU
```

実行時の主要依存は NAudio 2.2.1、ONNX Runtime GPU 1.28.0、MathNet.Numerics 5.0.0 です。NuGet の依存は各 `packages.lock.json` に固定しています。CUDA 13 / cuDNN 9 を使用します。

公開構成は Whisper＋Silero です。SenseVoice のモデル取得・認識機能と sherpa-onnx の実行依存は削除しました。アプリと C# テストは Python プロセスを起動しません。

## モデルの準備

初回起動時にアプリが自動取得します。開発用にリポジトリ内へ事前配置する場合:

```powershell
.\scripts\download-whisper-model.ps1
```

Whisper と Silero VAD を取得します。Whisper だけなら `-SkipVad`、Silero 単体なら `scripts/download-vad-model.ps1` を使います。

```text
models/whisper-large-v3-turbo/
  encoder_model_fp16.onnx
  decoder_model_merged_fp16.onnx
  config.json
  preprocessor_config.json
  generation_config.json
  tokenizer.json
  tokenizer_config.json
  Whisper-LICENSE.txt
  Whisper-SOURCE.txt
models/silero_vad.onnx
models/Silero-VAD-LICENSE.txt
models/Silero-VAD-SOURCE.txt
```

Whisper は [onnx-community の ONNX 変換版](https://huggingface.co/onnx-community/whisper-large-v3-turbo/tree/360ebcde2559d60bb474678be3c1de9ef347d01a)の FP16 を使用します。固定リビジョン、ファイル一覧、SHA256 は [whisper-model-manifest.json](scripts/whisper-model-manifest.json)、Silero の取得元と SHA256 は [出典文書](licenses/Silero-VAD-SOURCE.txt)に記載しています。

アプリの既定保存先は `%LOCALAPPDATA%\sensevoice-input\models` です。Whisper の準備に必要なファイルが不足していれば既定保存先へ取得し、成功後に設定を更新します。指定フォルダーにモデルが揃っている場合は再利用します。

アプリの取得処理は SHA256 を照合し、途中のファイルを失敗・中止時に削除します。再試行では検証済みファイルを再利用しますが、ファイル途中からの再開には対応していません。アプリ管理下の保存先にはライセンス・出典も保存し、旧版で不足していた文書を補います。利用者が指定した外部モデルのフォルダーには、自動で公式モデルのライセンスを付与しません。

## CUDA ランタイムの準備

GPU 用 DLL を事前配置する場合は [licenses/nvidia/](licenses/nvidia/) の条件を確認して実行します。

```powershell
.\scripts\download-cuda-runtime.ps1 -AcceptNvidiaLicense
```

[cuda-runtime-manifest.json](scripts/cuda-runtime-manifest.json) で NVIDIA 公式アーカイブの URL・SHA256・対象 DLL を固定しています。スクリプトは DLL とライセンス・通知文書を `artifacts/cuda-runtime` に保存します。ドライバーやシステム設定は変更しません。

アプリは `%LOCALAPPDATA%\sensevoice-input\cuda-runtime`、アプリ隣接の `cuda-runtime`、アプリのフォルダー、開発環境の `artifacts/cuda-runtime` を確認します。未準備なら利用条件への同意後に取得し、DLL の検索パスはアプリのプロセス内で設定します。CPU では取得しません。

CUDA 初期化失敗は `CudaUnavailable` として扱い、自動 CPU fallback は行いません。

## 設定と起動オプション

新規設定の主要項目:

```json
{
  "engine": "WhisperOnnx",
  "backend": "CUDA",
  "language": "ja",
  "modelDirectory": "C:\\models\\whisper-large-v3-turbo",
  "textInputMode": "Unicode"
}
```

既定の設定保存先は `%LOCALAPPDATA%\SenseVoiceInput\settings.json` です。

| 引数 | 用途 |
|---|---|
| `--settings` | 起動時に設定画面を表示 |
| `--settings-dir <path>` | 設定・ログを別フォルダーに隔離 |
| `--model-dir <path>` | モデルフォルダーを起動時に上書き |
| `--engine WhisperOnnx` | 認識エンジンを指定（公開版は Whisper のみ） |
| `--backend CPU` / `--backend CUDA` | 実行 backend を起動時に上書き |

`run.ps1` はリポジトリ内の Whisper モデルが存在する場合、そのパスを渡します。設定の相対モデルパスはプロセスの作業ディレクトリ基準です。日本語 / transcribe は固定です。

旧 SenseVoice 設定（`engine` がなく `modelDirectory` がある場合を含む）は、マイク・トリガーを保持して Whisper の標準モデルパスへ移行します。CPU/Auto は CPU、CUDA は CUDA として引き継ぎます。警告を表示し、利用者が保存するまで元ファイルを上書きしません。旧 Caps Lock 専用設定はキーの再設定が必要です。不正なトリガーは対象機能を無効化します。

## テスト

```powershell
dotnet test -c Release
```

通常テストはモデルやマイクを使いません。実モデルのテストには、手元の日本語 WAV とモデルを指定します。

```powershell
$env:WHISPER_TEST_MODEL = (Resolve-Path models/whisper-large-v3-turbo).Path
$env:WHISPER_TEST_WAV = 'C:\audio\sample-ja.wav'
$env:SENSEVOICE_TEST_VAD = (Resolve-Path models/silero_vad.onnx).Path
$env:WHISPER_TEST_PROVIDER = 'CPU'
dotnet test -c Release
```

`SENSEVOICE_TEST_VAD` はアプリ名に由来する既存の変数名で、指すモデルは Silero です。実マイクを使う場合だけ `SENSEVOICE_TEST_MICROPHONE=1` を追加します。マイクのテストでは短時間録音とデバイス解放を検証し、録音データは保存しません。

Whisper の実モデルテストは `WHISPER_TEST_PROVIDER=CPU` 以外では CUDA を使います。CUDA ランタイムを準備し、必要に応じてテストプロセスの PATH に追加してください。`CUDA_SETUP_TEST=1` は、外部 PATH に頼らずローカルランタイムを検出して CUDA provider を登録するテストを有効にします。

Log-Mel の数値 fixture は [生成手順](tests/SenseVoiceInput.Windows.Tests/Fixtures/README.md)を参照してください。再生成に限り Python と NumPy を使用します。

## 音声認識の実測と診断

上の実モデル用の環境変数を設定すると、既存の統合テストで日本語 WAV の認識結果・処理時間・RTF とセッション再利用を確認できます。

```powershell
dotnet test tests/SenseVoiceInput.Windows.Tests -c Release --filter 'FullyQualifiedName~WhisperIntegrationTests.JapaneseWaveAndSessionReuse' --logger 'console;verbosity=detailed'
```

このテストは同じ音声を2回認識し、詳細出力に発話本文も表示します。実モデル用の環境変数を設定していなければスキップされます。複数の音声を確認する場合は `WHISPER_TEST_WAV` を切り替えて実行してください。

通常の `diagnostic.log` にはモデルロード時間、provider、音声長、前処理/encoder/decoder/合計時間、トークン数、RTF、エラー分類を記録します。発話本文・録音・例外メッセージは含めません。

開発時に `WHISPER_PROFILE_DIR` を指定すると ONNX Runtime の演算プロファイルを保存します。CUDA での演算配置を確認できます。形状関連など一部の演算は ONNX Runtime の判断で CPU に配置されます。

## 実装上の前提

- PTT と AUTO は同じ `ISpeechRecognitionService` を使用します。Whisper のセッション・Tokenizer を初回認識時にロードして再利用し、同時認識は1件に制限します。
- キャンセルは前処理、decoder 各ステップ、ONNX Run の終了要求に伝搬します。モデルの初回ロード中は終了を待って破棄します。
- PTT は最大60秒の音声を Whisper の30秒窓に分割して処理します。窓境界をまたぐ単語は精度が落ちる可能性があります。
- FP16 重みを基準とします。INT8/Q4、DirectML/TensorRT は対象外です。CPU では ONNX Runtime 内部で一部の FP16 演算が float32 に変換されます。
- Silero VAD は ONNX Runtime CPU で直接実行します。指定した v4 モデルの入出力に対応し、別世代のモデルは非対応です。区間検出は発話250ms・語頭保護500ms・既定無音800ms、切り出し最大30秒です。
- 直接入力は IME の OFF を要求し、その状態を維持します。IMM 互換窓を持たないアプリなどには制約があります。貼り付け方式は完了 ACK を取得できないため、既定1500ms待って復元します。

## 配布ビルドとライセンスの保守

標準は .NET Desktop Runtime 10 x64 を別途必要とする framework-dependent 配布です。

```powershell
dotnet restore --locked-mode
dotnet test -c Release --no-restore
.\scripts\publish.ps1 -Destination artifacts/release
```

`publish.ps1` は配布先が新規または空であることを確認し、Release / win-x64 / framework-dependent で publish します。中間成果物は `artifacts/publish-build` に分離し、開発用アプリが使用中の DLL との競合を避けます。self-contained / single-file 配布はこの手順の対象外です。

通常の build / publish でも、`README.md`、`DEVELOP.md`、`LICENSE`、`THIRD_PARTY_NOTICES.md`、`licenses/`、`docs/`、モデル・CUDA の manifest をコピーします。配布フォルダーを ZIP 化するときもライセンス・通知文書を残してください。

標準の配布物にモデル・NVIDIA DLL は含めません。別途同梱する場合は、各モデルや NVIDIA の再配布条件を確認し、対応するライセンス・通知・出典も同梱してください。本体の MIT は第三者コンポーネントの条件を置き換えません。

依存を更新する際は、lockfile、manifest、[第三者通知](THIRD_PARTY_NOTICES.md)、ライセンス原文と [取得元・ハッシュ](licenses/sources.json) を合わせて更新します。モデルとビルド成果物は Git 対象外です。

## GitHub Releases への ZIP 公開

[Release Windows ZIP](.github/workflows/release.yml) は、`v1.0.0` のような `v` 付きバージョンタグの push で起動します。`v1.0.0-rc.1` などハイフン付きのタグはプレリリースとして公開します。

リリース対象の変更（ワークフローを含む）をコミットして GitHub に push した後、対象コミットにタグを付けます。

```powershell
git tag v1.0.0
git push origin v1.0.0
```

タグは `vMAJOR.MINOR.PATCH`、または `vMAJOR.MINOR.PATCH-rc.1` などの形式を使用してください。ブランチの push だけでは起動しません。ワークフローはタグのコミットを Windows runner で checkout し、`global.json` の SDK を導入して以下を実行します。

1. `dotnet restore --locked-mode` と通常テスト（モデル・GPU・実マイクのテストはスキップ）。
2. `scripts/publish.ps1` による Windows x64 の配布ビルド。
3. `SenseVoiceInput-v1.0.0-win-x64.zip` と、同名に `.sha256` を付けた検証ファイルの作成。
4. GitHub Release を下書きで作成し、両ファイルのアップロード完了後に公開。

ZIP は同名のトップレベルフォルダーを持ち、アプリ・ドキュメント・ライセンスを含みます。.NET Desktop Runtime 10 x64 は別途必要です。モデル・NVIDIA DLL は含めません。リリース本文には導入手順と GitHub が生成した変更履歴を載せます。

公開には組み込みの `GITHUB_TOKEN` とジョブの `contents: write` 権限を使い、追加の PAT は不要です。リポジトリ・組織側で GitHub Actions と当該権限が許可されている必要があります。使用する Actions はコミット SHA で固定しています。

失敗した実行は Actions 画面から再実行できます。アップロード途中の下書きがあれば添付を更新して再開します。公開済みの同じタグのリリースは変更せず、エラーで停止します。公開済み成果物を修正するときは新しいバージョンタグを作成してください。

GitHub に公開せず、同じ ZIP 作成をローカルで確認する場合:

```powershell
.\scripts\package-release.ps1 -Version v1.0.0 -OutputDirectory artifacts/packages
```

出力済みの ZIP や SHA256 は上書きしません。再確認には別の出力先を指定してください。

## 設計・過去の検証記録

以下は各実装・検証時点の記録です。削除済みの SenseVoice や sherpa-onnx に関する記載も残っています。現在の開発・配布手順はこの文書を優先してください。

- [設計](docs/architecture.md)
- [Whisper 仕様と実測](docs/whisper-onnx.md)
- [TDD 記録](docs/tdd.md)
- [自動録音](docs/automatic-recording.md)
- [入力トリガー](docs/input-triggers.md)
- [手動動作確認](docs/smoke-test.md)
