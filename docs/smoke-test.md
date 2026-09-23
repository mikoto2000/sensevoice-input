最新の自動録音版は全93件成功・skip0。実Silero VADと自動マイク→PTT切替を確認。ユーザー発話での最終入力は確認待ち。[詳細](automatic-recording.md)。

# Verification record

## 最新: 任意トリガー版

全86件（Core71 + Windows15）成功、skip0。実モデル・実マイクを含むReleaseテストとpublish成功。新設定UIの移行警告・保存をComputer Useで確認。物理キー取得から実発話入力は未確認。VAD本体は今回の対象外で自動録音は動作しない。詳細は [input-triggers.md](input-triggers.md)。

以下は旧版の検証履歴（Caps固有仕様は廃止）。

実施日: 2026-09-24 (JST)。Windows 11 x64 build 26200、.NET SDK 10.0.401 / runtime 10.0.12。

## Automated verification

- `dotnet restore --locked-mode`: 成功。
- `dotnet build -c Release --no-restore`: 成功、警告 0 / エラー 0。
- Core: **62 件成功**。状態、重複・競合、エラー復帰、終了、PCM、認識結果、Clipboard 手順、直接入力時の Clipboard 非アクセス・方式切替、旧設定移行、設定、ログ、JIS/US の Caps Lock 判定、実機で観測した長押しリピート列、最終 up 欠落と遅いリピート設定。
- Windows 境界: **3 件成功**。モデル欠落、事前キャンセル、短い音声。
- 実モデル: **1 件成功**。公式 `test_wavs/ja.wav` を CPU で認識、日本語文字と `ja` メタデータ、タグ除去を検証。Fake recognizer は使用していない。
- 実マイク: **1 件成功**。WASAPI で 500 ms の PCM 取得、停止、100 ms の再録音・停止。有限値・非空 buffer を確認し消去。発話の正確さを検証するテストではない。

合計 **67 ケース**。直接入力の標準化後の Release 実行は65件成功・2件skip・失敗0。実モデル・実マイクは各1件の opt-in で以前の別実行で成功済み。Release publish も成功。

```powershell
$env:SENSEVOICE_TEST_MODEL = (Resolve-Path '.\models\sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17').Path
$env:SENSEVOICE_TEST_MICROPHONE = '1'
dotnet test -c Release
```

## Desktop observations

| 項目 | 結果と確認範囲 |
|---|---|
| WPF 起動 | 成功。Computer Use で実ウィンドウと Ready 表示を確認 |
| 設定保存 | 成功。保存ボタンを操作し、画面の完了表示と JSON ファイルを確認 |
| 実録音 | 上記 WASAPI integration test 成功 |
| 日本語推論 | 上記実モデル integration test 成功 |
| キーから入力の処理 | ユーザーの試行中、ログに複数回の AudioCaptureStarted→Stopped→Recognition→TextInjectionCompleted を確認。ただしログだけで入力先と内容の正しさを判断しない |
| 修飾キー中の貼り付け拒否 | 実ログで Win32.Paste からのエラー通知を確認 |
| Notepad の目視結果 | 初版のキー照合、長押し連打、最終解放欠落をユーザー実機で検出。repeat lease 導入後、ユーザーは解放後の Clipboard 退避エラーを報告。直接入力を標準化した版の入力結果は未確認 |
| Close-to-tray / Exit | 実装済み、実 UI での最終確認は未完了 |
| VS Code / Terminal / browser textarea | 未実施 |

**単体テスト成功をもって、すべての Windows アプリで正常動作したとは扱わない。** Notepad の確定した目視結果・Clipboard 復元を確認するまで、全受け入れ条件の実機検証は未完了。

## Manual acceptance checklist

1. 既存の Clipboard 内容をコピーして控える（機密ではないテスト文字列を使用）。
2. 空の Notepad にフォーカスして Caps Lock **単独**を長押し。「このメソッドに null チェックを追加してください」と話して離す。
3. 認識結果が入力され、Ready に戻ることを確認。
4. 新しい行に手動 Ctrl+V。直接入力では開始前の Clipboard が変更されていないことを確認。貼り付け方式を別途選択した場合は2秒以上待ってから復元を確認。
5. 入力途中のキーリピートが二重録音を開始しないことを確認。
6. 認識中に他アプリへ移動し、入力中止の通知と誤入力がないことを確認。
7. 設定を閉じてもプロセスが残り、トレイから再表示できることを確認。
8. 録音中・認識中それぞれで Exit。マイクとキーフックが解放され、以後の貼り付けが起こらないことを確認。

意図的な focus 変更・Clipboard ロック・権限差・アプリ固有形式の追加 smoke は、独立したテスト文書で実施する。
