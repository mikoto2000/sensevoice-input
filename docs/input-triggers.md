# InputTrigger 設計・実装記録

2026-09-24。専用ブランチ `feature/configurable-input-triggers`。

## 調査と変更

旧実装は WH_KEYBOARD_LL 内で Caps Lock の物理scan codeを判定し、JIS固有の擬似up/downと解放欠落を50ms保留・リピート期限で補正していた。設定は `pushToTalkKey: CapsLock` 固定。WPF SettingsViewModel、トレイ、PushToTalkCoordinator が存在し、VADは存在しなかった。

今回、HoldGate、KeyBinding、RawKeyboardMonitor、診断引数を廃止。OS側ではコード変換とイベント配送のみを行う。旧仕様専用の16テストは削除し、Coordinatorとの結合テストと設定テストは汎用トリガー仕様へ更新した。元の音声録音・認識・入力テストは維持。

## 構成

`GlobalKeyboardService -> GlobalKeyEvent -> InputTriggerRouter -> InputTriggerMatcher -> Action`

- `InputTrigger`: SINGLE_KEY / KEY_COMBINATION / DOUBLE_TAP と構造化されたKeyCode配列。生のOS番号や表示文字列を保存しない。Displayは永続化対象外。
- `GlobalKeyEvent`: KeyCode / Down・Up / 単調増加ミリ秒。Windows型に依存しない。
- `InputTriggerMatcher`: リピート無視、組み合わせ成立・解除、2回押しを判定。組み合わせは追加modifierを拒否。無関係な通常キーはPTTを解除しない。2回押しは各tapと2回のup間隔が設定時間以内、別キー操作で取り消し。
- `InputTriggerRouter`: 機能別matcher、キー取得、設定更新、抑止判断。OSフックから録音などを呼ばず、アプリがActionをdispatcherへキューする。
- `PushToTalkCoordinator`: 既存のPushToTalkController相当。Idle/Recording/Recognizing/Injectingを維持。無効状態とREADY/LISTENING/PROCESSINGの表示は設定と組み合わせる。
- `AutoVoiceInputController`: 独立したDisabled/Ready/Armed/Listening/Processing。OFF、フォーカス喪失、PTT優先時にVAD停止・キャンセル。完了時は状態を再評価し音声バッファを消去する。重複の無音通知で処理を二重起動しない。
- `IVoiceActivityDetector`: Start/Stop/IsAvailable。将来アダプターが同じdispatcher上でSpeechDetected / SilenceDetectedAsyncへ通知する。実アダプターは未実装。Unavailable版はマイクを開かずAUTO READYに留まる。
- `TextInputFocusProbe`: ワーカースレッドでUI Automationのフォーカス・編集可否を確認。値/本文は読まない。パスワード、自アプリ、読み取り専用は除外。500ms間隔、同時問い合わせ1件。対象identity変更も旧セッションをキャンセル。UIA未対応アプリは判定不可。

## 設定とキー取得

PTTとAUTOは別のenabled/trigger設定。両方有効化できる。AUTOのenabledは切替機能の有効化で、録音ONの永続化ではない。起動/設定保存後はAUTO OFF。

同一キー・順序違いの同一組み合わせ・包含関係のある保持トリガーは競合として拒否。PTT Ctrl+GraveとAUTO Ctrl×2は許可。PTTのDOUBLE_TAPは禁止。Esc、Windowsキー、Alt+Tab、Ctrl+Alt+Deleteは禁止。2回押し間隔100～1000ms、無音判定200～5000ms。

「キーを設定」中は通常トリガーを停止し、キーをすべて離すと候補表示。DOUBLE_TAPの設定でもキー指定自体は1回。Esc、画面非表示でキャンセル。キャンセル中に押したキーの残りupも抑止。保存時に検証して再起動なしで更新する。保存はIdle・全キー解放時のみ。

壊れたトリガーは該当機能だけ無効化し通知。JSON全体が壊れた場合は両トリガーを無効化。元ファイルは自動変更しない。旧Caps Lock設定は明示再選択を促す。

## Windows固有事項と制約

WH_KEYBOARD_LLを継続。Ctrl/Altはextendedフラグ、Shiftはscan code（右0x36）、左右別VKも対応。注入イベント（LLKHF_INJECTED）は自己入力による再発火を避けるため無視する。F1～F24、左右modifier、英数字、主要編集・記号・テンキーを対応表で変換。UNKNOWNはトリガーに使えない。

Caps LockはVK_CAPITALとして通常キー扱い。JISのOEM IMEイベントをCapsへ偽装せず非対応とした。初期候補はF12であり固定キーではない。

単一キーと組み合わせを成立させた最後のkeydownおよび対応upを抑止。組み合わせ成立前のキーはOSに届くため、通常はmodifierから押すこと。最後にmodifierを離した時点まで入力処理を待つ保証はなく、修飾キーが残れば既存の入力アダプターが拒否する。2回押しは通常のショートカットと併用するためOSへ通過する（Capsの場合は元のtoggle動作も起こる）。他アプリとのグローバル競合を自動検出する機能はない。

## TDDと検証

1. TriggerTests 11件を追加、未定義型でRed→モデル/matcher/validationでGreen。
2. AutoAndCaptureTests 4件を追加、未定義VAD境界でRed→状態管理とcaptureでGreen。
3. TriggerSettingsTests 4件を追加、設定未定義でRed→隔離読み込み/競合/旧設定移行でGreen。
4. TriggerRouterTests 3件を追加、未定義routerでRed→配送/取得/抑止でGreen。
5. KeyboardMapTests 10件を追加、未定義mapでRed→Windows境界変換でGreen。
6. 未知キーの解放欠落と重複無音通知を追加、実際のassertion failureを確認して修正。Esc後の残りupも回帰確認。

追加35件、旧専用16件を廃止、差分+19件。全86件（Core71、Windows15）。Releaseで全成功・失敗0・skip0。公式日本語モデルと実WASAPIマイクも実行。Release buildは警告0・エラー0。

Computer Useで新設定画面、旧設定移行警告、PTT DISABLED / AUTO OFF・VAD未接続表示を確認。保存ボタン操作で新JSON形式への保存を確認。キー取得の物理入力と新トリガーからの実発話入力は未確認。注入キーを拒否する仕様なので、自動UIの合成キーを物理キー確認の代用にはしない。

## 将来拡張

MouseButton / Gamepad / LongPress等は別の入力イベントアダプターとInputTrigger種別を追加する。今回は対象外。VAD接続時はPTTとのマイク所有権、キャンセル完了待ち、対象フィールドの一致を実機で再検証すること。

参考: [KBDLLHOOKSTRUCT](https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-kbdllhookstruct)、[UI Automationの編集可能性](https://learn.microsoft.com/en-us/dotnet/framework/ui-automation/add-content-to-a-text-box-using-ui-automation)。
