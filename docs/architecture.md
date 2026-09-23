# Architecture

```text
WPF / NotifyIcon / SettingsViewModel
        | ApplicationSession (composition root)
WH_KEYBOARD_LL -> dispatcher -> PushToTalkCoordinator
                               | IAudioCaptureService -> WASAPI (NAudio)
                               | ISpeechRecognitionService -> sherpa-onnx -> ONNX Runtime CPU
                               | IForegroundWindowService -> GetForegroundWindow
                               | ITextInjectionService -> ClipboardTextInjectionService
                                                           | IClipboardDesktop -> WPF Clipboard / SendInput
```

状態は `Idle -> Recording -> Recognizing -> Injecting -> Idle`。空の結果では Injecting を省略。処理失敗は Error を通知し、録音リソースを回収して Idle に戻す。不正な遷移は例外。

Coordinator は SemaphoreSlim で処理を直列化する。録音開始待ちの KeyUp は開始完了を待つ。録音中の追加 KeyDown、認識・入力中の KeyDown / KeyUp は無視する。キーフックの callback はイベントを dispatcher にキューし、Windows に直ちに返す。押下リピートを KeyGate で除外し、Caps Lock の down/up を抑止する。

Caps Lock は非 extended の物理 scan code `0x3A` で照合する。日本語 JIS キーボードの英数キーでは `VK_CAPITAL (0x14)` ではなく `VK_OEM_ATTN (0xF0)` が来る場合があり、仮想キーだけの判定では押下を取り逃す。物理コードを持たない仮想 Caps イベント (`scan=0, VK=0x14`) も受け付ける。別の設定キーへの拡張では仮想キー照合を使う。[Microsoft PowerToys の日本語 IME / Caps の技術ノート](https://github.com/microsoft/PowerToys/blob/main/doc/devdocs/modules/keyboardmanager/keyboardmanager.md)。

WASAPI は既定の Communications 入力または明示したデバイスで Float32 を取得し、チャンネルを平均して mono 化。元のサンプルレートを recognizer に渡し、sherpa-onnx がモデル向け 16 kHz へのリサンプリング、fbank 等の前処理、SenseVoice 推論、CTC decode を行う。最大 60 秒で録音を止める。VAD を追加する場合は capture と recognition の間に segmenter を挿入できる。

認識モデルは初回発話でロードし再利用。推論は Task.Run 内で直列実行。上流 C# 設定構造体を利用し、C API で生成された handle の NULL を必ず確認する（上流ラッパーの null handle 利用を避ける）。stream / JSON / recognizer の所有権を finally / Dispose で解放。モデルパス変更で再初期化する。`RecognitionBackend` の解決は UI と分離し、MVP の非 CPU 値は拒否。

入力先は録音開始時の HWND。Snapshot 前、Clipboard 設定後、SendInput 直前に foreground を照合する。焦点を強制移動しない。Clipboard を materialize して退避し、認識結果・履歴同期除外形式を設定。Ctrl+V の down/up を一括送信し、短い待機後、sequence number が自身の書き込みから変わっていない場合だけ復元する。この待機はキャンセルせず終了時も完遂する。任意アプリとの厳密な paste 完了合意はないため既知制約として明示する。

終了はキーフックを解除し、新規処理を禁止、lifetime token をキャンセル。録音を停止し、実行中推論は完了待ち、結果は破棄。進行中の Clipboard 復元も待ち、ネイティブリソースとトレイを解放する。CPU Decode 自体は中断不可。OS による強制終了時の復元は保証できない。

TSF を将来追加する際は `ITextInjectionService` の実装を差し替える。Core は WPF、Win32、NAudio、sherpa-onnx に依存しない。設定画面には主要な状態遷移やネイティブ呼び出しを置かない。

診断とユーザー通知は分離。ログは固定イベント名と例外型・HResult・スタックのみ。録音/認識内容をログに渡す API を設けない。ログ書き込み失敗は LastWriteError に保持し画面で報告する。
