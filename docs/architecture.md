# Architecture

```text
WPF / NotifyIcon / SettingsViewModel
        | ApplicationSession (composition root)
WH_KEYBOARD_LL -> dispatcher -> PushToTalkCoordinator
                               | IAudioCaptureService -> WASAPI (NAudio)
                               | ISpeechRecognitionService -> sherpa-onnx -> ONNX Runtime CPU
                               | IForegroundWindowService -> GetForegroundWindow
                               | ITextInjectionService -> TextInjectionService (Unicode / Clipboard)
                                                           | IClipboardDesktop -> WPF Clipboard / SendInput
```

状態は `Idle -> Recording -> Recognizing -> Injecting -> Idle`。空の結果では Injecting を省略。処理失敗は Error を通知し、録音リソースを回収して Idle に戻す。不正な遷移は例外。

Coordinator は SemaphoreSlim で処理を直列化する。録音開始待ちの KeyUp は開始完了を待つ。録音中の追加 KeyDown、認識・入力中の KeyDown / KeyUp は無視する。キーフックの callback はイベントを dispatcher にキューし、Windows に直ちに返す。押下リピートを HoldGate で除外し、Caps Lock の down/up を抑止する。

実機の JIS 英数キーでは約31msごとのリピートに `up(VK_F0) -> down(VK_F0)` が約0.3ms間隔で届いた。単純な down 重複除去では録音を毎回止めてしまう。`PushToTalkHoldGate` は解放を50ms保留し、その間の down で保留を取り消す。最後の up だけが停止となる。単調増加時計と同一 dispatcher 上のタイマーを使い、タイマー遅延時は次の入力処理前にも期限を確認する。タイマーは解放保留中だけ動作。これは50ms未満の実際の離し直しも同一発話にまとめるトレードオフがある。

その後の実機確認では最終 up が来ないことも判明した。Raw Input を抑止なしで比較しても最終 break は観測できず、Raw Input への単純な置換では解決しなかった。通常モードは引き続き低レベルフックでキーを抑止し、OEM IME キーだけにリピート更新による停止期限を設ける。`SPI_GETKEYBOARDDELAY` / `SPI_GETKEYBOARDSPEED` を起動時に読み、初回は repeat delay + grace、リピート開始後は `max(100ms, 2 * repeat interval + 50ms)` だけ待つ。通常 Caps はこの期限を使わず、明示的な up を待つ。物理的な解放時刻の厳密な取得ではなく、欠落する通知に対するフォールバックである。

Caps Lock は非 extended の物理 scan code `0x3A` で照合する。日本語 JIS キーボードの英数キーでは `VK_CAPITAL (0x14)` ではなく `VK_OEM_ATTN (0xF0)` が来る場合があり、仮想キーだけの判定では押下を取り逃す。物理コードを持たない仮想 Caps イベント (`scan=0, VK=0x14`) も受け付ける。別の設定キーへの拡張では仮想キー照合を使う。[Microsoft PowerToys の日本語 IME / Caps の技術ノート](https://github.com/microsoft/PowerToys/blob/main/doc/devdocs/modules/keyboardmanager/keyboardmanager.md)。

WASAPI は既定の Communications 入力または明示したデバイスで Float32 を取得し、チャンネルを平均して mono 化。元のサンプルレートを recognizer に渡し、sherpa-onnx がモデル向け 16 kHz へのリサンプリング、fbank 等の前処理、SenseVoice 推論、CTC decode を行う。最大 60 秒で録音を止める。VAD を追加する場合は capture と recognition の間に segmenter を挿入できる。

認識モデルは初回発話でロードし再利用。推論は Task.Run 内で直列実行。上流 C# 設定構造体を利用し、C API で生成された handle の NULL を必ず確認する（上流ラッパーの null handle 利用を避ける）。stream / JSON / recognizer の所有権を finally / Dispose で解放。モデルパス変更で再初期化する。`RecognitionBackend` の解決は UI と分離し、MVP の非 CPU 値は拒否。

入力先は録音開始時の HWND。Snapshot 前、Clipboard 設定後、SendInput 直前に foreground を照合する。焦点を強制移動しない。Clipboard を materialize して退避し、認識結果・履歴同期除外形式を設定。Ctrl+V の down/up を一括送信し、短い待機後、sequence number が自身の書き込みから変わっていない場合だけ復元する。この待機はキャンセルせず終了時も完遂する。任意アプリとの厳密な paste 完了合意はないため既知制約として明示する。

終了はキーフックを解除し、新規処理を禁止、lifetime token をキャンセル。録音を停止し、実行中推論は完了待ち、結果は破棄。進行中の Clipboard 復元も待ち、ネイティブリソースとトレイを解放する。CPU Decode 自体は中断不可。OS による強制終了時の復元は保証できない。

標準は Unicode 直接入力。Clipboard は読み書きせず、キャンセルと foreground を確認して UTF-16 コード単位の KEYEVENTF_UNICODE down/up を一括送信する。部分成功は再試行しない。上記の Clipboard 手順はユーザーが明示選択した場合のみ使用する。方式は全アプリ共通の設定で、保存後の次回入力から有効。方式の自動判定・自動フォールバックは行わない。

TSF を将来追加する際は `ITextInjectionService` の実装を差し替える。Core は WPF、Win32、NAudio、sherpa-onnx に依存しない。設定画面には主要な状態遷移やネイティブ呼び出しを置かない。

診断とユーザー通知は分離。ログは固定イベント名と例外型・HResult・スタックのみ。録音/認識内容をログに渡す API を設けない。ログ書き込み失敗は LastWriteError に保持し画面で報告する。
