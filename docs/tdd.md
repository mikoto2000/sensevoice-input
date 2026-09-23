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

Windows アダプターは境界の Fake でアプリケーション仕様を先にテストした後に実装。OS API 自体をモックの細部に合わせた単体テストにせず、実モデル・実マイクの opt-in integration test と UI smoke test に分けた。

Green 後の整理では状態表、PCM 変換、認識設定、Clipboard 手順を Core 側に分離した。完了前の回帰検証では pending Start 中の KeyUp、重複 KeyUp、Stop / injection 失敗後の再試行、認識失敗時のバッファ消去、Clipboard 設定直後の focus 変更とキャンセルを追加した。これらの追加回帰ケースは既存実装で最初から Green であり、新規実装後に Red を後付けしたものではない。

詳細な実行結果は [smoke-test.md](smoke-test.md) を参照。
