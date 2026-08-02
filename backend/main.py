from fastapi import FastAPI, UploadFile, File, Form, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import HTMLResponse, Response
import pathlib, shutil
import sqlite3

# /=== DB部
BASE_DIR = pathlib.Path(__file__).resolve().parent.parent
dbname = BASE_DIR / "my.db"
test_dbname = BASE_DIR / "test.db"


def init_db():
    conn = sqlite3.connect(dbname)
    cur = conn.cursor()

    cur.execute(
        'CREATE TABLE IF NOT EXISTS persons('
        'id INTEGER PRIMARY KEY AUTOINCREMENT,'
        'name TEXT,'
        'height INTEGER,'
        'speed INTEGER,'
        'attack INTEGER'
        ')'
    )

    # 接続テスト用のダミーデータ (テーブルが空のときだけ入れる)
    cur.execute('SELECT COUNT(*) FROM persons')
    if cur.fetchone()[0] == 0:
        cur.executemany(
            'INSERT INTO persons(name, height, speed, attack) VALUES(?, ?, ?, ?)',
            [
                ("テスト太郎", 170, 12, 34),
                ("テスト花子", 160, 25, 18),
                ("ずんだもん", 150, 40, 5),
            ],
        )

    conn.commit()
    conn.close()


init_db()
# ===/


app = FastAPI()
app.add_middleware(
    CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"]
)

SAVE = BASE_DIR / "images"
SAVE.mkdir(exist_ok=True)
state = {"version": 0}


@app.get("/health")
def health():
    return {"ok": True}


@app.get("/dbtest")
def dbtest():
    conn = sqlite3.connect(dbname)
    conn.row_factory = sqlite3.Row
    rows = conn.execute(
        "SELECT id, name, height, speed, attack FROM persons ORDER BY id"
    ).fetchall()
    conn.close()

    # Unity の JsonUtility はトップレベルの配列を読めないので dict で包む
    return {"persons": [dict(r) for r in rows]}


@app.get("/swords")
def swords():
    if not test_dbname.exists():
        raise HTTPException(status_code=503, detail="test.db is missing")

    conn = sqlite3.connect(test_dbname)
    conn.row_factory = sqlite3.Row
    rows = conn.execute(
        "SELECT id, name, image_url, attack, defense, speed, height_cm, created_at "
        "FROM swords ORDER BY created_at DESC"
    ).fetchall()
    conn.close()

    return [
        {
            "id": row["id"],
            "name": row["name"],
            "image_url": row["image_url"],
            "stats": {
                "attack": row["attack"],
                "defense": row["defense"],
                "speed": row["speed"],
                "height_cm": row["height_cm"],
            },
            "created_at": row["created_at"],
        }
        for row in rows
    ]


@app.get("/swords/{sword_id}/image")
def sword_image(sword_id: str):
    if not test_dbname.exists():
        raise HTTPException(status_code=503, detail="test.db is missing")

    conn = sqlite3.connect(test_dbname)
    row = conn.execute(
        "SELECT image FROM swords WHERE id = ?", (sword_id,)
    ).fetchone()
    conn.close()

    if not row or not row[0]:
        raise HTTPException(status_code=404, detail="image not found")
    return Response(content=row[0], media_type="image/png")


@app.post("/api/upload")
async def upload(image: UploadFile = File(...), player: str = Form("1")):
    with (SAVE / f"p{player}.png").open("wb") as f:
        shutil.copyfileobj(image.file, f)
    state["version"] += 1
    return {"ok": True, "version": state["version"]}


@app.get("/", response_class=HTMLResponse)
def page():
    return (BASE_DIR / "image_process" / "website.html").read_text(encoding="utf-8")
