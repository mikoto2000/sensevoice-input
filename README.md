# SenseVoice Input

Windows 11 向けのローカル音声入力 MVP。**設定したトリガーを押している間だけ録音し、離すと SenseVoiceSmall で日本語認識して入力**します。WPF の設定画面とタスクトレイに常駐します。

## Requirements

- Windows 11 **x64**、マイク。Windows の「デスクトップ アプリによるマイクへのアクセス」が許可されていること。
- 開発: .NET SDK **10.0.401**（`global.json`、最新パッチ許容）。実行: .NET Desktop Runtime 10 x64。
- sherpa-onnx 向け SenseVoiceSmall INT8 ONNX モデル（モデル約 239 MB、配布アーカイブ約 163 MB）。
- 初回の NuGet 復元とモデル取得時のみインターネット接続。通常の認識処理に接続は不要。

## Build

リポジトリ直下で実行します。

```powershell
dotnet restore
dotnet build
```

依存バージョンは `packages.lock.json` に固定しています。再現確認には `dotnet restore --locked-mode` を使用できます。NuGet キャッシュはリポジトリ内 `.packages/`（Git 対象外）です。

## Model setup

```powershell
.\scripts\download-model.ps1
.\scripts\download-vad-model.ps1
```

公式リリースを `models/` に取得し、固定 SHA256 を照合して展開します。アプリはモデルを自動ダウンロードしません。

使用モデル: `sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17`。必要ファイルは同一フォルダーの `model.int8.onnx` と `tokens.txt`。別のエクスポート形式の ONNX は使用できません。

モデルの利用条件は配布物の `LICENSE` と [SenseVoice](https://github.com/FunAudioLLM/SenseVoice)、[FunASR のライセンス案内](https://github.com/modelscope/FunASR#license) を確認してください。モデルは Git に含めません。

## Test

```powershell
dotnet test
```

通常は OS に触れないテストを実行し、実モデル・VAD・マイクの 4 件は明示的な opt-in がない場合スキップします。

```powershell
$env:SENSEVOICE_TEST_MODEL = (Resolve-Path '.\models\sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17').Path
$env:SENSEVOICE_TEST_MICROPHONE = '1'
$env:SENSEVOICE_TEST_VAD = (Resolve-Path '.\models\silero_vad.onnx').Path
dotnet test
```

このモードでは公式 `test_wavs/ja.wav` の実推論と、既定マイクの短時間録音・停止・再オープンを検証します。マイク音声は保存しません。結果と実機確認の区別は [smoke-test.md](docs/smoke-test.md)、TDD サイクルは [tdd.md](docs/tdd.md) を参照してください。

## Run

初回は設定画面を開きます。

```powershell
.\scripts\run.ps1 -Settings
```

通常のトレイ起動:

```powershell
.\scripts\run.ps1
```

同等の直接実行:

```powershell
dotnet run --project src/SenseVoiceInput.App -- --model-dir "F:\models\sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17" --settings
```

`--model-dir` は実際のモデルフォルダーに置き換えます。省略すると保存済み設定、それもなければ実行ファイル横の `models/<モデル名>` を使用します。開発用の `--settings-dir <path>` で設定・ログの保存先を隔離できます。

## Usage

1. 起動し、トレイアイコンのダブルクリックまたは **Open Settings** からマイクとモデルフォルダーを確認して保存。
2. Notepad やエディターの入力位置にフォーカスを置く。
3. 設定したトリガーを押したまま話す（初期候補は F12、任意に変更可能）。
4. トリガーを離す。組み合わせの場合は修飾キーもすべて離す。認識・入力が終わるまで対象ウィンドウと入力位置を維持する。
5. トレイアイコンが通常状態に戻ったら次の入力が可能。

設定画面を閉じるとトレイへ隠れます。終了はトレイメニューの **Exit**。録音開始は盾アイコン、認識・入力中は情報アイコンで表示します。通常権限で起動してください。

## Architecture

| Project | 責務 |
|---|---|
| `SenseVoiceInput.Core` | 状態遷移、Push-to-Talk 調停、インターフェース、PCM 変換、クリップボード手順、設定、診断ログ |
| `SenseVoiceInput.Windows` | WASAPI、SenseVoice C API、キーフック、Clipboard、SendInput、Foreground Window |
| `SenseVoiceInput.App` | WPF、設定 ViewModel、トレイ、DI の composition root、起動・終了 |
| `SenseVoiceInput.Core.Tests` | Fake を使用した状態・失敗・キャンセル・競合・設定・プライバシーのテスト |
| `SenseVoiceInput.Windows.Tests` | モデル欠落等の境界テスト、明示実行の実推論・実マイクテスト |

DI はコンストラクター注入で実施し、専用コンテナーを追加していません。主要ロジックは code-behind に置きません。詳細: [architecture.md](docs/architecture.md)。

依存を採用した理由:

- **NAudio 2.2.1**: 実績のある WASAPI capture とデバイス列挙。安定した 2.x API を固定。
- **org.k2fsa.sherpa.onnx 1.13.8**: SenseVoice のリサンプリング・特徴量抽出・CTC デコードと ONNX Runtime CPU を一体提供。C# / ネイティブ C API を同一プロセスで使用。Python プロセスは不要。
- **xUnit 2.9.3 / Microsoft.NET.Test.Sdk / runner / coverlet**: テスト実行と必要時のカバレッジ収集。
- WPF / WinForms NotifyIcon は .NET Desktop の標準機能。

`Microsoft.ML.OnnxRuntime` を重ねて追加せず、sherpa-onnx 同梱の ONNX Runtime を使用します。CPU / Auto を提供し、CUDA / DirectML の設定値は予約だけで明示的に拒否します。UI に未実装バックエンドは出しません。

## Settings and logs

既定保存先: `%LOCALAPPDATA%\SenseVoiceInput\`。

```json
{
  "microphoneDeviceId": null,
  "pushToTalk": { "enabled": true, "trigger": { "type": "KEY_COMBINATION", "keys": ["LEFT_CTRL", "GRAVE"] } },
  "autoVoiceInput": {
    "enabled": true,
    "toggleTrigger": { "type": "DOUBLE_TAP", "keys": ["LEFT_CTRL"], "intervalMs": 350 },
    "onlyWhenTextInputFocused": true,
    "vad": { "enabled": true, "silenceTimeoutMs": 800 }
  },
  "backend": "Auto",
  "modelDirectory": "F:\\models\\sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17",
  "pasteRestoreDelayMs": 1500,
  "textInputMode": "Unicode"
}
```

設定変更は保存後の次回入力から有効。壊れた設定はユーザーへ通知し、元ファイルを自動上書きしません。ログは `diagnostic.log` と最大 1 世代の `.1`（各約 1 MB）。状態イベント、例外型・HResult・スタックを記録します。

キー設定は方式を選び「キーを設定」から実際のキーを押して離します。Escでキャンセル。保存時に競合を検証し、その場で反映します。旧Caps Lock設定は無効化し再選択を促します。不正なトリガーはその機能だけ無効化します。

## Privacy

音声認識はローカルで実行し、音声をクラウドへ送信しません。**認識結果や録音データは保存しません**。例外メッセージもログに含めません。PCM バッファは処理後に消去します（OS のページングやネイティブランタイムの内部メモリまで完全消去を保証するものではありません）。

標準の直接入力では Clipboard を読み書きしません。入力できないアプリでは、設定の「文字の入力方式」で「クリップボードで貼り付け」を選択できます（全アプリ共通の手動設定）。方式の自動切り替えは行いません。既存設定に `textInputMode` がない場合も直接入力になります。

貼り付け方式では認識結果は一時的に Clipboard に入ります。Windows の履歴・クラウド同期除外形式を設定します。第三者のクリップボード監視ソフトや入力先アプリによる保存は制御できません。

## Known limitations

- 単一キー・2～4キーの組み合わせ・2回押し（自動入力切替のみ）に対応。左右のCtrl/Shift/Altを区別。Esc/Windowsキー、Alt+Tab、Ctrl+Alt+Deleteは禁止。
- PTTは1回60秒、自動入力は最大30秒ごとに確定します。Silero VADで発話を判定しますが、環境音・テレビ等を発話と判定することがあります。
- Caps Lockは通常のキーとして扱います。解放イベントが欠けるJIS英数（OEM IMEキー）は非対応。専用の解放推定・Raw Input診断は廃止しました。
- 録音開始時のウィンドウを記録し、認識終了時にフォーカスが違えば入力を中止。自動で前面へ戻しません。同じウィンドウ内でのキャレット移動・タブ移動までは追跡しません。
- 直接入力は Unicode キーイベントを受け付けるアプリが対象です。未対応の場合は貼り付け方式を選択してください。SendInput の部分成功はエラー通知し、二重入力を避けるため自動再試行しません。
- 貼り付け方式では Clipboard の全形式を退避できない場合は貼り付け前に失敗します。特殊な遅延描画・独自形式の完全な復元は保証しません。ロック中の Clipboard はエラーとして通知。
- 貼り付け後は既定 1500 ms 待機して復元します。この間に別のコピー操作があれば復元しません。任意アプリの貼り付け完了 ACK は得られないため、特に遅いアプリでは race が残ります。待機時間を最大 10000 ms まで増やせます。
- 管理者権限のアプリ・UAC の安全なデスクトップへの入力は未対応。Ctrl+V 非対応アプリ、独自ショートカット、修飾キー押下中は入力できないことがあります。自動 Enter は送りません。Terminal の Ctrl+V 設定にも依存。
- ネイティブ推論を途中で強制停止する API はありません。終了要求では推論完了を待って結果を破棄し、入力せず安全に解放します。
- Windows x64 / CPU のみ。モデルフォルダーは ASCII 文字だけのパスを使用（上流の LPStr 設定 ABI の制約を明示的に検証）。
- マイク一覧は起動時取得。機器変更後は再起動。自動起動、TSF、IME 化、辞書、LLM 補正、履歴機能は未実装。

自動入力は設定した切替トリガーでON/OFFします。ONで入力欄にフォーカスするとARMEDとなり、マイクで発話待機します。発話後、既定800msの無音で確定して認識・入力します。認識中はマイクを停止するため、その間の次の発話は取得しません。OFF・対象変更・PTT開始で停止し未確定音声を破棄します。設定保存・再起動後はOFFです。VADモデルは既定で音声モデルの親フォルダーの `silero_vad.onnx`、設定画面で変更可能。詳細は [自動録音](docs/automatic-recording.md)。

## References

- [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [sherpa-onnx C# API](https://k2-fsa.github.io/sherpa/onnx/csharp-api/index.html)
- [公式 SenseVoice モデルと取得方法](https://k2-fsa.github.io/sherpa/onnx/sense-voice/pretrained.html)
- [Windows Clipboard 形式と履歴・同期制御](https://learn.microsoft.com/ja-jp/windows/win32/dataxchg/clipboard-formats)
