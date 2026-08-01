# Assets/Scripts — モジュール構成と分担

3人 + AIエージェントで並行開発するための決まり。**自分の担当フォルダの外を編集しない。**

## モジュールと担当

| フォルダ | 担当 | 中身 | 依存できる先 |
|---|---|---|---|
| `Core/` | 共有 | `SwordData` `Sqlite` `HitStop` `WavyText` | なし |
| `Data/` | データ・進行 | `SwordRepository` | Core |
| `Battle/` | 戦闘 | `Fighter` `SwordBuilder` `BattleCamera` `BattleEffects` `BattleStageInstaller` | Core |
| `Flow/` | データ・進行 | `DuelManager` `FlowInstaller` | Core, Data, Battle |
| `UI/` | UI・演出 | `BattleHud` `HpBarUI` `SwordSelectUI` `ForgeUI` `UiInstaller` `DebugResultOverlay` | Core, Battle, Flow |
| `App/` | 共有 | `GameBootstrap` | 全部 |

依存は**下から上への一方通行**。`.asmdef` で強制してあるので、逆流させるとコンパイルエラーになる。
エラーになったら「参照を足す」のではなく、**設計がおかしいと考えること**。

## 触ってよい場所

- 自分のモジュールのフォルダの中は自由
- `Core/` と `App/` は共有。変更するときは他の2人に一声かける
- 他モジュールのフォルダは**読むのは自由、編集は禁止**

## 競合を避けるための決まり

**1. シーンに物を置かない。**
`SampleScene.unity` には `GameBootstrap` を貼った空オブジェクトが1つあるだけ。
地面もカメラもUIも実行時にコードで組み立てている。
`.unity` はテキストだがマージが極めて困難なので、この方針は崩さない。

**2. 組み立ては各モジュールの Installer に書く。**
画面を足す → `UI/UiInstaller.cs`
場を変える → `Battle/BattleStageInstaller.cs`
進行の初期値 → `Flow/FlowInstaller.cs`
`App/GameBootstrap.cs` は3つを順に呼ぶだけ。**ここを編集する必要が出たら、まず設計を疑う。**

**3. モジュールをまたぐ連絡は event で。**
`DuelManager` はUIの型を1つも参照していない。`OnPhaseChanged` などを UI 側が購読している。
この向きを保つ限り、UI担当と進行担当は同じファイルを触らずに済む。

**4. 新しいファイルは自分のフォルダに作る。**
同名ファイルを別々の人が作ると `.meta` の GUID が衝突する。

## 動かし方

Unityで `SampleScene` を開いて再生するだけ。
DBのテストデータは `python tools/seed_sqlite.py`、
アップロードの再現は `python tools/seed_sqlite.py --add 剣の名前`。
