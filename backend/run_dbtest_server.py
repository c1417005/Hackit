"""UnityとSQLiteの接続確認に限定した、依存パッケージ不要のHTTPサーバー。

backend/main.py と同じ my.db / persons テーブルを使い、
GET /health と GET /dbtest だけを提供する。
"""

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
from pathlib import Path
import sqlite3


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
DB_PATH = REPOSITORY_ROOT / "my.db"


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


class DbTestHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        path = self.path.split("?", 1)[0]
        if path == "/health":
            self.send_json({"ok": True})
        elif path == "/dbtest":
            self.send_json({"persons": fetch_persons()})
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

    def log_message(self, format, *args):
        print(f"[DB Test Server] {self.address_string()} - {format % args}")


if __name__ == "__main__":
    init_db()
    server = ThreadingHTTPServer(("127.0.0.1", 8000), DbTestHandler)
    print("DB test server running on http://127.0.0.1:8000")
    print("Stop: Ctrl+C")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nDB test server stopped")
    finally:
        server.server_close()
