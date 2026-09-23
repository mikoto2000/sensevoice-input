> この文書は実装・検証時点の記録です。公開版は Whisper＋Silero（ONNX Runtime 直接実行）構成で、SenseVoice・sherpa-onnx の実行依存は削除しました。現在のセットアップと配布条件は [README](../README.md) と [第三者通知](../THIRD_PARTY_NOTICES.md) を参照してください。

# TDD execution notes

2026-09-24、feature/mvp で実行。以下はこの実装時に実際に実行した Red / Green の順序。

| サイクル | Red | Green |
|---|---|---|
| 状態 | InputStateMachine / enum 未定義でコンパイル失敗 | 最小の遷移表で 2 件成功 |
| 録音開始 | Coordinator と依存境界未定義 | KeyDown と重複抑止、3 件成功 |
| キー解放 | KeyUp / StateChanged 未実装 | Stop→Recognize→Inject、空結果抑止、5 件成功 |
| 失敗・キャンセル | Failed 通知未実装 | Error→Idle、終了・再押下抑止、10 件成功 |
| PCM | PcmConverter 未定義 | Float32 stereo / PCM16 mono / 不完全 frame、13 件成功 |
| ASR 境界 | backend / result 変換未定義 | CPU 選択・未対応 provider 拒否・タグ除去、18 件成功 |
| 入力 | Clipboard boundary 未定義 | 復元・新コピー保護・focus/SendInput 失敗、22 件成功 |
| キー・設定 | KeyGate / SettingsStore 未定義 | repeat 抑止、JSON roundtrip / 破損 / 範囲検証、27 件成功 |
| ログ | DiagnosticLog 未定義 | 例外メッセージから発話を漏らさない、28 件成功 |
| 言語タグの修正 | `<|en|>` が `ja` になる assertion failure を再現 | 言語を unwrap して保持、37 件成功（追加回帰テストを含む） |
| JIS Caps Lock の修正 | ユーザー実機で単独押下が反応しない。キー照合の新仕様 8 件が未実装で失敗 | scan 0x3A により英数/Caps を同一視、仮想キーの異なる up も処理。Core 45 件成功 |
| JIS 長押し連打の修正 | ユーザー実機で約0.1秒周期の録音/停止を確認。対象キー限定の診断で31ms周期の up/down 対を観測。HoldGate の4件が未実装で失敗 | 50msの解放保留・再押下で取り消し。Coordinator と結合した長押し再現テストも成功。Core 50 件 |
| 最終解放の欠落 | 明示 up を入れない実機相当テストと遅い repeat 設定のテストを追加し、未実装で失敗 | OEM モードキーだけに初回待機＋repeat lease を導入。通常 Caps を勝手に停止しないテストを含め Core 54 件 |

Windows アダプターは境界の Fake でアプリケーション仕様を先にテストした後に実装。OS API 自体をモックの細部に合わせた単体テストにせず、実モデル・実マイクの opt-in integration test と UI smoke test に分けた。

Green 後の整理では状態表、PCM 変換、認識設定、Clipboard 手順を Core 側に分離した。完了前の回帰検証では pending Start 中の KeyUp、重複 KeyUp、Stop / injection 失敗後の再試行、認識失敗時のバッファ消去、Clipboard 設定直後の focus 変更とキャンセルを追加した。これらの追加回帰ケースは既存実装で最初から Green であり、新規実装後に Red を後付けしたものではない。

Clipboard の退避失敗後、Unicode fallback の2件を Red（専用例外未定義）→Green で実装。その後ユーザーの設計変更により直接入力を標準にした。直接入力時の Clipboard 非アクセス、焦点変更・キャンセル、保存後の方式変更、Clipboard 方式の明示維持、旧設定のデフォルト・不正値拒否のテストを追加し、未定義型による Red を確認後に実装。Core 62 件 Green。fallback は削除した。

詳細な実行結果は [smoke-test.md](smoke-test.md) を参照。

## 任意トリガーへの設計変更
旧キー固有テスト16件は仕様廃止により置換。新規35件のRed/Greenと回帰検証は [input-triggers.md](input-triggers.md) に記録。全86件成功。

## 自動録音
Silero VAD・終了待ち・OFF後の設定操作復帰のRed/Greenと実マイク検証を [automatic-recording.md](automatic-recording.md) に記録。7件追加、全93件成功。

## IME OFF（2026-09-24）

ブランチ `feature/ime-off-before-input`。PTT/AUTO共通のTextInjectionServiceで、foreground確認→IME OFF→キャンセル/foreground再確認→文字入力の順序に変更。空の結果と事前キャンセルではIMEに触れない。入力後にONへ戻さない。

Windows側はGetGUIThreadInfoで現在の子入力ウィンドウを取得し、ImmGetDefaultIMEWndへWM_IME_CONTROL / IMC_SETOPENSTATUS(FALSE)を送る。IMC_GETOPENSTATUSでOFFを確認する。各メッセージはSendMessageTimeoutで250ms制限。修飾キーが残っている場合は切替前に拒否。切替の前後でforegroundおよび子フォーカスを確認する。IMEウィンドウがない場合は何もせず入力を続けるため、IMM非対応/独自TSFアプリでのIME OFFは保証しない。

5件のテストを追加し、未実装で5件のassertion failureを確認した後に実装。両方式の呼び出し順序・IME失敗時の入力抑止・切替中の焦点変更を検証。全98件（Core79/Windows19）、実モデル・VAD・マイクを含むRelease実行は全成功、skip0。ビルド警告0/エラー0。IMEをONにした実入力先への動作は未確認。

参考: https://learn.microsoft.com/windows/win32/api/imm/nf-imm-immgetdefaultimewnd 、https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getguithreadinfo 、https://github.com/microsoft/PowerToys/issues/30976 。

## 語頭欠けの改善（2026-09-24）

`fix/auto-speech-preroll`。native VADに可変長のWASAPIパケットをそのまま渡すと、区間開始がパケットサイズに依存する問題を公式日本語wavで再現した。512サンプルと1600サンプルで切り出し音声が異なるテストのRedを確認後、常に512サンプル（32ms）単位でnativeへ渡すことでGreenにした。

加えてVAD区間より前の500msを音声認識へ含める。65秒分の上限付きメモリリングで元音声を保持し、区間の絶対サンプル番号で前置する。以前の発話末尾より前へ戻らないため前の言葉を重複させない。セッション開始以前へ戻らず、OFF・停止時にメモリを消去する。3件のCoreテストを未定義型のRed→Greenで追加（弱い語頭、連続区間、リング折り返し/開始直後）。

入力欄フォーカス確認は500msから100msへ短縮。同時問い合わせは引き続き1件まで。マイク起動前や認識処理中の音声は復元できない。AUTO ARMED後の語頭欠けを抑える変更であり、無停止の連続認識ではない。

全102件（Core82/Windows20）成功、skip0。実SenseVoice・Silero・マイクを含む。Release publish成功。ユーザーの実発話での改善度は未確認。

## Whisper ONNX（2026-09-24）

専用ブランチfeature/whisper-onnx-cuda。ASR境界は維持し、モデルI/Oを実ロードで調査後にnative adapterを追加。前処理/decoder、pipeline/tokenizer、service、settingsの順にテストを先行し、未定義型/プロパティで実際にコンパイルRedを確認した後Greenへ。追加のmel参照fixtureと途中cancelは既存実装の回帰テスト。新規18件、全120件成功（実Whisper CUDA/SenseVoice/Silero/マイクを含みskip0）。C#ビルド/Publish成功。数値仕様、テスト範囲、比較結果、制約は [whisper-onnx.md](whisper-onnx.md)。
