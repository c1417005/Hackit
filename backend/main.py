from fastapi import FastAPI, UploadFile, File, Form, BackgroundTasks, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import HTMLResponse
from fastapi.staticfiles import StaticFiles
import pathlib, shutil, sys, sqlite3
from datetime import datetime

# パスは全部リポジトリのルート基準にする。
# こうしておけば、どのディレクトリから uvicorn を起動しても DB と画像の置き場所がズレない。
BASE_DIR = pathlib.Path(__file__).resolve().parent.parent
IMAGE_PROCESS_DIR = BASE_DIR / "image_process"

# image_process/ はパッケージではないので、import できるよう検索パスに足しておく。
# (パスを通すだけなので起動は遅くならない。重い import は使う直前におこなう)
if str(IMAGE_PROCESS_DIR) not in sys.path:
    sys.path.insert(0, str(IMAGE_PROCESS_DIR))

dbname = BASE_DIR / "my.db"

IMAGES = BASE_DIR / "images"
BEFORE_DIR = IMAGES / "before"   # スマホから届いた原本
AFTER_DIR = IMAGES / "after"     # 背景除去したテクスチャ
for d in (IMAGES, BEFORE_DIR, AFTER_DIR):
    d.mkdir(parents=True, exist_ok=True)

ALLOWED_SUFFIXES = {".jpg", ".jpeg", ".png", ".webp"}


# /=== DB部

# persons の列。スキーマを変えたらここも必ず合わせる (起動時の照合に使っている)
PERSON_COLUMNS = [
    "id", "name", "height",
    "before_path", "after_path",
    "hue", "saturation", "value", "r", "g", "b", "attribute",
    "attack", "speed",
    "status", "error", "created_at", "updated_at",
]

CREATE_PERSONS_SQL = '''
CREATE TABLE IF NOT EXISTS persons(
    id          INTEGER PRIMARY KEY AUTOINCREMENT,

    -- 撮影ページから受け取る (アップロード時点で確定)
    name        TEXT    NOT NULL,
    height      INTEGER NOT NULL,
    before_path TEXT,

    -- image_Processing.py が埋める
    after_path  TEXT,

    -- color_extraction.py が埋める (attack/speed の根拠)
    hue         REAL,
    saturation  REAL,
    value       REAL,
    r           INTEGER,
    g           INTEGER,
    b           INTEGER,
    attribute   TEXT,

    -- 最終ステータス
    attack      INTEGER,
    speed       INTEGER,

    -- 処理の進行状況: pending / processing / done / failed
    status      TEXT    NOT NULL DEFAULT 'pending',
    error       TEXT,
    created_at  TEXT    NOT NULL DEFAULT (datetime('now','localtime')),
    updated_at  TEXT
)
'''

# before_path / after_path は IMAGES からの相対パス ("before/1.jpg")。
# ディスク上は IMAGES / その値、URL は "/images/" + その値 になる。


def connect():
    conn = sqlite3.connect(dbname, timeout=10)
    conn.row_factory = sqlite3.Row
    return conn


def _archive_old_schema(cur):
    """旧スキーマの persons が残っていたら退避する。

    スキーマを変えても CREATE TABLE IF NOT EXISTS は何もしてくれないので、
    古い my.db を持ったまま pull した人が意味不明なエラーに当たるのを防ぐ。
    """
    cur.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='persons'")
    if cur.fetchone() is None:
        return

    existing = {row[1] for row in cur.execute('PRAGMA table_info(persons)')}
    if set(PERSON_COLUMNS) <= existing:
        return

    backup = f"persons_old_{datetime.now():%Y%m%d_%H%M%S}"
    cur.execute(f'ALTER TABLE persons RENAME TO {backup}')
    print(f"[WARN] persons が旧スキーマだったので {backup} に退避して作り直しました")


def init_db():
    conn = connect()
    cur = conn.cursor()

    # 画像処理はバックグラウンドスレッドから書き込むので、読み書きがぶつからないよう WAL にする
    cur.execute('PRAGMA journal_mode=WAL')

    _archive_old_schema(cur)
    cur.execute(CREATE_PERSONS_SQL)
    cur.execute('CREATE INDEX IF NOT EXISTS idx_persons_status ON persons(status)')

    # 接続テスト用のダミーデータ (テーブルが空のときだけ入れる)
    cur.execute('SELECT COUNT(*) FROM persons')
    if cur.fetchone()[0] == 0:
        cur.executemany(
            'INSERT INTO persons(name, height, speed, attack, attribute, status)'
            ' VALUES(?, ?, ?, ?, ?, ?)',
            [
                ("テスト太郎", 170, 12, 34, "炎", "done"),
                ("テスト花子", 160, 25, 18, "氷", "done"),
                ("ずんだもん", 150, 40, 5, "森", "done"),
            ],
        )

    conn.commit()
    conn.close()


init_db()
# ===/


# /=== 画像処理部

def compute_stats(shoulder_height_ratio: float, sat: float, val: float, height: int):
    """特徴量からゲーム用ステータスを決める。

    height は今のところ使っていないが、いずれ補正に使う予定なので引数に残してある。
    """
    from image_Processing import ratio_to_stats

    stats = ratio_to_stats(shoulder_height_ratio, sat, val)
    # TODO: height を使った補正をここに入れる (例: 背が高いほど attack にボーナス)
    return stats["attack"], stats["speed"]


def _touch(conn, person_id: int, **fields):
    """persons の1行を更新する。updated_at は毎回自動で入れる。"""
    fields["updated_at"] = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    assigns = ", ".join(f"{k}=?" for k in fields)
    conn.execute(
        f'UPDATE persons SET {assigns} WHERE id=?',
        [*fields.values(), person_id],
    )
    conn.commit()


def run_pipeline(person_id: int):
    """アップロード直後にバックグラウンドで走る処理本体。

    before 画像 → 背景除去して after 画像 → after から服の色を抽出 → attack/speed 決定。
    途中で落ちてもサーバーは死なせず、status='failed' と error を残す。
    """
    conn = connect()
    try:
        row = conn.execute(
            'SELECT before_path, height FROM persons WHERE id=?', (person_id,)
        ).fetchone()
        if row is None or not row["before_path"]:
            return

        _touch(conn, person_id, status="processing")

        # rembg / mediapipe の import は数秒かかるので、起動時ではなくここで読む
        from image_Processing import remove_background, estimate_physique
        from color_extraction import extract_clothing_color

        before = IMAGES / row["before_path"]
        after_rel = f"after/{person_id}.png"
        after = IMAGES / after_rel

        remove_background(before, after)

        # 体格の推定は背景ありの原本のほうが安定するので before を渡す
        shoulder_height_ratio, _torso_box, _img, _landmarks = estimate_physique(before)

        # 色は「処理後の画像から抽出する」フローなので after を渡す
        color = extract_clothing_color(after)

        attack, speed = compute_stats(
            shoulder_height_ratio, color["saturation"], color["value"], row["height"]
        )

        _touch(
            conn, person_id,
            after_path=after_rel,
            hue=color["hue"],
            saturation=color["saturation"],
            value=color["value"],
            r=color["rgb"]["r"],
            g=color["rgb"]["g"],
            b=color["rgb"]["b"],
            attribute=color["attribute"],
            attack=attack,
            speed=speed,
            status="done",
            error=None,
        )
        print(f"[OK] id={person_id} 処理完了 attack={attack} speed={speed}")

    except Exception as e:
        print(f"[ERROR] id={person_id} 画像処理に失敗: {e}")
        try:
            _touch(conn, person_id, status="failed", error=str(e))
        except Exception:
            pass
    finally:
        conn.close()
# ===/


app = FastAPI()
app.add_middleware(
    CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"]
)

# Unity から UnityWebRequestTexture で直接取りに来られるように画像を配信する
app.mount("/images", StaticFiles(directory=IMAGES), name="images")


def to_json(row: sqlite3.Row) -> dict:
    d = dict(row)
    # Unity 側は URL がそのまま欲しいので、相対パスを URL に組み立てて渡す
    d["before_url"] = f"/images/{d['before_path']}" if d.get("before_path") else ""
    d["after_url"] = f"/images/{d['after_path']}" if d.get("after_path") else ""
    return d


@app.get("/health")
def health():
    return {"ok": True}


@app.post("/api/upload")
async def upload(
    background_tasks: BackgroundTasks,
    image: UploadFile = File(...),
    name: str = Form(...),
    height: int = Form(...),
):
    suffix = pathlib.Path(image.filename or "").suffix.lower()
    if suffix not in ALLOWED_SUFFIXES:
        suffix = ".jpg"

    conn = connect()
    # ファイル名に id を使いたいので、先に行を作って id を確保してから保存する
    cur = conn.execute(
        'INSERT INTO persons(name, height, status) VALUES(?, ?, ?)',
        (name, height, "pending"),
    )
    person_id = cur.lastrowid
    conn.commit()
    if person_id is None:
        raise HTTPException(status_code=500, detail="failed to insert person")

    before_rel = f"before/{person_id}{suffix}"
    with (IMAGES / before_rel).open("wb") as f:
        shutil.copyfileobj(image.file, f)

    _touch(conn, person_id, before_path=before_rel)
    conn.close()

    # レスポンスを返してから画像処理を走らせる (数秒〜十数秒かかるため)
    background_tasks.add_task(run_pipeline, person_id)

    return {"ok": True, "id": person_id, "status": "pending"}


@app.get("/api/persons")
def list_persons():
    """Unity 用。処理が完走した行だけ返す。"""
    conn = connect()
    rows = conn.execute(
        "SELECT * FROM persons WHERE status='done' ORDER BY id"
    ).fetchall()
    conn.close()

    # Unity の JsonUtility はトップレベルの配列を読めないので dict で包む
    return {"persons": [to_json(r) for r in rows]}


@app.get("/api/persons/{person_id}")
def get_person(person_id: int):
    """撮影ページが処理の完了を待つのに使う。"""
    conn = connect()
    row = conn.execute('SELECT * FROM persons WHERE id=?', (person_id,)).fetchone()
    conn.close()

    if row is None:
        raise HTTPException(status_code=404, detail="person not found")
    return to_json(row)


@app.get("/dbtest")
def dbtest():
    """デバッグ用。status に関係なく全件返す。"""
    conn = connect()
    rows = conn.execute('SELECT * FROM persons ORDER BY id').fetchall()
    conn.close()
    return {"persons": [to_json(r) for r in rows]}


@app.get("/", response_class=HTMLResponse)
def page():
    return (IMAGE_PROCESS_DIR / "website.html").read_text(encoding="utf-8")
