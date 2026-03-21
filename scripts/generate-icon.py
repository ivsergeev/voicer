#!/usr/bin/env python3
"""
Generate Voicer and OpenVoicer application icons from scratch using Pillow.
Creates a clean microphone icon suitable for app icons, tray, and installers.
Outputs: installer/icons/icon-{1024,512,256}.png, installer/icons/voicer.ico,
         installer/icons/openvoicer.ico
"""

import os

from PIL import Image, ImageDraw, ImageFont

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR = os.path.dirname(SCRIPT_DIR)


def draw_microphone_icon(size: int) -> Image.Image:
    """Draw a clean microphone icon on a rounded-square background."""
    s = size
    # Use 4x supersampling for smooth anti-aliasing
    ss = s * 4
    img = Image.new("RGBA", (ss, ss), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # --- Background ---
    bg_color = (64, 72, 140)
    cr = int(ss * 0.22)
    draw.rounded_rectangle([(0, 0), (ss - 1, ss - 1)], radius=cr, fill=bg_color)

    # Top highlight
    overlay = Image.new("RGBA", (ss, ss), (0, 0, 0, 0))
    ov_draw = ImageDraw.Draw(overlay)
    ov_draw.rounded_rectangle(
        [(0, 0), (ss - 1, int(ss * 0.38))],
        radius=cr,
        fill=(255, 255, 255, 18),
    )
    img = Image.alpha_composite(img, overlay)
    draw = ImageDraw.Draw(img)

    # --- Drawing params ---
    cx = ss / 2
    white = (255, 255, 255)
    stroke = max(int(ss * 0.035), 2)

    # Center the mic group vertically
    y_off = ss * 0.19

    # --- Capsule ---
    mic_w = ss * 0.25
    mic_h = ss * 0.35
    mic_top = y_off
    mic_bot = mic_top + mic_h

    draw.rounded_rectangle(
        [(cx - mic_w / 2, mic_top), (cx + mic_w / 2, mic_bot)],
        radius=int(mic_w / 2),
        fill=white,
    )

    # --- U-arc wrapping lower part of capsule ---
    arc_w = ss * 0.42
    arc_top = mic_top + mic_h * 0.30
    arc_bot = mic_bot + ss * 0.12

    draw.arc(
        [(cx - arc_w / 2, arc_top), (cx + arc_w / 2, arc_bot)],
        start=0,
        end=180,
        fill=white,
        width=stroke,
    )

    # --- Stem ---
    stem_top = (arc_top + arc_bot) / 2
    stem_h = ss * 0.09
    stem_bot = stem_top + stem_h

    draw.rectangle(
        [(cx - stroke / 2, stem_top), (cx + stroke / 2, stem_bot)],
        fill=white,
    )

    # --- Base ---
    base_w = ss * 0.14
    draw.rounded_rectangle(
        [(cx - base_w / 2, stem_bot), (cx + base_w / 2, stem_bot + stroke)],
        radius=max(int(stroke * 0.4), 1),
        fill=white,
    )

    # Downsample with high-quality Lanczos
    img = img.resize((s, s), Image.LANCZOS)
    return img


def main():
    print("=== Generating Voicer icons ===")

    icons_dir = os.path.join(ROOT_DIR, "installer", "icons")
    os.makedirs(icons_dir, exist_ok=True)

    targets = [
        ("1024x1024 (macOS .icns source)", 1024, os.path.join(icons_dir, "icon-1024.png")),
        ("512x512", 512, os.path.join(icons_dir, "icon-512.png")),
        ("256x256 (Linux hicolor)", 256, os.path.join(icons_dir, "icon-256.png")),
    ]

    icons = {}
    for label, sz, path in targets:
        icon = draw_microphone_icon(sz)
        icon.save(path, "PNG", optimize=True)
        icons[sz] = icon
        kb = os.path.getsize(path) // 1024
        print(f"  {label}: {path} ({kb} KB)")

    # --- Generate .ico for Windows (contains 256, 48, 32, 16) ---
    print("\n=== Generating Windows .ico ===")
    ico_path = os.path.join(icons_dir, "voicer.ico")
    ico_sizes = [256, 48, 32, 16]
    ico_images = []
    for sz in ico_sizes:
        if sz in icons:
            ico_images.append(icons[sz].copy())
        else:
            ico_images.append(draw_microphone_icon(sz))

    # Pillow saves .ico with the first image as the main one
    ico_images[0].save(
        ico_path, format="ICO",
        sizes=[(img.width, img.height) for img in ico_images],
        append_images=ico_images[1:],
    )
    kb = os.path.getsize(ico_path) // 1024
    print(f"  Windows .ico: {ico_path} ({kb} KB)")

    # --- Generate OpenVoicer icon ---
    print("\n=== Generating OpenVoicer icons ===")
    ov_icons = {}
    for sz in [256, 48, 32, 16]:
        ov_icons[sz] = draw_openvoicer_icon(sz)

    ov_ico_path = os.path.join(icons_dir, "openvoicer.ico")
    ov_ico_images = [ov_icons[sz] for sz in [256, 48, 32, 16]]
    ov_ico_images[0].save(
        ov_ico_path, format="ICO",
        sizes=[(img.width, img.height) for img in ov_ico_images],
        append_images=ov_ico_images[1:],
    )
    kb = os.path.getsize(ov_ico_path) // 1024
    print(f"  Windows .ico: {ov_ico_path} ({kb} KB)")

    print("\nDone!")


def draw_openvoicer_icon(size: int) -> Image.Image:
    """Draw an OpenVoicer icon — 'OV' text on a blue rounded-square background."""
    s = size
    ss = s * 4  # 4x supersampling
    img = Image.new("RGBA", (ss, ss), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)

    # --- Background (same blue as OpenVoicer tray: #3B82F6) ---
    bg_color = (59, 130, 246)
    cr = int(ss * 0.22)
    draw.rounded_rectangle([(0, 0), (ss - 1, ss - 1)], radius=cr, fill=bg_color)

    # Top highlight
    overlay = Image.new("RGBA", (ss, ss), (0, 0, 0, 0))
    ov_draw = ImageDraw.Draw(overlay)
    ov_draw.rounded_rectangle(
        [(0, 0), (ss - 1, int(ss * 0.38))],
        radius=cr,
        fill=(255, 255, 255, 25),
    )
    img = Image.alpha_composite(img, overlay)
    draw = ImageDraw.Draw(img)

    # --- Text "OV" ---
    white = (255, 255, 255)
    font_size = int(ss * 0.42)
    try:
        font = ImageFont.truetype("arial.ttf", font_size)
    except (IOError, OSError):
        try:
            font = ImageFont.truetype("Arial Bold.ttf", font_size)
        except (IOError, OSError):
            font = ImageFont.load_default()

    text = "OV"
    bbox = draw.textbbox((0, 0), text, font=font)
    tw = bbox[2] - bbox[0]
    th = bbox[3] - bbox[1]
    tx = (ss - tw) / 2 - bbox[0]
    ty = (ss - th) / 2 - bbox[1]
    draw.text((tx, ty), text, fill=white, font=font)

    # Downsample
    img = img.resize((s, s), Image.LANCZOS)
    return img


if __name__ == "__main__":
    main()
