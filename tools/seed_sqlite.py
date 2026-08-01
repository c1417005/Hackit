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
    attack     INTEGER NOT NULL,
    defense    INTEGER NOT NULL,
    speed      INTEGER NOT NULL,
    reach      REAL    NOT NULL,
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

# attack + defense + speed = 120 になるよう配る。reach は 0.8〜1.5。
SWORDS = [
    ("seed-0001", "たけしの剣",   45, 35, 40, 1.30),
    ("seed-0002", "ゆうこの剣",   38, 48, 34, 1.00),
    ("seed-0003", "けんたの剣",   42, 33, 45, 1.10),
    ("seed-0004", "みさきの剣",   56, 38, 26, 1.45),
    ("seed-0005", "しょうごの剣", 30, 55, 35, 0.85),
    ("seed-0006", "あやのの剣",   47, 32, 41, 1.20),
    ("seed-0007", "だいちの剣",   60, 30, 30, 1.50),
    ("seed-0008", "りんの剣",     26, 26, 68, 0.80),
]


def main() -> None:
    os.makedirs(os.path.dirname(DB_PATH), exist_ok=True)
    if os.path.exists(DB_PATH):
        os.remove(DB_PATH)

    connection = sqlite3.connect(DB_PATH)
    try:
        connection.executescript(SCHEMA)

        for index, (sword_id, name, attack, defense, speed, reach) in enumerate(SWORDS):
            # created_at をずらして order by が意味を持つようにしておく
            created_at = f"2026-08-01T12:{index:02d}:00Z"
            connection.execute(
                "INSERT INTO swords (id, name, image_url, attack, defense, speed, reach, created_at)"
                " VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                (sword_id, name, "", attack, defense, speed, reach, created_at),
            )

        connection.commit()

        total = connection.execute("SELECT COUNT(*) FROM swords").fetchone()[0]
        print(f"{DB_PATH}")
        print(f"swords {total} 件")
        for row in connection.execute(
            "SELECT id, name, attack, defense, speed, reach FROM swords ORDER BY created_at DESC"
        ):
            print("  ", row, "合計", row[2] + row[3] + row[4])
    finally:
        connection.close()


if __name__ == "__main__":
    main()
