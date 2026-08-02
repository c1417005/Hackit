"""テスト用の SQLite を作り直す。

    python tools/seed_sqlite.py

出力先は Unity の StreamingAssets。ここに置くとビルドにもそのまま入り、
Unity からは Application.streamingAssetsPath でパスが引ける。

列はデータ契約（CLAUDE.md セクション4）に合わせてある。
JSON では stats がネストしているが、SQLite では平らに持つ。
Unity 側の SwordRepository が SwordData へ組み直す。
"""

import os
import sqlite3

HERE = os.path.dirname(os.path.abspath(__file__))
DB_PATH = os.path.join(
    HERE, "..", "Hackit_tomodati-sord", "Assets", "StreamingAssets", "tomodachi_sword.db"
)
DB_PATH = os.path.normpath(DB_PATH)

SCHEMA = """
DROP TABLE IF EXISTS swords;
DROP TABLE IF EXISTS matches;

CREATE TABLE swords (
    id         TEXT PRIMARY KEY,
    name       TEXT    NOT NULL,
    image_url  TEXT    NOT NULL DEFAULT '',
    -- 切り抜き済みPNGそのもの。サーバーがここに入れても、image_url 経由でも動く
    image      BLOB,
    attack     INTEGER NOT NULL,
    defense    INTEGER NOT NULL,
    speed      INTEGER NOT NULL,
    height_cm  REAL    NOT NULL,
    created_at TEXT    NOT NULL
);

CREATE TABLE matches (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    winner_id  TEXT NOT NULL,
    loser_id   TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now'))
);

CREATE INDEX idx_swords_created_at ON swords (created_at DESC);
"""

# attack + defense + speed = 120 になるよう配る。
# 各タプル末尾の height_cm によって小・中・大の雛型が選ばれる。
SWORDS = [
    ("seed-0001", "たけしの剣",   45, 35, 40, 174.0),
    ("seed-0002", "ゆうこの剣",   38, 48, 34, 158.0),
    ("seed-0003", "けんたの剣",   42, 33, 45, 168.0),
    ("seed-0004", "みさきの剣",   56, 38, 26, 182.0),
    ("seed-0005", "しょうごの剣", 30, 55, 35, 155.0),
    ("seed-0006", "あやのの剣",   47, 32, 41, 172.0),
    ("seed-0007", "だいちの剣",   60, 30, 30, 188.0),
    ("seed-0008", "りんの剣",     26, 26, 68, 152.0),
]


def add_one(
    name: str,
    image_path: str | None = None,
    height_cm: float | None = None,
) -> None:
    """Webからのアップロードを1件ぶん再現する。錬成待ちの動作確認用。

        python tools/seed_sqlite.py --add あたらしい剣
        python tools/seed_sqlite.py --add あたらしい剣 path/to/sword.png
        python tools/seed_sqlite.py --add あたらしい剣 path/to/sword.png 172

    Unity 側は「起動時に無かった id」を新着として拾うので、
    ゲームを錬成待ちにしたままこれを叩けば検知される。
    """
    import random
    import uuid
    from datetime import datetime, timezone

    attack = random.randint(25, 60)
    defense = random.randint(25, max(26, 121 - attack - 25))
    speed = 120 - attack - defense
    if height_cm is None:
        height_cm = round(random.uniform(145.0, 190.0), 1)

    blob = None
    if image_path:
        with open(image_path, "rb") as f:
            blob = f.read()

    connection = sqlite3.connect(DB_PATH)
    try:
        connection.execute(
            "INSERT INTO swords "
            "(id, name, image_url, image, attack, defense, speed, height_cm, created_at)"
            " VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            (
                f"web-{uuid.uuid4().hex[:8]}",
                name,
                "",
                blob,
                attack,
                defense,
                speed,
                height_cm,
                datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            ),
        )
        connection.commit()
        print(f"追加: {name}  atk{attack}/def{defense}/spd{speed} "
              f"height{height_cm}cm"
              f"  image={'あり' if blob else 'なし'}")
    finally:
        connection.close()


def main() -> None:
    os.makedirs(os.path.dirname(DB_PATH), exist_ok=True)
    if os.path.exists(DB_PATH):
        os.remove(DB_PATH)

    connection = sqlite3.connect(DB_PATH)
    try:
        connection.executescript(SCHEMA)

        for index, (sword_id, name, attack, defense, speed, height_cm) in enumerate(SWORDS):
            # created_at をずらして order by が意味を持つようにしておく
            created_at = f"2026-08-01T12:{index:02d}:00Z"
            connection.execute(
                "INSERT INTO swords "
                "(id, name, image_url, image, attack, defense, speed, height_cm, created_at)"
                " VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                (sword_id, name, "", None, attack, defense, speed, height_cm, created_at),
            )

        connection.commit()

        total = connection.execute("SELECT COUNT(*) FROM swords").fetchone()[0]
        print(f"{DB_PATH}")
        print(f"swords {total} 件")
        for row in connection.execute(
            "SELECT id, name, attack, defense, speed, height_cm "
            "FROM swords ORDER BY created_at DESC"
        ):
            print("  ", row, "合計", row[2] + row[3] + row[4])
    finally:
        connection.close()


if __name__ == "__main__":
    import sys

    if len(sys.argv) > 1 and sys.argv[1] == "--add":
        add_one(sys.argv[2] if len(sys.argv) > 2 else "テストの剣",
                sys.argv[3] if len(sys.argv) > 3 else None,
                float(sys.argv[4]) if len(sys.argv) > 4 else None)
    else:
        main()
