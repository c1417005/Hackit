# サーバー起動コマンド

## 実行ディレクトリ

**必ずリポジトリのルート（`Hackit\`）から実行する。**

```
C:\Users\soufu\Hackit>     ← ここ
```

`backend\` の中に入って実行してはいけない。`main.py` の中でパスを相対指定しているため、
別の場所に DB や画像フォルダが作られてしまう。

| 参照先 | コード上の指定 | ルートから起動した場合 |
| --- | --- | --- |
| DB | `my.db` | `Hackit\my.db` |
| 画像保存先 | `images/` | `Hackit\images\` |
| 撮影ページ | `image_process/website.html` | `Hackit\image_process\website.html` |

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
