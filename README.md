# SenseVoice Input

Windows 11 x64 向けのローカル音声入力アプリ。標準エンジンは **Whisper large-v3-turbo ONNX FP16 / CUDA / 日本語文字起こし**。PTT（押している間の録音）と Silero VAD による自動録音を提供します。SenseVoice は比較用に切替可能です。

## セットアップ

.NET SDK 10.0.401（実行のみなら .NET Desktop Runtime 10 x64）、マイク、CUDA 利用時は対応する NVIDIA GPU とドライバーが必要です。本実装は ONNX Runtime GPU 1.28.0、**CUDA 13 / cuDNN 9** を使用します。モデル・依存ファイルの取得後、認識はオフラインで完結します。Python プロセスは使いません。

```powershell
dotnet restore --locked-mode
.\scripts\download-cuda-runtime.ps1
dotnet build -c Release
.\scripts\run.ps1 -Settings
```

初回起動時、モデルが未配置ならアプリが自動ダウンロードします。保存先は `%LOCALAPPDATA%\sensevoice-input\models`（通常は `%USERPROFILE%\AppData\Local\sensevoice-input\models`）です。Whisper は約1.62GBの FP16 ONNX と設定・Tokenizerを固定リビジョンから取得し、SHA256 を照合します。Silero VAD も取得し、SenseVoice 選択時は既存スクリプトと同じ SHA256 のアーカイブから必要ファイルを展開します。管理者権限は不要です。

設定画面でファイルごとの進捗を確認・中止・再試行できます。準備完了まで音声入力を停止し、失敗時は理由を表示します。途中のファイルは削除し、再試行では検証済みの完了ファイルを再利用します（ファイル途中からの再開には非対応）。既存の指定フォルダーにモデルが揃っていればそのまま利用します。不足時は上記保存先へ取得し、成功後に設定のモデルパスを更新します。設定保存でエンジンを変更した場合も自動確認します。

手動で事前配置する場合は `scripts/download-whisper-model.ps1` も利用できます。CUDA DLL のセットアップは従来どおり別途必要です。

モデル: [onnx-community/whisper-large-v3-turbo](https://huggingface.co/onnx-community/whisper-large-v3-turbo/tree/360ebcde2559d60bb474678be3c1de9ef347d01a)。元モデルのライセンスは[MIT](https://huggingface.co/openai/whisper-large-v3-turbo)。ONNX配布READMEは元モデルを参照しています。ファイル一覧とハッシュは [manifest](scripts/whisper-model-manifest.json)、詳細な仕様・比較結果は [Whisper 実装報告](docs/whisper-onnx.md) を参照してください。

CUDA 取得スクリプトは NVIDIA 公式の固定アーカイブを検証し、DLL とライセンス文書を `artifacts/cuda-runtime` へ置きます。システム設定やドライバーを変更しません。CUDA/cuDNN を既に利用可能なら省略できます。[CUDA 利用条件](https://docs.nvidia.com/cuda/eula/index.html) / [cuDNN 利用条件](https://docs.nvidia.com/deeplearning/cudnn/backend/latest/reference/eula.html) が適用されます。`run.ps1` は子プロセスだけにローカル DLL の検索パスを渡します。

CUDA 初期化に失敗すると `CudaUnavailable` を表示します。自動 CPU fallback はありません。CPU を明示指定する場合:

```powershell
.\scripts\run.ps1 -Settings -Backend CPU
```

## モデルと設定

同一フォルダーに必要なファイル:

```text
models/whisper-large-v3-turbo/
  encoder_model_fp16.onnx
  decoder_model_merged_fp16.onnx
  config.json
  preprocessor_config.json
  generation_config.json
  tokenizer.json
  tokenizer_config.json
models/silero_vad.onnx
```

新規設定の主要項目:

```json
{
  "engine": "WhisperOnnx",
  "backend": "CUDA",
  "language": "ja",
  "modelDirectory": "F:\\models\\whisper-large-v3-turbo",
  "textInputMode": "Unicode"
}
```

設定画面でエンジン・Execution Providerを選択して保存します。日本語 / transcribe は固定です。Whisper は CUDA/CPU、SenseVoice は CPU/Auto に対応します。モデルフォルダーは既存モデルを使う場合に指定できます。

設定の保存先は従来どおり `%LOCALAPPDATA%\SenseVoiceInput\settings.json`。`--settings-dir <path>` で隔離可能。`--model-dir` / `--engine WhisperOnnx` / `--backend CUDA` は起動時上書き、`--settings` は設定画面を表示します。初期モデルパスは `%LOCALAPPDATA%\sensevoice-input\models\whisper-large-v3-turbo`。相対モデルパスは従来どおりプロセスの作業ディレクトリ基準です。

旧設定に `engine` がなく `modelDirectory` がある場合、既存 SenseVoice 設定として維持し案内を表示します。マイクとトリガーは変更しません。旧 Caps Lock 専用設定は引き続き再設定が必要です。不正なトリガーは該当機能を無効にし、元の設定ファイルを勝手に上書きしません。

比較用 SenseVoice:

```powershell
.\scripts\download-model.ps1
.\scripts\run.ps1 -Engine SenseVoice -Settings
```

## 操作

1. 設定画面でマイク、エンジン、モデルを確認し保存。
2. PTT は任意のキーを設定（初期候補F12）。入力欄で押したまま話し、離すと認識・入力します。組み合わせの場合は修飾キーも離してください。
3. 自動入力は設定した切替キーで ON/OFF。入力欄で `ARMED` になった後に話すと、既定800msの無音で確定します。語頭保護の500ms pre-rollは維持しています。
4. 認識中は入力位置を維持してください。AUTOの対象欄変更・OFF・PTT開始で未確定結果を破棄します。

設定画面を閉じるとトレイへ戻ります。再表示はトレイをダブルクリック、終了は **Exit**。保存・再起動時の AUTO は OFF。認識中はマイク待機を止めるため、その間に話し始めた音声は取得しません。

PTT と AUTO は同じ `ISpeechRecognitionService` を使います。Whisper のセッション・Tokenizerを初回認識時にロードして再利用し、同時認識は1件に制限します。キャンセルは前処理、decoder各ステップ、ONNX Runの終了要求に伝搬します。モデルの初回ロード中は終了を待って破棄します。

## ビルド・配布

```powershell
dotnet publish src/SenseVoiceInput.App -c Release -o artifacts/app
# 配布フォルダーから直接起動する場合、CUDA DLLを同梱するか、システム側で検索可能にします。
.\scripts\download-cuda-runtime.ps1 -Destination artifacts/app
```

ビルドは `Directory.Build.targets` で GPU 版 ONNX Runtime を明示配置します。sherpa 同梱の CPU DLL で上書きしないでください。NAudio 2.2.1、sherpa-onnx 1.13.8、ONNX Runtime GPU 1.28.0、MathNet.Numerics 5.0.0 を固定しています。モデル・ビルド成果物は Git 対象外です。

## テストと比較

```powershell
dotnet test -c Release
```

通常は114件成功、モデル/実マイクの6件をスキップ。実モデルと実マイクを含む全120件を実行するには:

```powershell
$env:PATH = (Resolve-Path artifacts/cuda-runtime).Path + ';' + $env:PATH
$env:WHISPER_TEST_MODEL = (Resolve-Path models/whisper-large-v3-turbo).Path
$env:SENSEVOICE_TEST_MODEL = (Resolve-Path models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17).Path
$env:WHISPER_TEST_WAV = Join-Path $env:SENSEVOICE_TEST_MODEL 'test_wavs/ja.wav'
$env:SENSEVOICE_TEST_VAD = (Resolve-Path models/silero_vad.onnx).Path
$env:SENSEVOICE_TEST_MICROPHONE = '1'
dotnet test -c Release
```

`WHISPER_TEST_PROVIDER=CPU` で CPU 実行を選べます。未指定は CUDA。実マイクテストは短時間録音とデバイス解放を検証し、録音データは保存しません。

開発用 A/B コマンド（このコマンドだけは認識文を標準出力へ表示）:

```powershell
dotnet run --project tools/AsrCompare -c Release -- models/whisper-large-v3-turbo models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17 CUDA sample1.wav sample2.wav
```

SenseVoice比較不要なら第2引数を `-` にします。PCM/float WAV対応。各WAVをWhisperで2回実行し、セッション再利用時も比較できます。正解テキストが十分にないためCERによる精度評価は未実施です。

## ログ・プライバシー・制約

- `diagnostic.log` にモデルロード時間、provider、音声長、前処理/encoder/decoder/合計時間、トークン数、RTF、エラー分類を記録します。発話本文や録音は通常ログに記録しません。例外メッセージもログに含めません。
- 開発時だけ `WHISPER_PROFILE_DIR` を指定するとORTの演算プロファイルを保存します。CUDAでの行列演算を確認できます。形状関連の小さな演算はORTの判断でCPUに配置されます。
- 標準は Unicode 直接入力で、クリップボードに触れません。未対応アプリ向けに貼り付け方式を明示選択できます。IMEは文字入力の直前にOFFを要求し、OFFを維持します。IMM互換窓を持たないアプリには制約があります。
- PTTは最大60秒、Whisperは30秒窓に分割して全音声を処理します。窓境界をまたぐ単語は精度が落ちる可能性があります。自動録音の既存上限は30秒。ストリーミング、タイムスタンプ、話者分離、翻訳、専門用語辞書は未実装。
- FP16重みを基準とします。INT8/Q4への変更、DirectML/TensorRTへの変更はしません。CPU選択時はORT内部で一部FP16演算がfloat32へ変換されます。
- 初回のモデルロードとCUDA準備は数秒かかります。無音や環境音の誤検知・Whisperの幻覚は完全には防げません。
- JIS英数のように解放通知が欠けるキーは非対応。Esc/Windowsキー、Alt+Tab、Ctrl+Alt+Deleteは予約。2回押しはAUTO切替のみ。
- 管理者アプリ/UAC画面、独自入力処理のアプリ、修飾キー押下中などでは文字入力できない場合があります。失敗時の自動再送はしません。アプリを自動的に前面へ戻しません。
- 貼り付け方式の全形式退避に失敗した場合は入力を中止します。貼り付け完了ACKはないため復元待機は既定1500ms。別のコピー操作があれば復元しません。Clipboard履歴/同期除外形式を付加しますが第三者アプリは制御できません。
- SenseVoiceのネイティブDecodeは途中中断できず完了後に結果を破棄します。SenseVoiceとSileroのモデルパスにはASCIIパスを使用してください。

詳細: [設計](docs/architecture.md)、[Whisper仕様と実測](docs/whisper-onnx.md)、[TDD記録](docs/tdd.md)、[自動録音](docs/automatic-recording.md)。
