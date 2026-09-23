# SenseVoice Input

Windows 11 向けのローカル音声入力 MVP。**Caps Lock を押している間だけマイクを録音し、離すと SenseVoiceSmall で日本語認識して入力**します。WPF の設定画面とタスクトレイに常駐します。

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
```

公式リリースを `models/` に取得し、固定 SHA256 を照合して展開します。アプリはモデルを自動ダウンロードしません。

使用モデル: `sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17`。必要ファイルは同一フォルダーの `model.int8.onnx` と `tokens.txt`。別のエクスポート形式の ONNX は使用できません。

モデルの利用条件は配布物の `LICENSE` と [SenseVoice](https://github.com/FunAudioLLM/SenseVoice)、[FunASR のライセンス案内](https://github.com/modelscope/FunASR#license) を確認してください。モデルは Git に含めません。

## Test

```powershell
dotnet test
```

通常は OS に触れないテストを実行し、実モデルとマイクの 2 件は明示的な opt-in がない場合スキップします。

```powershell
$env:SENSEVOICE_TEST_MODEL = (Resolve-Path '.\models\sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17').Path
$env:SENSEVOICE_TEST_MICROPHONE = '1'
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
3. **Caps Lock だけを押したまま**話す。Shift / Ctrl / Alt / Windows キーは押さない。
4. Caps Lock を離す。認識・入力が終わるまで対象ウィンドウと入力位置を維持する。
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
  "pushToTalkKey": "CapsLock",
  "backend": "Auto",
  "modelDirectory": "F:\\models\\sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17",
  "pasteRestoreDelayMs": 1500
}
```

設定変更は保存後の次回入力から有効。壊れた設定はユーザーへ通知し、元ファイルを自動上書きしません。ログは `diagnostic.log` と最大 1 世代の `.1`（各約 1 MB）。状態イベント、例外型・HResult・スタックを記録します。

## Privacy

音声認識はローカルで実行し、音声をクラウドへ送信しません。**認識結果や録音データは保存しません**。例外メッセージもログに含めません。PCM バッファは処理後に消去します（OS のページングやネイティブランタイムの内部メモリまで完全消去を保証するものではありません）。

貼り付け時に認識結果は一時的に Clipboard に入ります。Windows の履歴・クラウド同期除外形式を設定します。第三者のクリップボード監視ソフトや入力先アプリによる保存は制御できません。

## Known limitations

- Caps Lock は**完全に Push-to-Talk 専用**。短押しによる通常の Caps Lock 切り替えや 200 ms 判定はありません。日本語キーボードの「英数／Caps Lock」も物理 scan code 0x3A で判定し、Shift を併用しません（その英数キーの IME 切り替え動作も抑止）。変更用設定とキーサービスの境界はありますが、MVP は CapsLock のみ許可。
- 1 回 60 秒まで。常時録音・VAD は未実装。短い無音でもモデルが文字を生成する場合があります。
- 録音開始時のウィンドウを記録し、認識終了時にフォーカスが違えば入力を中止。自動で前面へ戻しません。同じウィンドウ内でのキャレット移動・タブ移動までは追跡しません。
- Clipboard の全形式を退避できない場合は貼り付け前に失敗します。特殊な遅延描画・独自形式の完全な復元は保証しません。ロック中の Clipboard はエラーとして通知。
- 貼り付け後は既定 1500 ms 待機して復元します。この間に別のコピー操作があれば復元しません。任意アプリの貼り付け完了 ACK は得られないため、特に遅いアプリでは race が残ります。待機時間を最大 10000 ms まで増やせます。
- 管理者権限のアプリ・UAC の安全なデスクトップへの入力は未対応。Ctrl+V 非対応アプリ、独自ショートカット、修飾キー押下中は入力できないことがあります。自動 Enter は送りません。Terminal の Ctrl+V 設定にも依存。
- ネイティブ推論を途中で強制停止する API はありません。終了要求では推論完了を待って結果を破棄し、入力せず安全に解放します。
- Windows x64 / CPU のみ。モデルフォルダーは ASCII 文字だけのパスを使用（上流の LPStr 設定 ABI の制約を明示的に検証）。
- マイク一覧は起動時取得。機器変更後は再起動。自動起動、TSF、IME 化、辞書、Toggle、LLM 補正、VAD、履歴機能は未実装。

次段階の候補は F13～F24 のキー設定と、遅延描画を用いた Clipboard 読み取り確認の改善です。

## References

- [.NET 10](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [sherpa-onnx C# API](https://k2-fsa.github.io/sherpa/onnx/csharp-api/index.html)
- [公式 SenseVoice モデルと取得方法](https://k2-fsa.github.io/sherpa/onnx/sense-voice/pretrained.html)
- [Windows Clipboard 形式と履歴・同期制御](https://learn.microsoft.com/ja-jp/windows/win32/dataxchg/clipboard-formats)
