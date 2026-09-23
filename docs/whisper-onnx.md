> この文書は実装・検証時点の記録です。公開版は Whisper＋Silero（ONNX Runtime 直接実行）構成で、SenseVoice・sherpa-onnx の実行依存は削除しました。現在のセットアップと配布条件は [README](../README.md) と [第三者通知](../THIRD_PARTY_NOTICES.md) を参照してください。

# Whisper large-v3-turbo ONNX 実装報告（2026-09-24）

## 調査結果

変更前は `ISpeechRecognitionService` の単一実装が SenseVoice。WASAPIでfloat PCMを録音し、Coreでmono化、sherpaがASR向けresample/特徴抽出/CTCを行っていた。AUTOには独立したSilero VADと16kHz WDL resamplerがあり、PTT/AUTOは既に同じASR interfaceを利用していた。設定はJSON、DIはApplicationSessionのコンストラクター注入。既存ASR/VAD/トリガーの再設計は不要と判断した。

sherpa-onnx 1.13.8が同梱するORTはCPU版（この配布物のファイル版1.28.2）。CUDA設定は従来拒否されていた。WhisperにMicrosoft.ML.OnnxRuntime.Gpu **1.28.0**を追加し、Build/Publish後にそのnative DLLを明示配置した。ABI 1.28系列の同じruntimeでSenseVoiceとSileroの実モデルテストも成功した。

GPUはRTX4070TiSUPER 16GB、ドライバー616.64。CUDA Toolkitの標準インストールはなかった。NVIDIA公式redistributableのCUDA13.0.2系とcuDNN9.14.0.64 CUDA13版をローカル取得した。グローバルPATHやドライバーは変更していない。依存要件は [ORT公式CUDA資料](https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html) に基づく。

## モデルと実I/O

取得元: [onnx-community/whisper-large-v3-turbo](https://huggingface.co/onnx-community/whisper-large-v3-turbo/tree/360ebcde2559d60bb474678be3c1de9ef347d01a)。固定revision `360ebcde2559d60bb474678be3c1de9ef347d01a`。元モデルは[MIT](https://huggingface.co/openai/whisper-large-v3-turbo)、ONNX配布READMEは元モデルを参照する構成（独立したlicense記載なし）。以下の**実ファイルをORTでロードしメタデータを確認**した。ファイル名やcache shapeの推測による実装ではない。

| ファイル | バイト数 | SHA256 |
|---|---:|---|
| encoder_model_fp16.onnx | 1,274,342,603 | fdadc70836e6b028fd5e580417c312208dad073d2d01e509e2d127c1373399d8 |
| decoder_model_merged_fp16.onnx | 344,227,339 | fdf10afca73a0c7bf87286cfb96cf7028a9edbc9bb02512509a526f95b126c9d |

ほかにconfig.json / preprocessor_config.json / generation_config.json / tokenizer.json / tokenizer_config.json。smallファイルを含む全ハッシュは [manifest](../scripts/whisper-model-manifest.json)。モデルはGitへ追加していない。これらFP16ファイルは単独ファイルで、外部weight dataファイルは不要。

**重みはFP16、公開入出力はfloat32**。バッチは1固定で使用。

| モデル | 名前 | 型・shape |
|---|---|---|
| Encoder input | input_features | float32 [batch,128,3000] |
| Encoder output | last_hidden_state | float32 [batch,1500,1280] |
| Decoder input | input_ids | int64 [batch,sequence] |
| Decoder input | encoder_hidden_states | float32 [batch,encoder_sequence,1280] |
| Decoder input | past_key_values.{0..3}.{decoder,encoder}.{key,value} | float32 [batch,20,past_sequence,64] |
| Decoder input | use_cache_branch | bool [1] |
| Decoder output | logits | float32 [batch,sequence,51866] |
| Decoder output | present.{0..3}.{decoder,encoder}.{key,value} | float32 [batch,20,present_sequence,64] |

初回はcache長0の16個のtensor、`use_cache_branch=false`、4個の開始tokenを渡す。継続は`true`と次token1個のみ。self-attention K/Vは毎step更新、cross-attention K/Vは初回の出力を維持する。cached branchの空cross-attention出力で上書きしない。cacheは一発話のContextが所有しDisposeする。

現段階ではcacheをmanaged float32 bufferへコピーして保持する。GPU I/O bindingによるzero-copy最適化は未実施。全sequenceを毎token再計算する方式は使用していない。

## 責務分離と生成

- `ISpeechRecognitionService`: 既存のAudioData→構造化結果。PTT/AUTO共通。
- `ConfigurableRecognitionService`: 設定によるエンジン選択、直列化、設定変更時の再生成。
- `WhisperOnnxRecognitionService`: 遅延ロード、セッション再利用、30秒超の窓分割、終了、診断。
- `WhisperPipeline`: 前処理→encoder→decoder→tokenizerの連携。依存をfakeに置換可能。
- `WhisperAudioPreprocessor`: mono/16kHz、Log-Mel。
- `WhisperEncoder` / `WhisperDecoder`: ONNX I/O、per-utterance cache。
- `WhisperTokenGenerator` / `WhisperGenerationConfig`: greedy、開始token、抑制、EOS、上限、キャンセル。
- `WhisperTokenizer`: 認識token IDを文字へ復号。
- `WhisperModelSessionFactory`: モデル存在・config/I/O検証、CUDA/CPU session生成、Runのキャンセル。

`generation_config.json`からprefix `[50258,50266,50360,50364]`（start、日本語、transcribe、no-timestamps）、EOS50257、max_length448、suppress_tokens、begin_suppress_tokensを取得する。追加token上限は444。初回抑制はprefix直後だけ適用し、timestamp IDは抑制する。言語自動判定・翻訳・beam search・temperature fallbackは実装していない。将来prompt処理はgeneration層へ追加でき、controllerは変更不要。

Tokenizerはdecode-only。Microsoft.ML.TokenizersやHF bindingsを検討したが、本用途ではBPE encodingもmergeも不要なため、対象tokenizer.jsonのvocabを読み、[HF ByteLevel](https://github.com/huggingface/tokenizers/blob/main/tokenizers/src/pre_tokenizers/byte_level.rs)に対応するbyte写像を逆変換してUTF-8へ戻す小さなadapterを採用した。独自BPE学習・分割器は実装しない。特殊tokenを除外し、未知IDはTokenizerFailedとする。日本語の既知ID38088/27311/31348から「こんにちは日本語」を検証済み。

## 前処理の数値仕様

preprocessor_configを読み、対応モデル仕様との一致を検証する。NAudio WDLでresample、16kHz monoなら変換不要。MathNetの任意長FFTを利用し、400点のperiodic Hann、hop160、centered reflect pad200、power2、Slaney128 mel、面積正規化。音声は右側を0埋めして480000sample、STFT末尾1frameを除いて3000frameとする。log10(max(1e-10,mel))→全体最大値-8でclamp→(log+4)/4。

[Transformers v4.46.3 audio_utils](https://github.com/huggingface/transformers/blob/v4.46.3/src/transformers/audio_utils.py)による63点の参照fixtureと絶対誤差2e-5以内で一致。44.1kHz stereo / 48kHz mono / 16kHz mono、無音shapeも検証。Pythonは参照fixtureを生成した開発用スクリプトだけで、アプリ・C#テストの実行には不要。

AudioDataはmono契約。stereoはcaptureの既存PCM変換でmonoにする。PTTの60秒入力は30秒ずつ処理して連結するため後半を黙って捨てない。ただし窓境界の単語復元や重複contextは未対応。AUTO側のVAD/pre-rollはASRと独立したまま。

## CUDA、キャンセル、エラー

要求providerを登録し、登録失敗で停止する。CUDAのない新プロセスでcublasLt64_13.dll欠落を起こし、**CudaUnavailable**となりCPUへ進まないことを確認した。CPUを明示選択すると同じモデルで認識できた。

ORTプロファイルを採取し、encoderにCUDA演算イベント1742件、decoderにCUDA18814件を確認（2回のsample実行の合計）。decoderのCPU12338件は主にUnsqueeze/Concat/Gatherなどの形状関連処理。CPUへの全面fallbackではない。通常ログにproviderを記録し、必要時だけ`WHISPER_PROFILE_DIR`を使う。

処理はSemaphoreSlimで直列化。キャンセルを待機・前処理・token loop・ORT RunOptions.Terminateへ伝搬。キャンセルした結果は返さない。Disposeは処理終了後にsessionを解放する。モデルロード自体は途中強制中断せず、終了後に破棄する。

分類はModelNotFound / ModelLoadFailed / CudaUnavailable / AudioPreprocessingFailed / InferenceFailed / TokenizerFailed。CancelledはOperationCanceledExceptionとして伝搬し、共通サービスが診断イベントを残す。例外本文、発話本文、音声本体は通常ログへ出さない。

## 性能・精度の確認

RTX4070TiSUPER、Windows11、他アプリ稼働中。以下は小規模smokeの観測値で、保証値や十分な精度評価ではない。時間はモデルロードを除く認識時間、cold wall timeはロードを含む。

| 入力 | 音声長 | CUDA再利用時 | RTF |
|---|---:|---:|---:|
| 公式ja.wav | 7.20秒 | 0.76～1.30秒 | 0.105～0.181 |
| Haruka合成音声・挨拶 | 3.70秒 | 0.409秒 | 0.110 |
| Haruka合成音声・ヌルチェック | 3.56秒 | 0.561秒 | 0.158 |

モデルロードCUDA約2.6～3.2秒。公式ja.wavのcold wallは約4.7～5.6秒。CPU選択はロード5.13秒、再利用11.40秒（RTF1.58）。起動時warm-upは実装せず初回利用時にロードする。

VRAMは250ms間隔のnvidia-smi全体値で、背景アプリ込み4744～8160MiB、差約3416MiB。この差は参考値で、プロセス単体の厳密なpeakではない。

| 音声 | Whisper | SenseVoice |
|---|---|---|
| 公式日本語ja.wav（人の音声） | うちの中学は弁当制で、持っていけない場合は50円の学校販売のパンを買う。 | うちの中学は弁当制で持っていきない場合は、50円の学校販売のパンを買う。 |
| Haruka合成・挨拶 | こんにちは。今日はいい天気ですね。 | こんにちは、今日はいい 天気 ですね。 |
| Haruka合成・コード変更指示 | このメソッドにヌルチェックを追加してください。 | この メソッド に 塗る チェック を 追加 して ください。 |

後ろ2例はWindows既存のMicrosoft Haruka Desktopで生成した合成音声で、人の実発話ではない。元WAV/合成WAV・評価ログはartifacts/modelsに置きGitには含めない。複数の実発話データセット/CER評価は未実施。ユーザーのマイク発話からエディターへ入力する最終品質確認は今回未実施。

## テスト・UI・TDD

新規18件、総数120件（Core85 / Windows35）。全実モデル・VAD・実マイクを有効にしたReleaseテストで120成功、skip0、fail0。通常モードは114成功/6skip。Release build/publishも成功、警告0。

TDD: 前処理/decoder8件→pipeline/tokenizer2件→サービス/実モデル3件→設定3件を、未定義型・プロパティのコンパイルRedを実際に確認した後でGreenにした。native I/Oは事前調査した仕様をadapterへ実装して実モデルテストで確認。追加の参照mel/loop途中cancel2件は既存Green実装への回帰テストであり、意図的な失敗を先に作ったとは扱わない。Refactorで責務のinterface化、共通selector、DLL配置の統一を行い既存テストを回帰確認した。

WPF画面のWhisperOnnx/CUDA/モデルパス、保存成功、エラー表示なしをComputer Useで確認。現在のユーザーのPTT無効・AUTO Alt+V・マイク設定を保存したまま、Whisper版を起動した。AUTOは再起動仕様によりOFF。モデル・CUDA DLLもローカル配置済み。

## Git

作業前に `feature/whisper-onnx-cuda` を作成（基点02d901e）。コミットは `feat: add Whisper large-v3-turbo ONNX CUDA recognition`。最終hashとclean状態は完了応答で報告する。
