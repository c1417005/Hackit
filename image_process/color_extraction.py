"""Extract clothing color from a generated person image.

Usage:
  python color_extraction.py IMG_4411_sword.png

This script detects the person with MediaPipe Pose, masks the torso region
between shoulders and hips, and computes the average clothing color.
"""

import json
import sys
from pathlib import Path

import cv2
import mediapipe as mp
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


def landmark_to_pixel(landmark, width: int, height: int):
    return int(landmark.x * width), int(landmark.y * height)


def get_pose_landmarks(image_bgr):
    image_rgb = cv2.cvtColor(image_bgr, cv2.COLOR_BGR2RGB)
    with mp.solutions.pose.Pose(static_image_mode=True, model_complexity=1) as pose:
        results = pose.process(image_rgb)
    if not results.pose_landmarks:
        raise ValueError("人物のランドマークを検出できませんでした。全身が写っているか確認してください。")
    return results.pose_landmarks.landmark


def create_torso_mask(image_bgr, landmarks):
    h, w = image_bgr.shape[:2]
    left_shoulder = landmark_to_pixel(landmarks[11], w, h)
    right_shoulder = landmark_to_pixel(landmarks[12], w, h)
    left_hip = landmark_to_pixel(landmarks[23], w, h)
    right_hip = landmark_to_pixel(landmarks[24], w, h)

    polygon = np.array([
        left_shoulder,
        right_shoulder,
        right_hip,
        left_hip,
    ], dtype=np.int32)
    mask = np.zeros((h, w), dtype=np.uint8)
    cv2.fillPoly(mask, [polygon], 255)
    return mask


def extract_clothing_color(image_path: Path):
    if not image_path.exists():
        raise FileNotFoundError(f"画像が見つかりません: {image_path}")

    img = cv2.imread(str(image_path), cv2.IMREAD_UNCHANGED)
    if img is None:
        raise FileNotFoundError(f"画像が読み込めません: {image_path}")

    if img.ndim == 2:
        raise ValueError("カラー画像を指定してください。")

    bgr = img[:, :, :3] if img.shape[2] == 4 else img
    landmarks = get_pose_landmarks(bgr)
    mask = create_torso_mask(bgr, landmarks)

    hsv = cv2.cvtColor(bgr, cv2.COLOR_BGR2HSV)
    torso_pixels = hsv[mask == 255]
    if torso_pixels.size == 0:
        raise ValueError("服領域の色を抽出できませんでした。マスク領域を確認してください。")

    avg_hsv = torso_pixels.mean(axis=0)
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
