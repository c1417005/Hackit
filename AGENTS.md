# 友達ソード — Unity担当向けプロジェクト文脈

このファイルはCodexが起動時に読む前提のコンテキストです。
**このリポジトリでの私の担当は Unity 部分のみ**です。Web・OpenCV・サーバー・クラウドは別メンバーが担当しています。

---

## 1. ゲーム概要

人の全身写真から剣を生成し、その剣で1vs1対戦するゲーム。

**ユーザー体験フロー**

1. Webサイト上でスマホから人の全体像を撮影し、アップロードする
2. 撮影した画像が処理され、剣になる
3. PC上で、生成された剣をプレイヤー2人が選択する（マリオカートのキャラ選択のような画面）
4. 1vs1で、選択した剣をPS4コントローラー操作で戦わせる
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

**重要な設計判断**

- UnityはサーバーPCを経由せず、Supabase REST API を直接読み書きする
- 画像処理とステータス算出はアップロード時に完走させる。Unityは完成済みPNGを落とすだけ
- 3Dの剣モデルは生成しない。**板ポリ(Quad)に切り抜き画像を貼る**方式。カメラ横固定なので破綻しない
- ステータスは生成AIに全任せせず、画像から機械的に算出する（再現性とバランスのため）

## 4. データ契約（チーム間で確定済み・変更禁止）

`swords` テーブル / API レスポンス:

```json
{
  "id": "uuid",
  "name": "たけしの剣",
  "image_url": "https://xxx.supabase.co/storage/v1/object/public/swords/uuid.png",
  "stats": { "attack": 45, "defense": 35, "speed": 40, "reach": 1.3 },
  "created_at": "2026-08-01T12:00:00Z"
}
```

- `attack` / `defense` / `speed` : 合計120ポイントを配分した値
- `reach` : 剣の長さ倍率（0.8〜1.5想定）
- 一覧取得: `GET /rest/v1/swords?select=*&order=created_at.desc&limit=30`
- 戦績送信: `POST /rest/v1/matches` body `{"winner_id": "...", "loser_id": "..."}`
- ヘッダ: `apikey` と `Authorization: Bearer <anon key>`

**注意**: `JsonUtility` はトップレベルが配列のJSONをパースできない。Supabaseは `[{...}]` を返すので `SwordListWrapper.FromJsonArray()` で包んでからパースすること。

## 5. Unity側のスクリプト

Unityプロジェクトの実体はリポジトリ直下ではなく `Hackit_tomodati-sord/` の中。
スクリプトは `Hackit_tomodati-sord/Assets/Scripts/` に配置。

環境: Unity 6000.5.6f1 / URP 17.5 / Input System 1.20。

**実装済み**

| ファイル | 役割 |
|---|---|
| `SwordData.cs` | サーバーと共有するデータモデル。フィールド名はJSONキーと完全一致させること。`SwordListWrapper.FromJsonArray()` もここ |
| `SwordBuilder.cs` | 画像+ステータスから剣のGameObjectを生成。`SwordRoot > Blade(Quad) / Spine` の階層 |
| `Fighter.cs` | 手を支点に剣を組み立て、縦斬り・横斬り・HP・3D連続命中判定を管理。`OnHpChanged(current, max)` / `OnDefeated(fighter)` を公開 |
| `BattleEffects.cs` | 残像以外の命中演出、ダメージ文字、簡易効果音、カウントダウン、K.O.表示をコード生成 |
| `HitStop.cs` | 命中時に `Time.timeScale` を一瞬落とす演出。多重呼び出しで伸びない |
| `HpBarUI.cs` | 1人分のHPバー。`Fighter.OnHpChanged` を購読。減った分を白帯が遅れて追う。`rightAligned` で2P用に右詰めになる |
| `BattleHud.cs` | 対戦画面のHUD。`BattleHud.Create(p1, p2)` でCanvasとHPバー2本をコードから組み立てる |
| `BattleTestBootstrap.cs` | **テスト用の足場**。地面・カメラ・ファイター2体を実行時に組み立て、モックの剣を手動Equipする。剣画像も手続き的に生成するのでアセット不要。選択画面ができたら捨てて良い |

**通信・進行**

| ファイル | 役割 |
|---|---|
| `SwordRepository.cs` | Supabaseとの通信。`useMock=true` の間は通信せずローカルモックで動く。通信失敗時もモックにフォールバックしてデモを止めない |
| `DuelManager.cs` | 剣一覧取得 → 選択 → 装備 → 対戦 → 決着 → 戦績送信 |

**剣の構造**

Quadのピボットは中心にあるため、そのまま回すと剣の真ん中を軸に回ってしまう。
`SwordRoot`（回転の中心＝握り）の子として `Blade` を上方向にオフセットして配置してある。

実際の階層は `Fighter > HandPivot > Palm / Thumb / Grip / SwordPivot > SwordRoot > Blade / Spine / Hitbox / TipTrail`。
`HandPivot` の原点が手首の支点。手と剣をまとめて3D回転し、縦斬りと横斬りで刃先をZ方向にも動かす。
`SwordPivot` は握りの位置決めと左右の向き反転を担当する（左向きは Y軸180度回転）。
`localScale.x = -1` での反転は Collider に負のスケールがかかるので使わないこと。
反転時に裏面が見えるため、剣のマテリアルは `_Cull = Off` にしてある。

旧HingeJoint式の吊り下げ物理は廃止済み。剣のHitboxはTriggerなので、剣同士が絡まって停止しない。斬撃中は毎フレームOverlapBoxと前位置からのBoxCastを行い、Z方向を含む高速移動の抜けを防ぐ。

**操作（DualShock4）**

- □ (buttonWest) … 縦斬り
- △ (buttonNorth) … 横斬り

Input System の Action Asset は使わず、`Gamepad.all[playerIndex]` を直接ポーリングしている。
2人ローカル対戦ではこの方が設定が要らず事故らないため、この方針を維持する。

パッドが人数分刺さっていない時のために、`Fighter.keyboardFallback`（既定ON）でキーボード代用が効く。
1P = `F` 縦斬り・`R` 横斬り、2P = `.` 縦斬り・`,` 横斬り。
リザルト表示後は `R` で選択画面へ戻る。

**戦闘ロジック**

- 振り速度は `stats.speed` から算出。`speed` 20〜70 を攻撃時間へ線形にマップ
- 1回の振りは 振りかぶり → Triggerが有効な斬撃 → 戻し
- 1回の攻撃で命中は1回だけ
- ダメージ = `max(1, 12 + attack * 0.42 - defense * 0.18)`
- 命中時に0.065秒のヒットストップを入れている
- HPは 100 固定。`Fighter.maxHp` で変えられる

## 6. 未実装（これから作る部分）

優先度順:

1. ~~**戦闘が成立する状態**~~ — 完了。`BattleTestBootstrap` を `SampleScene` に置いてあるので再生するだけで動く
2. ~~**HPバーUI**~~ — 完了。`BattleHud` / `HpBarUI`。`BattleTestBootstrap` から `BattleHud.Create(p1, p2)` を呼んでいるので、`DuelManager` ができたらそちらから呼ぶ形に移す
3. **剣の選択画面** — `DuelManager.Swords` を並べ、コントローラーで左右選択、決定で `DuelManager.SelectSword(playerIndex, data)` を呼ぶ。2人揃ったら `StartBattle()`
4. **勝利演出・リザルト画面** — `Fighter.OnDefeated` を受けて出す。今は `BattleTestBootstrap.HandleDefeated()` が仮で "1P WIN" を出しているだけ
5. **Supabaseとの結合** — `SwordRepository` の `useMock` を false にして接続先を設定。最後にやる

`Fighter` 側の受け口はもう空いている: `Equip(SwordData, Texture2D)` / `ResetForBattle()` / `SetInputEnabled(bool)` / `SetFacing(int)`。
`DuelManager` はこれらを呼ぶだけで良い。

## 7. 既知のハマりどころ

- **剣がマゼンタ（ピンク）になる**: 描画パイプラインの不一致。`SwordBuilder.UnlitShaderCandidates` に、使用中のパイプラインのUnlitシェーダー名を先頭側へ追加する。現状は `Universal Render Pipeline/Unlit` で解決済み
- **`Rigidbody` 配下で "Concave Mesh Colliders are not supported" が出る**: `GameObject.CreatePrimitive` はコライダー付きで生まれ、`Object.Destroy` は遅延実行なので1フレーム残る。`SwordBuilder.CreateMeshObject()` のように組み込みメッシュから自前で組むか、`DestroyImmediate` を使う
- **`_rb.linearVelocity` がコンパイルエラー**: Unity 2022以前は `_rb.velocity`。本プロジェクトは Unity 6 なので `linearVelocity` が正
- **Input System が動かない**: Project Settings → Player → Active Input Handling を確認。本プロジェクトは `Input System Package (New)` 設定済み
- **本番のPNGを使う時**: Texture Type は Sprite ではなく `Default`、`Alpha Is Transparency` を ON。`SwordBuilder` はアルファクリップ（`_Cutoff` 0.5）で抜いている
- **Unity MCP が `Unity not detected` を返す**: コンパイル/ドメインリロード中の一時的なもの。同じ呼び出しをリトライすれば通る
- **uGUI で単色の板が出ない**: `Image` は `sprite` 未設定だと何も描かない。`RawImage` なら `texture` 未設定でも `color` そのままの矩形になるので、Sprite を用意せずに済む
- **UIのテキスト**: TextMeshPro は初回に Essentials のインポートを求められて詰まるので、`UnityEngine.UI.Text` + `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")` を使っている（Unity 2022以降のフォント名。それ以前は `Arial.ttf`）

## 8. Codexへの依頼方針

- ハッカソンの残り時間が短いため、**リファクタリングより動く状態を優先**してほしい
- 既存のスクリプト構造（特に剣の階層と `useMock` フォールバック）は維持する
- データ契約（セクション4）はチーム間の合意事項なので勝手に変更しない
- 新しいパッケージの導入は極力避ける
- このファイル自体はチーム共有ではなく私用のメモ。実態とズレたら書き換えて良い
- `BattleTestBootstrap.cs` は使い捨ての足場。本番フローができたら遠慮なく消す
