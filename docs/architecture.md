更新: VAD本体と自動録音は [automatic-recording.md](automatic-recording.md) で実装済み。以下のVAD未実装に関する記載はトリガー設計時点の履歴です。

# Architecture

```text
WPF / NotifyIcon / SettingsViewModel
        | ApplicationSession (composition root)
WH_KEYBOARD_LL -> GlobalKeyEvent -> InputTriggerRouter -> dispatcher -> PushToTalkCoordinator
                               | IAudioCaptureService -> WASAPI (NAudio)
                               | ISpeechRecognitionService -> sherpa-onnx -> ONNX Runtime CPU
                               | IForegroundWindowService -> GetForegroundWindow
                               | ITextInjectionService -> TextInjectionService (Unicode / Clipboard)
                                                           | IClipboardDesktop -> WPF Clipboard / SendInput
```

状態は `Idle -> Recording -> Recognizing -> Injecting -> Idle`。空の結果では Injecting を省略。処理失敗は Error を通知し、録音リソースを回収して Idle に戻す。不正な遷移は例外。

Coordinator は SemaphoreSlim で処理を直列化する。録音・認識中の重複操作を無視し、終了時は入力をキャンセルする。トリガーはキー固有処理から分離した。独立したAutoVoiceInputControllerとVAD接続境界を追加。詳細は [input-triggers.md](input-triggers.md)。

認識モデルは初回発話でロードし再利用。推論は Task.Run 内で直列実行。上流 C# 設定構造体を利用し、C API で生成された handle の NULL を必ず確認する（上流ラッパーの null handle 利用を避ける）。stream / JSON / recognizer の所有権を finally / Dispose で解放。モデルパス変更で再初期化する。`RecognitionBackend` の解決は UI と分離し、MVP の非 CPU 値は拒否。

入力先は録音開始時の HWND。Snapshot 前、Clipboard 設定後、SendInput 直前に foreground を照合する。焦点を強制移動しない。Clipboard を materialize して退避し、認識結果・履歴同期除外形式を設定。Ctrl+V の down/up を一括送信し、短い待機後、sequence number が自身の書き込みから変わっていない場合だけ復元する。この待機はキャンセルせず終了時も完遂する。任意アプリとの厳密な paste 完了合意はないため既知制約として明示する。

終了はキーフックを解除し、新規処理を禁止、lifetime token をキャンセル。録音を停止し、実行中推論は完了待ち、結果は破棄。進行中の Clipboard 復元も待ち、ネイティブリソースとトレイを解放する。CPU Decode 自体は中断不可。OS による強制終了時の復元は保証できない。

標準は Unicode 直接入力。Clipboard は読み書きせず、キャンセルと foreground を確認して UTF-16 コード単位の KEYEVENTF_UNICODE down/up を一括送信する。部分成功は再試行しない。上記の Clipboard 手順はユーザーが明示選択した場合のみ使用する。方式は全アプリ共通の設定で、保存後の次回入力から有効。方式の自動判定・自動フォールバックは行わない。

TSF を将来追加する際は `ITextInjectionService` の実装を差し替える。Core は WPF、Win32、NAudio、sherpa-onnx に依存しない。設定画面には主要な状態遷移やネイティブ呼び出しを置かない。

診断とユーザー通知は分離。ログは固定イベント名と例外型・HResult・スタックのみ。録音/認識内容をログに渡す API を設けない。ログ書き込み失敗は LastWriteError に保持し画面で報告する。
