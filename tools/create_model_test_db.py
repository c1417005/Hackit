"""3Dモデル生成の結合テスト用 test.db を作る。

背景透明のTポーズPNGも標準ライブラリだけで生成し、
ステータスと一緒にswords.image BLOBへ保存する。
"""

from pathlib import Path
import sqlite3
import struct
import zlib


REPOSITORY_ROOT = Path(__file__).resolve().parent.parent
DB_PATH = REPOSITORY_ROOT / "test.db"

WIDTH = 384
HEIGHT = 512

SCHEMA = """
DROP TABLE IF EXISTS swords;
DROP TABLE IF EXISTS matches;

CREATE TABLE swords (
    id         TEXT PRIMARY KEY,
    name       TEXT    NOT NULL,
    image_url  TEXT    NOT NULL,
    image      BLOB    NOT NULL,
    attack     INTEGER NOT NULL,
    defense    INTEGER NOT NULL,
    speed      INTEGER NOT NULL,
    height_cm  REAL    NOT NULL,
    created_at TEXT    NOT NULL,
    CHECK (attack + defense + speed = 120)
);

CREATE TABLE matches (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    winner_id  TEXT NOT NULL,
    loser_id   TEXT NOT NULL,
    created_at TEXT NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%SZ', 'now'))
);

CREATE INDEX idx_test_swords_created_at ON swords (created_at DESC);
"""

SWORDS = [
    {
        "id": "model-test-small",
        "name": "小柄テストの剣",
        "height_cm": 155.0,
        "attack": 34,
        "defense": 50,
        "speed": 36,
        "shirt": (45, 150, 245, 255),
        "pants": (30, 55, 105, 255),
        "body_width": 58,
        "created_at": "2026-08-02T12:00:00Z",
    },
    {
        "id": "model-test-medium",
        "name": "標準テストの剣",
        "height_cm": 170.0,
        "attack": 45,
        "defense": 35,
        "speed": 40,
        "shirt": (35, 190, 105, 255),
        "pants": (45, 70, 80, 255),
        "body_width": 66,
        "created_at": "2026-08-02T12:01:00Z",
    },
    {
        "id": "model-test-large",
        "name": "長身テストの剣",
        "height_cm": 185.0,
        "attack": 56,
        "defense": 30,
        "speed": 34,
        "shirt": (235, 90, 65, 255),
        "pants": (75, 45, 55, 255),
        "body_width": 76,
        "created_at": "2026-08-02T12:02:00Z",
    },
]


def png_chunk(kind: bytes, data: bytes) -> bytes:
    return (
        struct.pack(">I", len(data))
        + kind
        + data
        + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    )


def make_tpose_png(shirt, pants, body_width: int) -> bytes:
    """384x512の背景透明TポーズPNGを生成する。"""
    pixels = bytearray(WIDTH * HEIGHT * 4)

    def set_pixel(x: int, y: int, color) -> None:
        if 0 <= x < WIDTH and 0 <= y < HEIGHT:
            offset = (y * WIDTH + x) * 4
            pixels[offset:offset + 4] = bytes(color)

    def rectangle(x0: int, y0: int, x1: int, y1: int, color) -> None:
        for y in range(max(0, y0), min(HEIGHT, y1)):
            for x in range(max(0, x0), min(WIDTH, x1)):
                set_pixel(x, y, color)

    def ellipse(cx: int, cy: int, rx: int, ry: int, color) -> None:
        for y in range(max(0, cy - ry), min(HEIGHT, cy + ry + 1)):
            normalized_y = (y - cy) / max(1, ry)
            for x in range(max(0, cx - rx), min(WIDTH, cx + rx + 1)):
                normalized_x = (x - cx) / max(1, rx)
                if normalized_x * normalized_x + normalized_y * normalized_y <= 1.0:
                    set_pixel(x, y, color)

    center = WIDTH // 2
    skin = (238, 184, 148, 255)
    hair = (45, 32, 28, 255)
    shoes = (30, 30, 35, 255)

    # 頭。上下に十分な透明余白を残す。
    ellipse(center, 75, 34, 35, skin)
    ellipse(center, 57, 34, 18, hair)
    rectangle(center - 10, 105, center + 10, 126, skin)

    # Tポーズの腕。胴と接続し、両端に手を付ける。
    arm_top = 132
    arm_bottom = 168
    hand_left = 66
    hand_right = WIDTH - 66
    rectangle(hand_left, arm_top, hand_right, arm_bottom, shirt)
    ellipse(hand_left, (arm_top + arm_bottom) // 2, 18, 19, skin)
    ellipse(hand_right, (arm_top + arm_bottom) // 2, 18, 19, skin)

    # 胴体。
    half_body = body_width // 2
    rectangle(center - half_body, 122, center + half_body, 300, shirt)
    ellipse(center, 126, half_body, 18, shirt)

    # 腰と脚。輪郭が途切れないように少し重ねる。
    rectangle(center - half_body, 288, center + half_body, 325, pants)
    leg_width = max(25, body_width // 3)
    gap = 5
    rectangle(center - gap - leg_width, 315, center - gap, 458, pants)
    rectangle(center + gap, 315, center + gap + leg_width, 458, pants)
    ellipse(center - gap - leg_width // 2, 458, leg_width // 2, 14, shoes)
    ellipse(center + gap + leg_width // 2, 458, leg_width // 2, 14, shoes)

    raw_scanlines = bytearray()
    row_size = WIDTH * 4
    for y in range(HEIGHT):
        raw_scanlines.append(0)  # PNG filter: None
        start = y * row_size
        raw_scanlines.extend(pixels[start:start + row_size])

    signature = b"\x89PNG\r\n\x1a\n"
    header = struct.pack(">IIBBBBB", WIDTH, HEIGHT, 8, 6, 0, 0, 0)
    return (
        signature
        + png_chunk(b"IHDR", header)
        + png_chunk(b"IDAT", zlib.compress(bytes(raw_scanlines), 9))
        + png_chunk(b"IEND", b"")
    )


def main() -> None:
    with sqlite3.connect(DB_PATH) as connection:
        connection.executescript(SCHEMA)

        for sword in SWORDS:
            png = make_tpose_png(
                sword["shirt"], sword["pants"], sword["body_width"]
            )
            connection.execute(
                "INSERT INTO swords "
                "(id, name, image_url, image, attack, defense, speed, height_cm, created_at) "
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                (
                    sword["id"],
                    sword["name"],
                    f"/swords/{sword['id']}/image",
                    png,
                    sword["attack"],
                    sword["defense"],
                    sword["speed"],
                    sword["height_cm"],
                    sword["created_at"],
                ),
            )

    print(f"created: {DB_PATH}")
    with sqlite3.connect(DB_PATH) as connection:
        for row in connection.execute(
            "SELECT id, name, length(image), attack, defense, speed, height_cm "
            "FROM swords ORDER BY created_at"
        ):
            print(row)


if __name__ == "__main__":
    main()
