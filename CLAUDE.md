# 友達ソード — Unity担当向けプロジェクト文脈

このファイルはClaude Codeが起動時に読む前提のコンテキストです。
**このリポジトリでの私の担当は Unity 部分のみ**です。Web・OpenCV・サーバー・クラウドは別メンバーが担当しています。

---

## 1. ゲーム概要

人の全身写真から剣を生成し、その剣で1vs1対戦するゲーム。

**目指す手触りは「ソーセージレジェンド」**。剣そのものが天井から吊り下がった振り子になっていて、
左右に漕いで振り回し、相手の剣にぶつけていく。ダメージは衝突の勢いで決まる。

漕ぐ力は重力に勝てないので、押しっぱなしでは持ち上がらない。**ブランコと同じで、
振り子の周期に合わせて漕ぐと勢いが乗り、やがて何周も高速回転できる**。
この「振り回せているか」がそのまま強さになる。移動もガードも無い。

**ユーザー体験フロー**

1. Webサイト上でスマホから人の全体像を撮影し、アップロードする
2. 撮影した画像が処理され、剣になる
3. PC上で、生成された剣をプレイヤー2人が選択する（マリオカートのキャラ選択のような画面）
4. 1vs1で、選択した剣を振り子として激突させる（PS4コントローラー）
5. HPが先になくなった方の負け

## 2. 開発条件

- チーム5人（Web / OpenCV / サーバー・クラウド / Unity＝私 / 他）
- **ハッカソンで1〜2日**の短期開発。作り込みより完走を優先する
- 対戦画面は **3D空間だが、カメラは横固定**（実質2.5D）

## 3. 全体アーキテクチャ

```
[Web(スマホ)] --画像--> [サーバーPC: FastAPI]
                            |  OpenCVで人物切り抜き
                            |  画像からステータスを機械的に算出
                            v
                    [Supabase]
                      - Storage : 切り抜き済みPNG (public)
                      - Postgres: swords / matches テーブル
                            ^
                            | REST API を直接叩く（サーバーPCは経由しない）
                            v
                        [Unity]
```

**開発中のデータ源（2026-08-01 追加）**

Supabase の結合を待たずに進められるよう、Unity は**ローカルの SQLite** からも読めるようにしてある。
`SwordRepository.source` で `Mock / Sqlite / Supabase` を切り替える。既定は `Sqlite`。

```
Assets/StreamingAssets/tomodachi_sword.db   ← Unityが読む
tools/seed_sqlite.py                        ← テストデータを入れ直すスクリプト
```

テーブルは JSON の契約をそのまま平らにしたもの。`stats` のネストは無く
`attack / defense / speed / height_cm` が列。`SwordRepository` が `SwordData` に組み直す。

```sql
swords  (id TEXT PK, name TEXT, image_url TEXT, image BLOB, attack INT, defense INT, speed INT, height_cm REAL, created_at TEXT)
matches (id INTEGER PK AUTOINCREMENT, winner_id TEXT, loser_id TEXT, created_at TEXT)
```

**重要な設計判断**

- UnityはサーバーPCを経由せず、Supabase REST API を直接読み書きする
- 画像処理とステータス算出はアップロード時に完走させる。Unityは完成済みPNGを落とすだけ
- 背景透過済みTポーズPNGの輪郭を押し出し、身長別の小・中・大の雛型設定で3Dモデルを実行時生成する
- ステータスは生成AIに全任せせず、画像から機械的に算出する（再現性とバランスのため）

## 4. データ契約（チーム間で確定済み・変更禁止）

`swords` テーブル / API レスポンス:

```json
{
  "id": "uuid",
  "name": "たけしの剣",
  "image_url": "https://xxx.supabase.co/storage/v1/object/public/swords/uuid.png",
  "stats": { "attack": 45, "defense": 35, "speed": 40, "height_cm": 172 },
  "created_at": "2026-08-01T12:00:00Z"
}
```

- `attack` / `defense` / `speed` : 合計120ポイントを配分した値
- `height_cm` : 撮影した人物の身長。モデル長は `height_cm × (1.5 / 170)`。160cm未満=小、160〜180cm未満=中、180cm以上=大の調整設定を選ぶ
- 一覧取得: `GET /rest/v1/swords?select=*&order=created_at.desc&limit=30`
- 戦績送信: `POST /rest/v1/matches` body `{"winner_id": "...", "loser_id": "..."}`
- ヘッダ: `apikey` と `Authorization: Bearer <anon key>`

**注意**: `JsonUtility` はトップレベルが配列のJSONをパースできない。Supabaseは `[{...}]` を返すので `SwordListWrapper.FromJsonArray()` で包んでからパースすること。

## 5. Unity側のスクリプト

Unityプロジェクトの実体はリポジトリ直下ではなく `Hackit_tomodati-sord/` の中。
スクリプトは `Hackit_tomodati-sord/Assets/Scripts/` に配置。

環境: Unity 6000.5.6f1 / URP 17.5 / Input System 1.20。

**モジュール構成（2026-08-01、3人開発のため整理）**

```
Assets/Scripts/
  Core/    SwordData Sqlite HitStop WavyText              ← 依存なし
  Data/    SwordRepository                                 ← Core
  Battle/  Fighter SwordBuilder BattleCamera BattleEffects BattleStageInstaller
  Flow/    DuelManager FlowInstaller                       ← Core Data Battle
  UI/      BattleHud HpBarUI SwordSelectUI ForgeUI UiInstaller DebugResultOverlay
  App/     GameBootstrap                                   ← 全部
```

- 依存は下から上への一方通行。`.asmdef` で強制してあり、逆流するとコンパイルエラーになる
- 担当は「戦闘 = Battle」「UI・演出 = UI」「データ・進行 = Data + Flow」。`Core` と `App` は共有
- **詳しい決まりは [`Assets/Scripts/CLAUDE.md`](Hackit_tomodati-sord/Assets/Scripts/CLAUDE.md) にある。エージェントはそちらを読む**

**実装済み**

| ファイル | 役割 |
|---|---|
| `SwordData.cs` | サーバーと共有するデータモデル。フィールド名はJSONキーと完全一致させること。`SwordListWrapper.FromJsonArray()` もここ |
| `SwordBuilder.cs` | 画像+ステータスから剣の見た目を生成。`SwordRoot(握り) > Blade(Quad) / Spine(薄い芯)` の階層。当たり判定は作らず `GetMetrics()` で寸法だけ渡す |
| `Fighter.cs` | プレイヤー本体＝吊り下がった剣そのもの。漕ぎ・衝突ダメージ・HP。`OnHpChanged(current, max)` / `OnSpinChanged(0〜1)` / `OnDefeated(fighter)` を公開 |
| `BattleCamera.cs` | 両者の剣が収まる最小限まで自動で寄り引きする横固定カメラ |
| `HitStop.cs` | 命中時に `Time.timeScale` を一瞬落とす演出。多重呼び出しで伸びない |
| `HpBarUI.cs` | 1人分のHPバー。`Fighter.OnHpChanged` を購読。減った分を白帯が遅れて追う。`rightAligned` で2P用に右詰めになる |
| `BattleHud.cs` | 対戦画面のHUD。`BattleHud.Create(p1, p2)` でCanvasとHPバー2本を組み立てる。`Bind(duel)` すると対戦中とリザルト中だけ表示される |
| `SwordRepository.cs` | 剣データの取得元。`source` で `Mock / Sqlite / Supabase` を切替。どれで失敗しても最後はモックに落ちるのでデモが止まらない。モック画像の手続き生成もここ |
| `Sqlite.cs` | SQLite の最小 P/Invoke ラッパー。読み取りと単純な書き込みだけ |
| `DuelManager.cs` | 剣一覧取得 → 選択 → 装備 → 対戦 → 決着 → 戦績送信。`Loading / Select / Battle / Result` の4フェーズ。画面は持たない |
| `SwordSelectUI.cs` | 既存の剣を選ぶ画面。カードを並べ、2人分のカーソルで選ぶ |
| `ForgeUI.cs` | 「既存/新規」のモード選択と、新規作成の一連の画面（QR→錬成→抜刀→確認） |
| `WavyText.cs` | 1文字ずつ波打つ見出し。錬成演出用。Legacy Text を並べて自前で動かしている |
| `GameBootstrap.cs` | 組み立ての起点。3つの Installer を順に呼ぶだけ。シーンにはこれを貼った空オブジェクトが1つあるだけ |
| `BattleStageInstaller.cs` | 地面・背景・カメラ・ライト・後処理・ファイター2体を組み立てて返す |
| `FlowInstaller.cs` | `SwordRepository` + `DuelManager` を組み立てる |
| `UiInstaller.cs` | 画面をまとめて生成する。画面を足すならここに1行 |
| `DebugResultOverlay.cs` | 操作説明と決着の仮表示（IMGUI）。リザルト画面ができたら消す |

**振り子の構造（崩さないこと）**

```
Fighter          … 原点が「吊り元」。Rigidbody + HingeJoint(Z軸) + BoxCollider(刃)
  ├ Rope         … 吊り元から握りまでの紐（見た目だけ）
  └ SwordPivot   … 握りの位置。Z180度で剣を下向きに、左向きならY180度でミラー
      └ SwordRoot
          ├ Blade  … 切り抜き画像を貼った板ポリ(Quad)
          └ Spine  … 薄い芯
```

- Quadのピボットは中心にあるので、`SwordRoot` の子として `Blade` を上方向にオフセットしてある
- **当たり判定は Fighter 自身の BoxCollider に置く**。子に置くと衝突コールバックの行き先が変わって扱いが増える
- `localScale.x = -1` での左右反転は Collider に負のスケールがかかるので使わない。Y軸180度回転でミラーし、裏面が見えるので剣のマテリアルは `_Cull = Off`
- HingeJoint は `connectedBody = null` でワールドに固定。吊り元を動かしたら `connectedAnchor` を取り直すこと（`ResetForBattle` がやっている）

**間合いの条件**

吊り元の間隔 `D` が広すぎると剣が相手に永久に届かない。条件は

```
吊り元から刃先までの距離 R > sqrt(D^2 + ropeLength^2)
```

`R` は身長から生成したモデル長に連動する。`D = 1.0`（`spawnDistance = 0.5`）で sqrt(1.25) = 1.12 なので、対応する最小身長でも届くことを確認する。
`spawnDistance` を触るときは必ずこれを確認する。

**操作（DualShock4）— 左右だけ**

- 左スティック左右 / L1・R1 / 十字左右 … 漕ぐ（左回り・右回り）

移動もガードも無い。選択画面の決定は ×(buttonSouth)、取消は ○(buttonEast)。

Input System の Action Asset は使わず、`Gamepad.all[playerIndex]` を直接ポーリングしている。
2人ローカル対戦ではこの方が設定が要らず事故らないため、この方針を維持する。

パッドが人数分刺さっていない時のために、`Fighter.keyboardFallback`（既定ON）でキーボード代用が効く。
1P = `A`/`D`、2P = `←`/`→`。選択画面も同じキーで、決定は 1P `F` / 2P `.`、取消は 1P `G` / 2P `/`。
決着後は `R` で選択画面に戻る。

`useExternalInput = true` にすると入力を読まなくなり、`SetSwingInput(-1〜1)` で外から漕がせられる
（CPU戦や動作確認用）。

**戦闘ロジック**

- 漕ぐトルクは**重力トルクを基準にした相対値**（`swingPower`、既定 0.62）。剣の長さや重さが変わっても操作感が揃う
- **`swingPower` は必ず 1.0 未満にすること**。1.0 を超えると押しっぱなしで持ち上がってしまい、漕ぐ意味が無くなってゲームが成立しない
- 回転速度の上限は `baseMaxSpin`（9 rad/s）× speed 補正。上限方向にはトルクを足さない
- ダメージ = `max(1, 刃先の速さ × damageScale × (attack / 40) - defense × 0.3)`
- 衝突時は**それぞれが自分の刃先の速さぶんだけ相手に与える**。振れていない側は `minImpactSpeed` 未満で弾かれるので与えない。撃ち合えば両者削れる
- 刃先の速さは `FixedUpdate` で控えた**衝突前**の値を使う。`OnCollisionEnter` の時点では衝突後の速度しか見えない
- 命中時に0.05秒のヒットストップ。多重ヒット防止に `hitCooldown` 0.25秒
- HPは 100 固定

実測（2026-08-01、共振ポンピングで漕いだ場合）:
静止から漕ぎ始めて振り角180度・1回転に到達、最大刃先速度 11.4 m/s。
両者が漕ぎ続けた状態で 56秒で HP 100 → 60 程度。

**stats の割り当て**

| stat | 効果 |
|---|---|
| `attack` | ダメージ係数 |
| `defense` | 重さ（`mass = 1 + defense/60`）とダメージ軽減 |
| `speed` | 角度抵抗と回転速度上限。高いほど速く回せる |
| `height_cm` | モデルの長さと攻撃範囲。`height_cm × (1.5 / 170)` でUnity上の長さに変換 |

## 6. 未実装（これから作る部分）

優先度順:

1. ~~**戦闘が成立する状態**~~ — 完了。`BattleTestBootstrap` を `SampleScene` に置いてあるので再生するだけで動く。2026-08-01 にソーセージレジェンド式（振り子＋チャージ）へ作り直した
2. ~~**HPバーUI**~~ — 完了。`BattleHud` / `HpBarUI`。HPバーの下にチャージゲージも出る
3. ~~**剣の選択画面**~~ — 完了。`SwordSelectUI`。カード選択 → `DuelManager.SelectSword()` → 2人揃うと自動で `StartBattle()`
4. **勝利演出・リザルト画面** — `DuelManager` の `Result` フェーズに乗せる。今は `BattleTestBootstrap.OnGUI()` が "1P WIN" と "Rキーで戻る" を出しているだけ。`DuelManager.Winner` / `Loser` / `CanLeaveResult` が使える
5. **Supabaseとの結合** — `SwordRepository` の `useMock` を false にして `supabaseUrl` と `anonKey` を入れるだけ。通信部分は書いてあるが**未検証**（モックでしか動かしていない）

`Fighter` 側の受け口: `Equip(SwordData, Texture2D)` / `ResetForBattle()` / `SetInputEnabled(bool)` / `SetFacing(int)`。

**フェーズごとの画面**

| フェーズ | SwordSelectUI | ForgeUI | BattleHud | 対戦キャラ |
|---|---|---|---|---|
| Loading / Select | 表示 | 非表示 | 非表示 | 非アクティブ |
| ModeSelect / Forge | 非表示 | 表示 | 非表示 | 非アクティブ |
| Battle / Result | 非表示 | 非表示 | 表示 | アクティブ |

**起動からの流れ**

```
Loading → ModeSelect（2人同時に「既存の武器」か「新規作成」を選ぶ）
            ↓ 新規を選んだ人だけ1人ずつ
          Forge: WaitingUpload → Forging → Ready → Drawn → Confirm
            ↓ 「この剣で戦う」で準備完了 / 「既存の武器」なら Select へ回る
          Select（まだ決まっていない人だけが操作。決まった人は「準備完了」表示）
            ↓ 2人とも決まった時点で
          Battle
```

- 新着の剣は「錬成画面に入った時点で存在しなかった `id`」で判定する。サーバーとの合図が要らない
- `uploadPollInterval`（0.7秒）でDBを見に行く。`minForgeSeconds`（3.2秒）は演出を最低限見せるための下限
- QR画像は `Assets/StreamingAssets/qr.png`。無ければ枠内に断り書きが出る。`ForgeUI.qrImage` に直接差してもよい
- 動作確認は `python tools/seed_sqlite.py --add 剣の名前` でアップロードを再現できる

## 7. 既知のハマりどころ

- **剣がマゼンタ（ピンク）になる**: 描画パイプラインの不一致。`SwordBuilder.UnlitShaderCandidates` に、使用中のパイプラインのUnlitシェーダー名を先頭側へ追加する。現状は `Universal Render Pipeline/Unlit` で解決済み
- **`Rigidbody` 配下で "Concave Mesh Colliders are not supported" が出る**: `GameObject.CreatePrimitive` はコライダー付きで生まれ、`Object.Destroy` は遅延実行なので1フレーム残る。`SwordBuilder.CreateMeshObject()` のように組み込みメッシュから自前で組むか、`DestroyImmediate` を使う
- **`_rb.linearVelocity` がコンパイルエラー**: Unity 2022以前は `_rb.velocity`。本プロジェクトは Unity 6 なので `linearVelocity` が正
- **Input System が動かない**: Project Settings → Player → Active Input Handling を確認。本プロジェクトは `Input System Package (New)` 設定済み
- **本番のPNGを使う時**: Texture Type は Sprite ではなく `Default`、`Alpha Is Transparency` を ON。`SwordBuilder` はアルファクリップ（`_Cutoff` 0.5）で抜いている
- **Unity MCP が `Unity not detected` を返す**: コンパイル/ドメインリロード中の一時的なもの。同じ呼び出しをリトライすれば通る
- **uGUI で単色の板が出ない**: `Image` は `sprite` 未設定だと何も描かない。`RawImage` なら `texture` 未設定でも `color` そのままの矩形になるので、Sprite を用意せずに済む
- **UIのテキスト**: TextMeshPro は初回に Essentials のインポートを求められて詰まるので、`UnityEngine.UI.Text` + `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` を使っている（Unity 2022以降のフォント名。それ以前は `Arial.ttf`）
- **剣が相手に届かない**: 吊り元が離れすぎている。上の「間合いの条件」を確認する。シーンに保存済みの `spawnDistance` はスクリプトの既定値より優先されるので、既定値を変えただけでは直らない
- **押しっぱなしで剣が持ち上がってしまう**: `swingPower` が 1.0 以上になっている。漕ぐゲームでなくなるので必ず 1.0 未満に
- **SQLite のネイティブをどこから持ってきているか**: `Assets/Plugins` には何も置いていない。Windows 10以降が標準で持つ `winsqlite3.dll` を叩いている。`Sqlite.cs` は `sqlite3` → `winsqlite3` の順に探すので、**公式の `sqlite3.dll` を `Assets/Plugins/x86_64/` に置けば自動でそちらに乗り換わる**。Windows以外に出すときは置くこと（`winsqlite3` は Windows 専用で、MS がアプリ向けに保証している API でもない）
- **DBのパス**: `Application.streamingAssetsPath`。StreamingAssets はビルドにそのままコピーされるので、実行ファイルの隣から同じ相対パスで読める
- **テストデータを入れ直す**: `python tools/seed_sqlite.py`。テーブルごと作り直すので matches も消える
- **Play中にスクリプトを保存すると状態が壊れる**: ドメインリロードでコルーチンと非シリアライズのフィールドが飛ぶ。`DuelManager` が「Select フェーズなのに剣0件」のようになったら、Playを抜けて入り直す
- **`AddComponent` した直後にフィールドを差し込む場合、`OnEnable` での購読は間に合わない**: `OnEnable` は `AddComponent` の時点で走るので、その後に代入する参照は null。`Start` で張り直すこと（`DuelManager.SubscribeFighters` が実例）
- **エディタが非フォーカスだと Play モードのフレームが進まない**: `frameCount` が 1 のまま止まる。外部から動作確認する時は `Application.runInBackground = true` にする
- **`InputSystem.QueueStateEvent` で注入した入力は `wasPressedThisFrame` にならない**: `isPressed` は変わるがエッジ検出は発火しない。コード上から入力操作をテストすることはできないので、操作まわりは実機で確認するしかない

## 8. Claude Codeへの依頼方針

- ハッカソンの残り時間が短いため、**リファクタリングより動く状態を優先**してほしい
- 既存のスクリプト構造（特に剣の階層と `useMock` フォールバック）は維持する
- データ契約（セクション4）はチーム間の合意事項なので勝手に変更しない
- 新しいパッケージの導入は極力避ける
- このファイル自体はチーム共有ではなく私用のメモ。実態とズレたら書き換えて良い
- `BattleTestBootstrap.cs` は使い捨ての足場。本番フローができたら遠慮なく消す
