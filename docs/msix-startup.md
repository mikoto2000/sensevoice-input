# MSIX 自動起動

## 実装

パッケージ ID の有無を `GetCurrentPackageFullName` で判定します。MSIX 内では `PackagedLoginStartupService` が Windows の StartupTask を使い、ZIP からの通常起動では `LoginStartupService` が従来の HKCU Run 登録を使います。パッケージの状態取得に失敗した場合に Run キーへフォールバックする処理はありません。

manifest の `SenseVoiceInputStartup` は既定で無効です。設定画面の保存操作で有効化を要求します。自動起動時は `--startup` を渡すため、既に起動中なら重複起動の案内を出さず終了します。準備済みの場合は通知領域に常駐します。

自動起動の状態は settings.json に重複保存せず、Windows を正として読み取ります。

| Windows の状態 | チェック | アプリからの変更 |
|---|---|---|
| Disabled | OFF | 可能 |
| Enabled | ON | 可能 |
| DisabledByUser | OFF | 不可。Windows 設定から再有効化する案内 |
| DisabledByPolicy | OFF | 不可。管理者・環境の制限を案内 |
| EnabledByPolicy | ON | 不可。管理者ポリシーを案内 |

画面をアクティブにしたときに状態を再取得します。Windows の状態が同じなら未保存のチェック操作は保持します。保存失敗・拒否の後は実際の状態へ表示を戻します。自動起動の状態取得に失敗した場合は変更を止め、画面を開き直す案内を出します。

保存は UI スレッドから非同期で行い、処理中は重複保存・音声入力・モデル準備を止めます。設定ファイルへの書き込みが失敗した場合は、自分が変更した通常状態を復元します。復元時に Windows が利用者・ポリシーによって状態を変えていた場合は上書きしません。復元にも失敗した場合は両方のエラーを報告します。

WinRT API のため Windows/App/Windows.Tests の TFM を `net10.0-windows10.0.19041.0` に変更しています。製品の対応環境・MSIX の MinVersion は引き続き Windows 11 です。SDK targeting pack は `global.json` で固定した .NET SDK が選択します。更新時は WinRT の配布ライセンスも確認してください。

## 検証記録（2026-09-24）

- Release ビルド: 警告0・エラー0。
- テスト: Core 104件、Windows 52件成功。モデル・マイク・GPU に依存する6件はスキップ。
- 自動起動の新規19件は、状態表示、Windows の拒否、ポリシー、保存失敗時の復元、復元失敗、ZIP 版の選択、manifest の ID・引数・既定無効を検証。
- 検証用 ID による MSIX 作成: MakeAppx 成功。最終出力は `artifacts/msix-startup-final/`。生成パッケージ内の TaskId、既定無効、`--startup`、WinRT DLL とライセンスの収録も確認。
- 上記は StartupTask の状態制御をテスト用の代替実装で確認したものです。実際の MSIX インストールと再ログインは未実施です。

## インストール後の確認手順

1. ZIP 版は終了し、自動起動も無効にする。正式 ID、または隔離したテスト用 ID の MSIX をインストールする。
2. 最初に手動で起動する。初期の自動起動が OFF であることを確認する。
3. モデル準備の完了または中止後、チェックを入れて保存する。Windows の設定 → アプリ → スタートアップで有効になっていることを確認する。
4. テスト用 Windows アカウントでサインアウト・サインインし、通知領域へ1プロセスだけ起動することを確認する。
5. アプリでチェックを外して保存し、次回ログインで起動しないことを確認する。
6. 再有効化した後、Windows 設定側から無効にする。アプリ設定を再表示し、OFF と無効化理由が出ることを確認する。別の設定の保存で勝手に再有効化されないことを確認する。
7. Windows 設定で再有効化し、アプリへ戻って ON に反映されることを確認する。
8. ポリシーを管理するテスト環境では管理者による有効固定・無効固定も確認する。
9. 同じ Identity/TaskId でバージョンを上げたパッケージへ更新し、状態と自動起動を確認する。削除後に自動起動項目が残らないことも確認する。

本番利用中の PC を検証のためにサインアウトしないでください。ZIP 版の既存 Run 登録は別の配布物に属するため、MSIX から自動削除しません。

参考: [StartupTask](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask)、[desktop:Extension](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop-extension)
