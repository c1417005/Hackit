"""Extract clothing color from a generated person image.

Usage:
  python color_extraction.py IMG_4411_sword.png

背景除去済み画像のアルファ(人物のシルエット)を使って人物領域を切り出し、
その平均色を服の色とみなす。

もとは MediaPipe Pose の肩・腰ランドマークで胴体を囲んでいたが、
Python 3.13 で入る mediapipe には Solutions API が含まれておらず
(0.10.30 以降・1.0.0 いずれもホイールに solutions/ が無い)、
姿勢推定なしで成立する方式に置き換えた。
"""

import json
import sys
from pathlib import Path

import cv2
import numpy as np

HUE_ATTRIBUTE_TABLE = [
    (0, 10, "炎"),
    (10, 25, "土"),
    (25, 35, "光"),
    (35, 85, "森"),
    (85, 130, "氷"),
    (130, 160, "闇"),
    (160, 180, "炎"),
]


def hue_to_attribute(hue: float) -> str:
    for lo, hi, label in HUE_ATTRIBUTE_TABLE:
        if lo <= hue < hi:
            return label
    return "無"


# 人物とみなすアルファのしきい値。背景除去の境界は半透明になるので少し余裕をみる
PERSON_ALPHA_THRESHOLD = 10


def create_person_mask(image_bgr, alpha=None):
    """人物のピクセルだけを 255 にしたマスクを返す。

    背景除去済みの RGBA を渡す前提。alpha が無い画像だと背景も混ざるので、
    その場合は画像全体をそのまま対象にする。
    """
    h, w = image_bgr.shape[:2]
    if alpha is None:
        return np.full((h, w), 255, dtype=np.uint8)

    mask = np.zeros((h, w), dtype=np.uint8)
    mask[alpha > PERSON_ALPHA_THRESHOLD] = 255
    return mask


def extract_clothing_color(image_path: Path):
    if not image_path.exists():
        raise FileNotFoundError(f"画像が見つかりません: {image_path}")

    img = cv2.imread(str(image_path), cv2.IMREAD_UNCHANGED)
    if img is None:
        raise FileNotFoundError(f"画像が読み込めません: {image_path}")

    if img.ndim == 2:
        raise ValueError("カラー画像を指定してください。")

    has_alpha = img.shape[2] == 4
    bgr = img[:, :, :3] if has_alpha else img
    alpha = img[:, :, 3] if has_alpha else None
    mask = create_person_mask(bgr, alpha)

    hsv = cv2.cvtColor(bgr, cv2.COLOR_BGR2HSV)
    person_pixels = hsv[mask == 255]
    if person_pixels.size == 0:
        raise ValueError("人物領域が見つかりませんでした。背景除去に失敗している可能性があります。")

    avg_hsv = person_pixels.mean(axis=0)
    hue, sat, val = float(avg_hsv[0]), float(avg_hsv[1]), float(avg_hsv[2])

    avg_pixel = np.uint8([[[hue, sat, val]]])
    avg_bgr = cv2.cvtColor(avg_pixel, cv2.COLOR_HSV2BGR)[0, 0]
    avg_rgb = {"r": int(avg_bgr[2]), "g": int(avg_bgr[1]), "b": int(avg_bgr[0])}

    return {
        "image": str(image_path.name),
        "hue": round(hue, 1),
        "saturation": round(sat, 1),
        "value": round(val, 1),
        "rgb": avg_rgb,
        "attribute": hue_to_attribute(hue),
    }


def main():
    if len(sys.argv) != 2:
        print("使い方: python color_extraction.py <IMG_4411_sword.png>")
        sys.exit(1)

    image_path = Path(sys.argv[1])
    result = extract_clothing_color(image_path)
    result_path = image_path.with_name(image_path.stem + "_clothing_color.json")
    with result_path.open("w", encoding="utf-8") as f:
        json.dump(result, f, ensure_ascii=False, indent=2)

    print(f"[OK] 服の色を抽出しました: {result_path}")
    print(json.dumps(result, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
