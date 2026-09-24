# Third-party notices

本プロジェクトの自作コード・ドキュメントはルートの [MIT License](LICENSE) に従います。
第三者のライブラリ、モデル、設定・Tokenizer、GPU ランタイムにはそれぞれの条件が適用されます。
本体の MIT ライセンスは、これらの権利表示や条件を置き換えません。

## アプリに含まれるライブラリ

| 対象 | バージョン | 条件・本文 |
|---|---|---|
| NAudio、NAudio.Core / Asio / Midi / Wasapi / WinForms / WinMM | 2.2.1 | MIT、Copyright 2020 Mark Heath。[全文](licenses/NAudio-LICENSE.txt) |
| MathNet.Numerics | 5.0.0 | MIT。[全文](licenses/MathNet.Numerics-LICENSE.txt) |
| Microsoft.ML.OnnxRuntime.Gpu / Gpu.Windows / Managed | 1.28.0 | MIT、Microsoft Corporation。[全文](licenses/ONNXRuntime-LICENSE.txt)、[内包する第三者通知](licenses/ONNXRuntime-ThirdPartyNotices.txt) |
| System.Numerics.Tensors | 9.0.0 | MIT、.NET Foundation and Contributors。[全文](licenses/System.Numerics.Tensors-LICENSE.txt)、[第三者通知](licenses/System.Numerics.Tensors-ThirdPartyNotices.txt) |
| Microsoft.Windows.SDK.NET.Ref（Microsoft.Windows.SDK.NET.dll） | 10.0.19041.57 | Windows SDK の条件。[原文](licenses/Windows-SDK-LICENSE.rtf) |
| C#/WinRT（WinRT.Runtime.dll、上記 targeting pack に含まれる） | 2.2.0.48161 | MIT、Microsoft Corporation。[全文](licenses/CsWinRT-LICENSE.txt) |

NuGet の固定依存は各 `packages.lock.json` に記録しています。GPU メタパッケージは Linux パッケージも復元しますが、公開アプリの対象は Windows x64 です。
標準の ZIP 配布は framework-dependent です。.NET Desktop Runtime は利用者が別途インストールします。Store 用の自己完結型ビルドは .NET ランタイムを同梱し、ビルド時に使用した runtime pack の LICENSE と第三者通知を `licenses/dotnet/` に収録します。

## 初回取得するモデル

| 対象 | 出典・固定情報 | 条件 |
|---|---|---|
| Whisper large-v3-turbo ONNX FP16 と設定・Tokenizer | [onnx-community](https://huggingface.co/onnx-community/whisper-large-v3-turbo/tree/360ebcde2559d60bb474678be3c1de9ef347d01a)、revision `360ebcde2559d60bb474678be3c1de9ef347d01a`。元モデルは [OpenAI](https://huggingface.co/openai/whisper-large-v3-turbo) | 元モデル MIT。[全文](licenses/Whisper-LICENSE.txt)、[出典](licenses/Whisper-SOURCE.txt) |
| Silero VAD v4 | [Silero Team](https://github.com/snakers4/silero-vad/tree/v4.0)。k2-fsa 配布 ONNX を SHA256 で固定 | MIT。[全文](licenses/Silero-VAD-LICENSE.txt)、[取得元・ハッシュ](licenses/Silero-VAD-SOURCE.txt) |

モデルファイルは改変せず取得します。ONNX 変換版 Whisper のモデルカードは元モデルを参照しています。
アプリと手動取得スクリプトは公式モデルの保存先にライセンスと出典のコピーを残します。
任意の外部モデルを利用者が指定した場合、そのモデルの出典・条件を利用者側で保持してください。
モデルを再配布するときは、対応するライセンス・出典も一緒に配布してください。

SenseVoiceSmall のモデルは独自条件のため公開構成から除外し、取得・認識機能も削除しています。
sherpa-onnx の NuGet / ネイティブ DLL も配布しません。Silero は ONNX Runtime CPU で直接実行します。

## NVIDIA ランタイム（任意・別条件）

CUDA backend で使用するコンポーネントは `scripts/cuda-runtime-manifest.json` の URL と SHA256 で固定しています。

| コンポーネント | バージョン |
|---|---|
| CUDA Runtime (cuda_cudart) | 13.0.96 |
| cuBLAS | 13.1.0.3 |
| cuFFT | 12.0.0.61 |
| NVRTC | 13.0.88 |
| cuDNN (CUDA 13) | 9.14.0.64 |

これらは MIT ではありません。[CUDA EULA](https://docs.nvidia.com/cuda/eula/index.html) と [cuDNN EULA](https://docs.nvidia.com/deeplearning/cudnn/backend/latest/reference/eula.html)、および取得する各アーカイブ内の条件に従います。
固定アーカイブのライセンス原文は [licenses/nvidia](licenses/nvidia/) に保存しています。
アプリでの新規自動取得は条件への同意後に開始します。手動取得は `-AcceptNvidiaLicense` が必要です。
取得処理は使用する DLL と LICENSE / EULA / NOTICE / COPYING / COPYRIGHT 文書を保存します。

標準の `dotnet publish` は NVIDIA DLL やモデルを同梱しません。
別途 DLL を同梱する場合は、対象バージョンの再配布可能ファイル、用途・配布条件、第三者通知を確認し、同梱する NVIDIA ファイルに MIT が適用されると表示しないでください。
ドライバーは同梱・自動インストールしません。

## 開発・テスト用の依存と参照

- xUnit 2.9.3、xunit.runner.visualstudio 3.1.4、xunit.abstractions 2.0.3、xunit.analyzers 1.18.0: Apache-2.0。テストプロジェクトだけで使用し、アプリ配布には含めません。
- Microsoft.NET.Test.Sdk / Microsoft.TestPlatform / Microsoft.CodeCoverage 17.14.1、coverlet.collector 6.0.4、Newtonsoft.Json 13.0.3: MIT。テスト用のみ。
- Log-Mel 数値 fixture の生成元: Hugging Face Transformers v4.46.3 `audio_utils.py`（Apache-2.0）。[ライセンス全文](licenses/Transformers-LICENSE.txt)。生成手順はソースツリーの `tests/SenseVoiceInput.Windows.Tests/Fixtures/README.md` を参照。NumPy は再生成時のみ必要で配布に含めません。
- Silero v4 の ONNX 入出力仕様確認に sherpa-onnx v1.13.8 `silero-vad-model.cc`（Copyright 2023 Xiaomi Corporation）を参照しました。[Apache-2.0 本文](licenses/sherpa-onnx-reference-LICENSE.txt)。C# の状態管理・区間切り出しは本プロジェクトで実装しており、sherpa-onnx のバイナリは含みません。
- アプリ・トレイアイコンは本プロジェクト向けの生成画像です。第三者アイコンパッケージは使用していません。

上流から取得した文書の URL と SHA256 は [licenses/sources.json](licenses/sources.json) に記録しています。
配布時には `LICENSE`、この文書、`licenses/` を残してください。依存を更新する場合は本文と通知も更新してください。
