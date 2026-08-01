from fastapi import FastAPI, UploadFile, File, Form
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import HTMLResponse
import pathlib, shutil

app = FastAPI()
app.add_middleware(CORSMiddleware, allow_origins=["*"],
                   allow_methods=["*"], allow_headers=["*"])

SAVE = pathlib.Path("images"); SAVE.mkdir(exist_ok=True)
state = {"version": 0}

@app.get("/health")
def health():
    return {"ok": True}

@app.post("/api/upload")
async def upload(file: UploadFile = File(...), player: str = Form("1")):
    with (SAVE / f"p{player}.png").open("wb") as f:
        shutil.copyfileobj(file.file, f)
    state["version"] += 1
    return {"ok": True, "version": state["version"]}

@app.get("/", response_class=HTMLResponse)
def page():
    return pathlib.Path("phone.html").read_text(encoding="utf-8")