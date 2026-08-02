"""UnityとSQLiteの結合テスト用、依存パッケージ不要のHTTPサーバー。

backend/main.py と同じ my.db / persons テーブルを使い、
test.db からはモデル生成用の剣一覧とPNGを提供する。
"""

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import argparse
import json
from pathlib import Path
import sqlite3
from urllib.parse import unquote


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
DB_PATH = REPOSITORY_ROOT / "my.db"
TEST_DB_PATH = REPOSITORY_ROOT / "test.db"


def init_db():
    with sqlite3.connect(DB_PATH) as connection:
        cursor = connection.cursor()
        cursor.execute(
            "CREATE TABLE IF NOT EXISTS persons("
            "id INTEGER PRIMARY KEY AUTOINCREMENT,"
            "name TEXT,"
            "height INTEGER,"
            "speed INTEGER,"
            "attack INTEGER"
            ")"
        )
        cursor.execute("SELECT COUNT(*) FROM persons")
        if cursor.fetchone()[0] == 0:
            cursor.executemany(
                "INSERT INTO persons(name, height, speed, attack) VALUES(?, ?, ?, ?)",
                [
                    ("テスト太郎", 170, 12, 34),
                    ("テスト花子", 160, 25, 18),
                    ("ずんだもん", 150, 40, 5),
                ],
            )


def fetch_persons():
    with sqlite3.connect(DB_PATH) as connection:
        connection.row_factory = sqlite3.Row
        rows = connection.execute(
            "SELECT id, name, height, speed, attack FROM persons ORDER BY id"
        ).fetchall()
    return [dict(row) for row in rows]


def fetch_swords():
    if not TEST_DB_PATH.exists():
        raise FileNotFoundError(
            "test.dbがありません。tools/create_model_test_db.pyを実行してください"
        )

    with sqlite3.connect(TEST_DB_PATH) as connection:
        connection.row_factory = sqlite3.Row
        rows = connection.execute(
            "SELECT id, name, image_url, attack, defense, speed, height_cm, created_at "
            "FROM swords ORDER BY created_at DESC"
        ).fetchall()

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


def fetch_sword_image(sword_id: str):
    if not TEST_DB_PATH.exists():
        return None

    with sqlite3.connect(TEST_DB_PATH) as connection:
        row = connection.execute(
            "SELECT image FROM swords WHERE id = ?", (sword_id,)
        ).fetchone()
    return row[0] if row and row[0] else None


class DbTestHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        path = self.path.split("?", 1)[0]
        if path == "/health":
            self.send_json({"ok": True})
        elif path == "/dbtest":
            self.send_json({"persons": fetch_persons()})
        elif path == "/swords":
            try:
                self.send_json(fetch_swords())
            except FileNotFoundError as error:
                self.send_json({"error": str(error)}, status=503)
        elif path.startswith("/swords/") and path.endswith("/image"):
            sword_id = unquote(path[len("/swords/"):-len("/image")]).strip("/")
            image = fetch_sword_image(sword_id)
            if image:
                self.send_bytes(image, "image/png")
            else:
                self.send_json({"error": "Image Not Found"}, status=404)
        else:
            self.send_json({"error": "Not Found"}, status=404)

    def send_json(self, body, status=200):
        encoded = json.dumps(body, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(encoded)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(encoded)

    def send_bytes(self, body: bytes, content_type: str, status=200):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):
        print(f"[DB Test Server] {self.address_string()} - {format % args}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=8000)
    arguments = parser.parse_args()

    init_db()
    server = ThreadingHTTPServer((arguments.host, arguments.port), DbTestHandler)
    print(f"DB test server running on http://{arguments.host}:{arguments.port}")
    print("Stop: Ctrl+C")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nDB test server stopped")
    finally:
        server.server_close()
