"""
ともだちソード - 画像処理パイプライン
入力: 友達の全身写真1枚
出力:
  - <name>_sword.png   : 背景除去済みテクスチャ(ソードの見た目)
  - <name>_stats.json  : ステータス(体格比率・色属性など)

使い方:
  python friend_sword_processor.py friend.jpg
  → friend_sword.png, friend_stats.json が生成される

必要ライブラリ:
    pip install rembg opencv-python numpy
"""

import sys
import json
from pathlib import Path
import os

BASE_DIR = Path(__file__).resolve().parent
INPUT_PATH = BASE_DIR / "IMG_4411.jpg"

import cv2
import numpy as np
from PIL import Image
# rembg is used to obtain a foreground alpha mask
from rembg import remove
# 色相(Hue, 0-179) → 属性の対応表。プロジェクトに合わせて調整してOK
HUE_ATTRIBUTE_TABLE = [
    (0, 10, "炎"),
    (10, 25, "土"),
    (25, 35, "光"),
    (35, 85, "森"),
    (85, 130, "氷"),
    (130, 160, "闇"),
    (160, 180, "炎"),  # 赤の折り返し
]


def hue_to_attribute(hue: float) -> str:
    for lo, hi, attr in HUE_ATTRIBUTE_TABLE:
        if lo <= hue < hi:
            return attr
    return "無"


def estimate_physique(image_path: Path):
    """Estimate shoulder/height ratio and torso box using rembg foreground mask.

    Returns: (shoulder_height_ratio, (x1,y1,x2,y2), img_bgr, {})
    """
    img_bgr = cv2.imread(str(image_path))
    if img_bgr is None:
        raise FileNotFoundError(f"画像が読み込めません: {image_path}")

    h, w = img_bgr.shape[:2]

    # Use rembg to get RGBA and alpha mask
    pil = Image.fromarray(cv2.cvtColor(img_bgr, cv2.COLOR_BGR2RGB))
    rgba = remove(pil)
    rgba_np = np.array(rgba)
    if rgba_np.shape[2] == 4:
        alpha = rgba_np[:, :, 3]
    else:
        gray = cv2.cvtColor(img_bgr, cv2.COLOR_BGR2GRAY)
        alpha = np.where(gray < 250, 255, 0).astype(np.uint8)

    ys, xs = np.where(alpha > 0)
    if ys.size == 0:
        raise ValueError("人物の領域が検出できませんでした（背景除去失敗）。")

    y1, y2 = ys.min(), ys.max()
    x1, x2 = xs.min(), xs.max()
    bw = x2 - x1
    bh = y2 - y1

    # approximate shoulder width by horizontal span at 25% down from top of bbox
    sample_y = y1 + int(0.25 * bh)
    row = alpha[sample_y, :]
    cols = np.where(row > 0)[0]
    if cols.size == 0:
        shoulder_width_px = bw
    else:
        shoulder_width_px = cols.max() - cols.min()

    height_px = max(bh, 1e-6)
    shoulder_height_ratio = shoulder_width_px / height_px

    # torso box: from 20% to 60% of foreground bbox height
    ty1 = y1 + int(0.20 * bh)
    ty2 = y1 + int(0.60 * bh)
    ty1 = max(0, ty1)
    ty2 = min(h, ty2)

    print(f"[OK] 体格推定(簡易): 肩幅/身長比={shoulder_height_ratio:.3f}, 胴体矩形=({x1},{ty1})-({x2},{ty2})")
    return shoulder_height_ratio, (x1, ty1, x2, ty2), img_bgr, {}


def remove_background(image_path: Path, out_path: Path) -> Image.Image:
    """rembgで背景除去し、透過PNGとして保存"""
    img = Image.open(image_path).convert("RGB")
    result = remove(img)  # RGBAが返る
    result.save(out_path)
    print(f"[OK] 背景除去 -> {out_path}")
    return result


    # legacy mediapipe-based functions removed; use rembg-based estimate_physique instead


def extract_torso_color(img_bgr, torso_box):
    """胴体部分をHSV平均で色抽出し、属性に変換"""
    x1, y1, x2, y2 = torso_box
    torso_bgr = img_bgr[y1:y2, x1:x2]

    if torso_bgr.size == 0:
        raise ValueError("胴体領域が空です。矩形座標を確認してください。")

    torso_hsv = cv2.cvtColor(torso_bgr, cv2.COLOR_BGR2HSV)
    avg_hsv = torso_hsv.reshape(-1, 3).mean(axis=0)
    hue, sat, val = float(avg_hsv[0]), float(avg_hsv[1]), float(avg_hsv[2])

    attribute = hue_to_attribute(hue)
    print(f"[OK] 色抽出: H={hue:.1f} S={sat:.1f} V={val:.1f} -> 属性={attribute}")
    return hue, sat, val, attribute





def ratio_to_stats(shoulder_height_ratio: float, sat: float, val: float):
    """
    体格比率・彩度・明度からゲーム用ステータスに変換する。
    数値の対応関係はプロジェクトの仕様に合わせて自由に調整してください。
    """
    # 肩幅/身長比が大きいほど「がっしり」→ 攻撃力寄り
    # 比が小さいほど「細身」→ スピード寄り
    attack = round(50 + shoulder_height_ratio * 300)
    speed = round(150 - shoulder_height_ratio * 250)

    # 彩度が高い(鮮やかな服)ほど攻撃力ボーナス、明度が高いほど防御ボーナス(例)
    attack += round(sat / 255 * 20)
    defense = round(30 + val / 255 * 40)

    stats = {
        "attack": max(1, attack),
        "speed": max(1, speed),
        "defense": max(1, defense),
    }
    print(f"[OK] ステータス変換: {stats}")
    return stats


def process(image_path: str):
    image_path = Path(image_path)
    if not image_path.exists():
        raise FileNotFoundError(f"入力画像が見つかりません: {image_path}")
    
    stem = image_path.stem
    out_dir = image_path.parent
    texture_path = out_dir / f"{stem}_sword.png"
    stats_path = out_dir / f"{stem}_stats.json"

    # 画像処理部分を分離して呼び出す
    proc = process_image(image_path, texture_path)

    # ステータス算出を分離して呼び出す
    stats = compute_stats_from_features(proc["shoulder_height_ratio"], proc["sat"], proc["val"])

    result = {
        "name": stem,
        "texture_file": texture_path.name,
        "shoulder_height_ratio": round(proc["shoulder_height_ratio"], 4),
        "color": {
            "hue": round(proc["hue"], 1),
            "saturation": round(proc["sat"], 1),
            "value": round(proc["val"], 1),
            "attribute": proc["attribute"],
        },
        "stats": stats,
    }

    with open(stats_path, "w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    print(f"[OK] ステータスJSON -> {stats_path}")
    print("\n=== 完成データ ===")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return result


def process_image(image_path: Path, texture_out: Path):
    """画像処理パイプラインを実行して特徴量を返す。
    戻り値: dict with keys: texture_path, shoulder_height_ratio, torso_box, img_bgr, hue, sat, val, attribute
    """
    # 背景除去
    remove_background(image_path, texture_out)

    # 体格推定 (rembgベース)
    shoulder_height_ratio, torso_box, img_bgr, _ = estimate_physique(image_path)

    # 服の色抽出 (胴体矩形を使う)
    hue, sat, val, attribute = extract_torso_color(img_bgr, torso_box)

    return {
        "texture_path": texture_out,
        "shoulder_height_ratio": shoulder_height_ratio,
        "torso_box": torso_box,
        "img_bgr": img_bgr,
        "hue": hue,
        "sat": sat,
        "val": val,
        "attribute": attribute,
    }


def compute_stats_from_features(shoulder_height_ratio: float, sat: float, val: float):
    """特徴量からゲーム用ステータスを計算して返す。"""
    return ratio_to_stats(shoulder_height_ratio, sat, val)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print("使い方: python friend_sword_processor.py <IMG_4411.jpg>")
        sys.exit(1)

    try:
        process(sys.argv[1])
    except Exception as e:
        print(f"[ERROR] {e}")
        sys.exit(1)