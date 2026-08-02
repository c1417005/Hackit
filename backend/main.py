from fastapi import FastAPI, UploadFile, File, Form
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import HTMLResponse
import pathlib, shutil
import sqlite3

# /=== DB部
dbname = "my.db"
conn = sqlite3.connect(dbname)
cur = conn.cursor()

cur.execute(
    'CREATE TABLE persons(' \
    'id INTEGER PRIMARY KEY AUTOINCREMENT,' \
    'name STRING,'
    'height INTEGER,'
    'speed INTEGER,'
    'attack INTEGER,'

    ')'
)

conn.close()
# ===/


app = FastAPI()
app.add_middleware(
    CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"]
)

SAVE = pathlib.Path("images")
SAVE.mkdir(exist_ok=True)
state = {"version": 0}


@app.get("/health")
def health():
    return {"ok": True}


@app.post("/api/upload")
async def upload(image: UploadFile = File(...), player: str = Form("1")):
    with (SAVE / f"p{player}.png").open("wb") as f:
        shutil.copyfileobj(image.file, f)
    state["version"] += 1
    return {"ok": True, "version": state["version"]}


@app.get("/", response_class=HTMLResponse)
def page():
    return pathlib.Path("image_process/website.html").read_text(encoding="utf-8")
