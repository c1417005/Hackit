"""Extract clothing color from a generated person image.

Usage:
  python color_extraction.py IMG_4411_sword.png

This script detects the person with MediaPipe Pose, masks the torso region
between shoulders and hips, and computes the average clothing color.
"""

import json
import sys
from pathlib import Path
import os

import cv2
from rembg import remove
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


def _bbox_from_alpha(alpha: np.ndarray):
    ys, xs = np.where(alpha > 0)
    if ys.size == 0:
        return None
    y1, y2 = ys.min(), ys.max()
    x1, x2 = xs.min(), xs.max()
    return x1, y1, x2, y2


def estimate_torso_box_from_mask(bgr_img):
    """Estimate torso bounding box from an RGBA foreground extracted by rembg.

    Strategy:
    - Run rembg.remove to get RGBA image and alpha mask
    - Compute foreground bbox from alpha
    - Define torso as the central upper-middle portion of the bbox (approx shoulders->hips)
    """
    from PIL import Image

    pil = Image.fromarray(cv2.cvtColor(bgr_img, cv2.COLOR_BGR2RGB))
    rgba = remove(pil)
    rgba_np = np.array(rgba)
    if rgba_np.shape[2] == 4:
        alpha = rgba_np[:, :, 3]
    else:
        # fallback: treat non-white pixels as foreground
        gray = cv2.cvtColor(bgr_img, cv2.COLOR_BGR2GRAY)
        alpha = np.where(gray < 250, 255, 0).astype(np.uint8)

    bbox = _bbox_from_alpha(alpha)
    if bbox is None:
        raise ValueError("人物の領域が検出できませんでした（背景除去失敗）。")

    x1, y1, x2, y2 = bbox
    bw = x2 - x1
    bh = y2 - y1
    # Torso: from 20% down from top to 60% down (relative to foreground bbox)
    ty1 = y1 + int(0.20 * bh)
    ty2 = y1 + int(0.60 * bh)
    # Clip
    ty1 = max(0, ty1)
    ty2 = min(bgr_img.shape[0], ty2)

    return (x1, ty1, x2, ty2), alpha



def extract_clothing_color(image_path: Path):
    if not image_path.exists():
        raise FileNotFoundError(f"画像が見つかりません: {image_path}")

    img = cv2.imread(str(image_path), cv2.IMREAD_UNCHANGED)
    if img is None:
        raise FileNotFoundError(f"画像が読み込めません: {image_path}")

    if img.ndim == 2:
        raise ValueError("カラー画像を指定してください。")

    bgr = img[:, :, :3] if img.shape[2] == 4 else img

    # Use rembg to estimate torso box and alpha mask, then build mask for torso
    torso_box, alpha = estimate_torso_box_from_mask(bgr)
    x1, y1, x2, y2 = torso_box
    mask = np.zeros(bgr.shape[:2], dtype=np.uint8)
    mask[y1:y2, x1:x2] = (alpha[y1:y2, x1:x2] > 0).astype(np.uint8) * 255

    # Compute average color by averaging B,G,R channels of masked pixels
    bgr_pixels = bgr[mask == 255]
    if bgr_pixels.size == 0:
        raise ValueError("服領域の色を抽出できませんでした。マスク領域を確認してください。")

    avg_bgr = bgr_pixels.mean(axis=0)  # [B, G, R]
    avg_rgb = {"r": int(round(float(avg_bgr[2]))), "g": int(round(float(avg_bgr[1]))), "b": int(round(float(avg_bgr[0])))}

    # For attribute mapping, convert the average RGB back to HSV to get hue
    avg_pixel_bgr = np.uint8([[[avg_rgb["b"], avg_rgb["g"], avg_rgb["r"]]]])
    avg_hsv = cv2.cvtColor(avg_pixel_bgr, cv2.COLOR_BGR2HSV)[0, 0]
    hue, sat, val = float(avg_hsv[0]), float(avg_hsv[1]), float(avg_hsv[2])

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
