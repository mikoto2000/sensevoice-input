> この文書は実装・検証時点の記録です。公開版は Whisper＋Silero（ONNX Runtime 直接実行）構成で、SenseVoice・sherpa-onnx の実行依存は削除しました。現在のセットアップと配布条件は [README](../README.md) と [第三者通知](../THIRD_PARTY_NOTICES.md) を参照してください。

# Architecture

```text
WPF / tray / SettingsViewModel
  ApplicationSession: composition root
    GlobalKeyboardService -> InputTriggerRouter
      PTT -> PushToTalkCoordinator -> AudioCaptureService (WASAPI)
      AUTO -> AutoVoiceInputController <- WasapiVadService (Silero, CPU)
    Both -> ISpeechRecognitionService
      ConfigurableRecognitionService (one request at a time)
        WhisperOnnxRecognitionService
          WhisperAudioPreprocessor -> WhisperEncoder -> WhisperDecoder
          WhisperTokenGenerator -> WhisperTokenizer -> SpeechRecognitionResult
        SenseVoiceRecognitionService (sherpa C API, CPU)
    -> ITextInjectionService (Unicode / explicit Clipboard)
```

ControllerにはWhisper固有処理を置かない。CoreはONNX Runtime、WPF、NAudio、sherpaに依存しない。composition rootで認識サービスを生成し、PTT/AUTOの双方へ同じインスタンスを注入する。VADは音声区間を出す責務だけを持ち、ASRモデル・generation configを知らない。

`ConfigurableRecognitionService`はエンジン/絶対モデルパス/providerの組を比較し、変更時だけ旧エンジンをDisposeする。SemaphoreSlimで切替・認識・破棄を直列化。Whisper内部にもゲートがあり、直接利用でも同時認識は1件。モデル・Tokenizer・設定は遅延ロードして保持する。

前処理は入力monoを16kHzへ変換、30秒窓のLog-Melを生成。NAudio WDLとMathNet FFTを利用。Encoder/Decoderは実モデルで確認したfloat32 I/Oを使う（重みはFP16）。Decoder Contextが一発話のKVキャッシュを所有し、成功/失敗/cancelのいずれもDisposeする。計算の数値仕様、cacheの初回/継続分岐、モデルの固定ハッシュは [whisper-onnx.md](whisper-onnx.md)。

PTTは `Idle -> Recording -> Recognizing -> Injecting -> Idle`、空結果ならInjectingを省略。AUTOは入力欄のUIA identityとforegroundを認識前後で確認。OFF/入力先変更/PTT開始でキャンセルし、不適切な入力先への送信を防ぐ。認識中はAUTOのマイクを止めるため連続ストリーミングではない。500ms pre-rollと512sample固定VADフレームは維持。詳細は [automatic-recording.md](automatic-recording.md)。

終了時は新規トリガーを止め、AUTO/PTTのキャンセル・終了を待ち、認識セッション・マイク・trayを解放する。WhisperのONNX RunはRunOptions.Terminateへキャンセルを伝搬する。モデルロードは中断できず、ロード終了後に破棄。SenseVoice Decodeも完了後に結果を破棄する。

Unicode直接入力が標準でClipboardを読まない。入力直前のIME ON/OFFを保存し、IME OFFを要求してからforegroundを再確認しUTF-16のSendInputを行う。両方式とも入力イベントの処理待ち後、例外・キャンセル時も保存したIME状態の復元を試みる。元の子入力ウィンドウとIMEが有効で同じ欄にフォーカスがある場合だけ復元し、別の欄には適用しない。貼り付け方式は明示選択時だけClipboardを退避し、履歴/同期除外形式を付加して貼り付ける。Clipboardの復元はsequence numberが変わらない場合だけ行い、途中の新しいコピーを上書きしない。入力後の復元待ちはキャンセルしない。部分送信を自動再送しない。

通常ログは状態イベント、ASR数値メタデータ、例外型・分類・HResult・スタックのみ。ASR本文は渡さない。録音・特徴量・cacheの管理バッファは処理後に消去し、native出力はusingで破棄する。OS/ランタイム内の全コピー消去までは保証しない。認識本文を明示的に確認する場合は、実モデルの統合テストの詳細出力を使用する（手順は [DEVELOP.md](../DEVELOP.md)）。
