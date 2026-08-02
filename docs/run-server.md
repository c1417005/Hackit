# サーバー起動コマンド

## 実行ディレクトリ

**必ずリポジトリのルート（`Hackit\`）から実行する。**

```
C:\Users\soufu\Hackit>     ← ここ
```

`backend.main:app` というモジュール指定を解決するため、ルートから起動する必要がある。
`backend\` の中に入って実行すると `Could not import module "backend.main"` になる。

なお DB と画像の置き場所は `main.py` 内で `BASE_DIR`（リポジトリのルート）を基準に
組み立てているので、起動ディレクトリが違っても別の場所に作られることはない。

| 参照先 | 実際の場所 |
| --- | --- |
| DB | `Hackit\my.db` |
| 画像保存先 | `Hackit\images\before\`（原本）、`Hackit\images\after\`（背景除去後） |
| 撮影ページ | `Hackit\image_process\website.html` |

`my.db` と `images\` は `.gitignore` 済み。各自のローカルで自動生成されるので共有しない。

---

## 起動コマンド

シェルによってパスの区切り文字が違う。**シェルと表記をセットで合わせること。**

### PowerShell

```powershell
venv\Scripts\python.exe -m uvicorn backend.main:app --host 0.0.0.0 --port 8000 --reload --reload-dir backend
```

### cmd

```cmd
venv\Scripts\python.exe -m uvicorn backend.main:app --host 0.0.0.0 --port 8000 --reload --reload-dir backend
```

PowerShell と同じ。

### Git Bash

```bash
./venv/Scripts/python.exe -m uvicorn backend.main:app --host 0.0.0.0 --port 8000 --reload --reload-dir backend
```

スラッシュに変える。先頭の `./` も必要。

> bash でバックスラッシュを使うとエスケープ扱いされ、
> `bash: venvScriptspython.exe: command not found` になる。

### デモ・本番時

`--reload --reload-dir backend` を外す。編集中に一瞬落ちるのを避けるため。

```powershell
venv\Scripts\python.exe -m uvicorn backend.main:app --host 0.0.0.0 --port 8000
```

---

## オプションの意味

| オプション | 意味 | 外すとどうなるか |
| --- | --- | --- |
| `--host 0.0.0.0` | LAN 内の他端末からの接続を受ける | `127.0.0.1` のみになり、他の PC・スマホから繋がらない |
| `--port 8000` | 待ち受けポート | 塞がっていたら `8001` などに変える |
| `--reload` | `.py` 保存時に自動再起動 | 手動で再起動が必要 |
| `--reload-dir backend` | 監視対象を `backend\` に限定 | `venv\` 配下まで監視して重くなる |

`venv\Scripts\activate` は不要。`venv\Scripts\python.exe` を直接指定すれば
その venv で動くので、PowerShell の実行ポリシーで activate がブロックされる問題を避けられる。

---

## 起動後の確認

### 1. サーバー自身から

```
http://127.0.0.1:8000/health   → {"ok":true}
http://127.0.0.1:8000/dbtest   → テストデータ3件
```

### エンドポイント一覧

| メソッド | パス | 用途 |
| --- | --- | --- |
| GET | `/` | スマホの撮影ページ |
| GET | `/health` | 疎通確認 |
| POST | `/api/upload` | 撮影ページから `image` / `name` / `height` を受け取る。`{"ok":true,"id":12,"status":"pending"}` を即返し、画像処理は裏で走る |
| GET | `/api/persons` | **Unity 用**。処理が完走した（`status='done'`）行だけ返す |
| GET | `/api/persons/{id}` | 1件の状態を返す。撮影ページが完了を待つのに使う |
| GET | `/dbtest` | デバッグ用。`status` に関係なく全件返す |
| GET | `/images/before/{id}.jpg` | アップロード原本 |
| GET | `/images/after/{id}.png` | 背景除去済みテクスチャ |

`status` は `pending` → `processing` → `done`（または `failed`）と遷移する。
`failed` のときは `error` 列に理由が入る。

### 2. 他の PC・スマホから

サーバー PC で `ipconfig` を実行し、**Wi-Fi アダプタの IPv4** を確認する。

```
http://<サーバーのIP>:8000/dbtest   ← Unity 担当が疎通確認
http://<サーバーのIP>:8000/         ← スマホの撮影ページ
```

VirtualBox（`192.168.56.x`）や VMware（`192.168.x.1`）の仮想アダプタの IP も一緒に出るが、
**それらは他の PC から繋がらない**ので選ばないこと。

---

## 繋がらないときのチェック順

1. **`--host 0.0.0.0` を付けたか** — いちばん多い原因
2. **Windows ファイアウォール** — 初回起動時のダイアログで「プライベートネットワーク」を許可する
3. **IP が変わっていないか** — DHCP なので Wi-Fi 再接続で変わる。`ipconfig` で確認し直す
4. **AP アイソレーション** — 学内 Wi-Fi は端末同士の通信をブロックしていることがある。
   その場合はファイアウォールを開けても繋がらないので、スマホのテザリングに全員集める

## よくあるエラー

| エラー | 原因 | 対処 |
| --- | --- | --- |
| `bash: venvScriptspython.exe: command not found` | bash でバックスラッシュを使った | スラッシュ表記に変える |
| `No module named uvicorn` | システムの Python で動いている | `venv\Scripts\python.exe` とフルパス指定する |
| `Could not import module "backend.main"` | 実行ディレクトリが違う | リポジトリのルートから実行する |
| `address already in use` / `10048` | 8000番が使用中 | `--port 8001` に変える |
