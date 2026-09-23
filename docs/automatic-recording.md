> この文書は実装・検証時点の記録です。公開版は Whisper＋Silero（ONNX Runtime 直接実行）構成で、SenseVoice・sherpa-onnx の実行依存は削除しました。現在のセットアップと配布条件は [README](../README.md) と [第三者通知](../THIRD_PARTY_NOTICES.md) を参照してください。

# 自動録音（Silero VAD）

2026-09-24。ブランチ `feature/automatic-voice-recording`。

## 操作と実装

自動音声入力の切替機能を有効にして保存し、設定したトリガーでAUTO ONにする。テキスト入力欄のみ設定がONの場合、UI Automationで編集可能な入力先が見つかるまでREADY。見つかるとARMEDになりWASAPIマイクを開く。VADにはマイクの常時読み取りが必要だが、音声認識モデルは発話区間が確定したときだけ実行する。

`WASAPI Float32 -> mono -> WDL resampling 16kHz -> Silero VAD -> speech segment -> SenseVoice -> Unicode input`

Silero VADは既存sherpa-onnx 1.13.8のネイティブAPIを使用。ハンドルのNULL検査を行い、CPU1スレッド、閾値0.5、最小発話250ms、設定された無音時間（既定800ms）、最大発話30秒、nativeリング65秒。発話区間はnative VADの区間抽出を使用し、音声ファイルは保存しない。

- SpeechStartedでLISTENING。SegmentReadyでPROCESSING。認識完了後にARMEDへ戻る。
- 待機・録音はOFF、入力欄変更、PTT、キー設定時に停止。WASAPIを終了してからVADを破棄する。
- 停止済みセッションの遅延通知はセッション同一性で破棄する。未配信の区間bufferも消去する。
- PTTを優先。VADマイクを同期解放後にPTTマイクを開始する。モデル推論は既存lockで直列化し、キャンセルされた自動認識結果は挿入しない。
- 終了時は自動認識のキャンセル・完了を待ってからモデルを破棄する。
- 認識の前後にUIA入力欄identityとforeground HWNDを確認する。同じウィンドウの別入力欄へ移動した場合も中止する。
- VAD初期化・マイク異常ではAUTO OFFにして通知。モデル未配置も通知する。
- UI・トレイにAUTO状態を表示。通常操作の自動有効化は行わず、起動と設定保存時はOFF。

## モデル

`scripts/download-vad-model.ps1` で [sherpa公式配布](https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx) を取得する。643,854 bytes。SHA256:

`9E2449E1087496D8D4CABA907F23E0BD3F78D91FA552479BB9C23AC09CBB1FD6`

初回セットアップでのみネットワークを使う。モデルはGit対象外。設定 `autoVoiceInput.vad.modelPath` が空欄ならSenseVoiceモデルフォルダーの親の `silero_vad.onnx` を使う。ASCIIパス必須。配布・ライセンス情報は [sherpa Silero VAD](https://k2-fsa.github.io/sherpa/onnx/vad/silero-vad.html) と [Silero VAD](https://github.com/snakers4/silero-vad) を参照。

## 検証

- VADエンジン・resamplerのテストを先に作成し未実装でRed。実装後、無音で発火せず公式日本語wavから発話区間が取れることを実モデルでGreen確認。
- 初期化失敗と終了待ちのテストを先に追加しStopAsync未定義でRed→Green。
- OFF後の認識完了で設定操作が復帰しない不具合はassertion failureを確認し修正。
- WASAPI自動待機→停止→PTT録音→自動待機再開の実マイク検証を追加。これはアダプター実装後の統合確認として実施。
- 最終Releaseテストは93件（Core74 + Windows19）すべて成功。実SenseVoice、実Silero、実マイク2系統を含みskip0。通常実行は89件成功・4件skip。
- Release publish成功。Computer Useで `AUTO OFF · Silero VAD`、保存済みPTT・AUTOトリガー維持、新モデル設定欄を確認。
- ユーザーの発話による入力先アプリへの自動入力は確認待ち。モデル・録音テストの成功だけで最終入力成功とは扱わない。

## 制約

UIA入力欄判定は約100ms間隔。未対応アプリはARMEDにならない場合がある。認識中はマイクを停止するため連続会話の全取り込みは行わない。30秒で区切った場合も同様に認識中の音声は欠落する。環境音、テレビ、他人の声の誤検出はあり得る。話者分離・エコーキャンセル・無限連続録音は未実装。音声はメモリのみだが、OS・native内部bufferの完全消去までは保証しない。

最終UIA確認とキー入力の間に入力先が変化する短い競合窓は残る。ユーザーはPROCESSING中に入力先を動かさないこと。

語頭保護: VAD入力は512サンプル（32ms）単位に固定し、確定区間の前500msを追加して認識する。セッション内の音声だけを使い、前回区間と重複しない。停止時に先読みbufferを消去する。最新テストは全102件成功。
