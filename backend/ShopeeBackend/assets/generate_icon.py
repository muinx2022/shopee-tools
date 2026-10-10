"""
ShopeeBackend App Icon Generator
Generates high-resolution app-icon.png and multi-resolution app-icon.ico
Design: Server / Database Stack with Shopee Brand Styling
"""

import os
import math
from PIL import Image, ImageDraw, ImageFilter

def draw_database_symbol(draw, cx, cy, w, h, layers=3):
    """Draws a 3D-styled database cylinder stack with layers."""
    layer_h = h // (layers + 1)
    for i in range(layers):
        ly = cy - h // 2 + i * (layer_h + 4)
        # Cylinder body
        draw.rounded_rectangle(
            [cx - w // 2, ly, cx + w // 2, ly + layer_h + 8],
            radius=layer_h // 2,
            fill=(238, 77, 45, 255),
            outline=(255, 140, 100, 240),
            width=3
        )
        # Top disc highlight
        draw.ellipse(
            [cx - w // 2 + 3, ly + 2, cx + w // 2 - 3, ly + layer_h],
            fill=(255, 120, 80, 255)
        )
        # Inner gloss
        draw.arc(
            [cx - w // 2 + 6, ly + 3, cx + w // 2 - 6, ly + layer_h - 1],
            start=180, end=360,
            fill=(255, 220, 200, 240),
            width=2
        )

def generate_icons(output_dir: str):
    os.makedirs(output_dir, exist_ok=True)
    W, H = 1024, 1024
    canvas = Image.new('RGBA', (W, H), (0, 0, 0, 0))

    # 1. Drop shadow for squircle
    shadow = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    s_draw = ImageDraw.Draw(shadow)
    s_box = [96, 120, 928, 952]
    s_radius = 185
    s_draw.rounded_rectangle(s_box, radius=s_radius, fill=(0, 0, 0, 150))
    shadow = shadow.filter(ImageFilter.GaussianBlur(38))
    canvas.paste(shadow, (0, 0), shadow)

    # 2. Main Squircle Body (Dark sleek background #1C1D24 -> #0F1014)
    squircle = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    sq_draw = ImageDraw.Draw(squircle)
    box = [96, 96, 928, 928]

    for y in range(box[1], box[3]):
        ratio = (y - box[1]) / (box[3] - box[1])
        # Dark charcoal gradient
        r = int(0x23 * (1 - ratio) + 0x0F * ratio)
        g = int(0x25 * (1 - ratio) + 0x10 * ratio)
        b = int(0x2E * (1 - ratio) + 0x14 * ratio)
        sq_draw.line([(box[0], y), (box[2], y)], fill=(r, g, b, 255))

    sq_mask = Image.new('L', (W, H), 0)
    sqm_draw = ImageDraw.Draw(sq_mask)
    sqm_draw.rounded_rectangle(box, radius=s_radius, fill=255)
    squircle.putalpha(sq_mask)
    canvas.paste(squircle, (0, 0), squircle)

    draw = ImageDraw.Draw(canvas)

    # 3. Outer Shopee Orange Border & Subtle Inner Glow
    # Outer accent border: #EE4D2D
    draw.rounded_rectangle(box, radius=s_radius, outline=(238, 77, 45, 235), width=8)

    # Inner subtle metallic bevel
    inner_box = [box[0] + 6, box[1] + 6, box[2] - 6, box[3] - 6]
    draw.rounded_rectangle(inner_box, radius=s_radius - 6, outline=(255, 255, 255, 30), width=2)

    # Top ambient orange glow
    glow = Image.new('RGBA', (W, H), (0, 0, 0, 0))
    g_draw = ImageDraw.Draw(glow)
    g_draw.ellipse([262, 50, 762, 350], fill=(238, 77, 45, 35))
    glow = glow.filter(ImageFilter.GaussianBlur(50))
    canvas.paste(glow, (0, 0), glow)

    # Re-acquire draw after paste
    draw = ImageDraw.Draw(canvas)

    # 4. Server Rack Dimensions
    tray_w = 600
    tray_h = 112
    tray_gap = 36
    tray_left = (W - tray_w) // 2   # 212
    tray_right = tray_left + tray_w  # 812

    total_h = 3 * tray_h + 2 * tray_gap # 336 + 72 = 408
    start_y = 512 - total_h // 2 + 30   # 338

    # 5. Database Cylinder Header (above server rack)
    db_cx = W // 2
    db_cy = start_y - 82
    draw_database_symbol(draw, db_cx, db_cy, w=96, h=52, layers=3)

    # Data stream connection lines between database and top server tray
    draw.line([(db_cx, db_cy + 28), (db_cx, start_y - 2)], fill=(238, 77, 45, 180), width=3)
    draw.ellipse([db_cx - 4, start_y - 6, db_cx + 4, start_y + 2], fill=(0, 230, 118, 255))

    # 6. Rack Vertical Side Mount Rails
    rail_l = tray_left - 26
    rail_r = tray_right + 26
    rail_top = start_y - 24
    rail_bottom = start_y + total_h + 24

    draw.rounded_rectangle([rail_l - 12, rail_top, rail_l + 4, rail_bottom], radius=8, fill=(35, 37, 45, 255), outline=(60, 64, 76, 255), width=2)
    draw.rounded_rectangle([rail_r - 4, rail_top, rail_r + 12, rail_bottom], radius=8, fill=(35, 37, 45, 255), outline=(60, 64, 76, 255), width=2)

    # Hex screws on rails
    for i in range(3):
        cy = start_y + i * (tray_h + tray_gap) + tray_h // 2
        for rx in [rail_l - 4, rail_r + 4]:
            draw.ellipse([rx - 6, cy - 6, rx + 6, cy + 6], fill=(130, 136, 150, 255))
            draw.ellipse([rx - 3, cy - 3, rx + 3, cy + 3], fill=(30, 32, 38, 255))

    # 7. Draw 3 Server Trays (Chassis)
    for i in range(3):
        ty = start_y + i * (tray_h + tray_gap)
        by = ty + tray_h

        # Tray drop shadow
        t_shadow = Image.new('RGBA', (W, H), (0, 0, 0, 0))
        ts_draw = ImageDraw.Draw(t_shadow)
        ts_draw.rounded_rectangle([tray_left, ty + 6, tray_right, by + 10], radius=18, fill=(0, 0, 0, 100))
        t_shadow = t_shadow.filter(ImageFilter.GaussianBlur(10))
        canvas.paste(t_shadow, (0, 0), t_shadow)

        # Tray gradient body: Shopee Orange (#FF6B3D to #EE4D2D)
        tray_img = Image.new('RGBA', (W, H), (0, 0, 0, 0))
        ti_draw = ImageDraw.Draw(tray_img)
        for y in range(ty, by):
            ratio = (y - ty) / tray_h
            r = int(255 * (1 - ratio) + 238 * ratio)
            g = int(107 * (1 - ratio) + 77 * ratio)
            b = int(61 * (1 - ratio) + 45 * ratio)
            ti_draw.line([(tray_left, y), (tray_right, y)], fill=(r, g, b, 255))

        t_mask = Image.new('L', (W, H), 0)
        tm_draw = ImageDraw.Draw(t_mask)
        tm_draw.rounded_rectangle([tray_left, ty, tray_right, by], radius=18, fill=255)
        tray_img.putalpha(t_mask)
        canvas.paste(tray_img, (0, 0), tray_img)

        # Tray border and highlight
        draw = ImageDraw.Draw(canvas)
        draw.rounded_rectangle([tray_left, ty, tray_right, by], radius=18, outline=(255, 160, 130, 220), width=3)
        draw.line([(tray_left + 18, ty + 2), (tray_right - 18, ty + 2)], fill=(255, 215, 195, 230), width=2)

        # Caddy / Drive Bays (Left section)
        c_y1 = ty + 18
        c_y2 = by - 18
        c_w = 84
        c_gap = 16
        c_start_x = tray_left + 26

        for d in range(3):
            cx1 = c_start_x + d * (c_w + c_gap)
            cx2 = cx1 + c_w
            # Dark caddy bay
            draw.rounded_rectangle([cx1, c_y1, cx2, c_y2], radius=10, fill=(24, 25, 32, 255), outline=(180, 50, 25, 180), width=2)
            # Latch handle
            hx1 = cx1 + 12
            hx2 = cx2 - 12
            hy1 = c_y1 + 16
            hy2 = c_y2 - 16
            draw.rounded_rectangle([hx1, hy1, hx2, hy2], radius=6, fill=(42, 44, 54, 255))
            # Handle grip slit
            draw.line([(hx1 + 8, (hy1 + hy2) // 2), (hx2 - 8, (hy1 + hy2) // 2)], fill=(18, 19, 24, 255), width=3)
            # Disk drive pin LED
            pin_x = cx2 - 12
            pin_y = c_y1 + 12
            pin_color = (0, 230, 118, 255) if (i + d) % 2 == 0 else (56, 189, 248, 255)
            draw.ellipse([pin_x - 3, pin_y - 3, pin_x + 3, pin_y + 3], fill=pin_color)

        # Middle Ventilation / Activity Grill
        grill_x1 = c_start_x + 3 * (c_w + c_gap) + 10
        grill_x2 = grill_x1 + 68
        draw.rounded_rectangle([grill_x1, c_y1 + 4, grill_x2, c_y2 - 4], radius=8, fill=(20, 21, 28, 255), outline=(180, 50, 25, 150), width=2)
        # Vertical vent slits
        for vx in range(grill_x1 + 12, grill_x2 - 8, 9):
            draw.line([(vx, c_y1 + 14), (vx, c_y2 - 14)], fill=(45, 48, 60, 255), width=3)

        # Right Section: Status Control Bezel
        pnl_x1 = grill_x2 + 20
        pnl_x2 = tray_right - 26
        draw.rounded_rectangle([pnl_x1, c_y1, pnl_x2, c_y2], radius=12, fill=(18, 19, 25, 255), outline=(190, 55, 30, 210), width=2)

        led_center_y = (c_y1 + c_y2) // 2

        # 1. Status LED: Green neon (Online/Healthy)
        led1_x = pnl_x1 + 32
        draw.ellipse([led1_x - 14, led_center_y - 14, led1_x + 14, led_center_y + 14], fill=(0, 230, 118, 55))
        draw.ellipse([led1_x - 9, led_center_y - 9, led1_x + 9, led_center_y + 9], fill=(0, 230, 118, 255))
        draw.ellipse([led1_x - 4, led_center_y - 5, led1_x + 2, led_center_y + 1], fill=(255, 255, 255, 230))

        # 2. Activity LED: Bright White (Traffic/Query)
        led2_x = led1_x + 36
        draw.ellipse([led2_x - 11, led_center_y - 11, led2_x + 11, led_center_y + 11], fill=(255, 255, 255, 45))
        draw.ellipse([led2_x - 7, led_center_y - 7, led2_x + 7, led_center_y + 7], fill=(255, 255, 255, 255))
        draw.ellipse([led2_x - 3, led_center_y - 4, led2_x + 1, led_center_y], fill=(210, 240, 255, 255))

        # 3. Power Button
        pwr_x = led2_x + 38
        draw.ellipse([pwr_x - 12, led_center_y - 12, pwr_x + 12, led_center_y + 12], fill=(40, 42, 52, 255), outline=(75, 80, 95, 255), width=2)
        draw.arc([pwr_x - 6, led_center_y - 6, pwr_x + 6, led_center_y + 6], start=40, end=320, fill=(225, 230, 240, 255), width=2)
        draw.line([(pwr_x, led_center_y - 7), (pwr_x, led_center_y - 1)], fill=(225, 230, 240, 255), width=2)

    # Export PNG (512x512)
    png_path = os.path.join(output_dir, 'app-icon.png')
    icon_512 = canvas.resize((512, 512), Image.Resampling.LANCZOS)
    icon_512.save(png_path, 'PNG')
    print(f"Exported PNG: {png_path} (512x512)")

    # Export ICO with multiple sizes [256, 128, 64, 48, 32, 16]
    ico_path = os.path.join(output_dir, 'app-icon.ico')
    sizes = [256, 128, 64, 48, 32, 16]
    icon_imgs = [canvas.resize((s, s), Image.Resampling.LANCZOS) for s in sizes]
    icon_imgs[0].save(
        ico_path,
        format='ICO',
        sizes=[(s, s) for s in sizes],
        append_images=icon_imgs[1:]
    )
    print(f"Exported ICO: {ico_path} with sizes: {sizes}")

if __name__ == '__main__':
    target_dir = os.path.dirname(os.path.abspath(__file__))
    generate_icons(target_dir)
