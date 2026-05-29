"""
Create floating animated GIFs from Pokemon HOME sprites.
Downloads the static PNG, creates frames with vertical bobbing, saves as GIF.
"""
import urllib.request
import os
from PIL import Image, ImageSequence
import math

# Our 5 phase Pokemon
POKEMON = {
    "victini": 494,
    "hoopa": 720,
    "celebi": 251,
    "keldeo": 647,
    "jirachi": 385,
}

HOME_BASE = "https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/"
OUTPUT_DIR = os.path.join(os.path.dirname(__file__), "floating-sprites")

# Animation settings
NUM_FRAMES = 24          # frames in the loop
FLOAT_DISTANCE = 8       # pixels up/down from center
FRAME_DURATION_MS = 80   # milliseconds per frame (~12.5 fps, smooth)
CANVAS_PADDING = FLOAT_DISTANCE + 2  # extra space for floating

def download_sprite(species_id):
    """Download HOME sprite PNG."""
    url = f"{HOME_BASE}{species_id}.png"
    print(f"  Downloading {url}...")
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req) as response:
        return response.read()

def create_floating_gif(name, species_id, output_path):
    """Create a floating animated GIF from a static PNG sprite."""
    print(f"Creating floating GIF for {name} (#{species_id})...")

    # Download the sprite
    png_data = download_sprite(species_id)

    # Save temp PNG
    temp_png = os.path.join(OUTPUT_DIR, f"{name}_temp.png")
    with open(temp_png, "wb") as f:
        f.write(png_data)

    # Open the sprite
    sprite = Image.open(temp_png).convert("RGBA")
    w, h = sprite.size

    # Create canvas with extra padding for floating movement
    canvas_w = w
    canvas_h = h + (CANVAS_PADDING * 2)

    frames = []
    for i in range(NUM_FRAMES):
        # Sine wave for smooth floating: goes up, back to center, down, back to center
        progress = i / NUM_FRAMES
        offset_y = int(FLOAT_DISTANCE * math.sin(2 * math.pi * progress))

        # Create transparent frame
        frame = Image.new("RGBA", (canvas_w, canvas_h), (0, 0, 0, 0))

        # Paste sprite at offset position (centered horizontally, floating vertically)
        paste_y = CANVAS_PADDING - offset_y
        frame.paste(sprite, (0, paste_y), sprite)

        # Convert to palette mode for GIF (with transparency)
        # Use alpha channel to determine transparency
        alpha = frame.split()[3]
        frame_rgb = frame.convert("RGB")
        frame_p = frame_rgb.quantize(colors=255, method=2)

        # Set transparency for pixels that were transparent
        mask = Image.eval(alpha, lambda a: 255 if a < 128 else 0)
        frame_p.paste(255, mask)  # 255 = transparent color index

        frames.append(frame_p)

    # Save as animated GIF
    frames[0].save(
        output_path,
        save_all=True,
        append_images=frames[1:],
        duration=FRAME_DURATION_MS,
        loop=0,  # infinite loop
        transparency=255,
        disposal=2,  # clear frame before drawing next
    )

    # Clean up temp file
    os.remove(temp_png)

    file_size = os.path.getsize(output_path)
    print(f"  Saved: {output_path} ({file_size:,} bytes)")

def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    print("=" * 50)
    print("Creating Floating Pokemon GIFs")
    print("=" * 50)

    for name, species_id in POKEMON.items():
        output_path = os.path.join(OUTPUT_DIR, f"{name}_float.gif")
        create_floating_gif(name, species_id, output_path)

    print()
    print("All GIFs created successfully!")
    print(f"Output directory: {OUTPUT_DIR}")

    # List all files
    for f in os.listdir(OUTPUT_DIR):
        if f.endswith(".gif"):
            size = os.path.getsize(os.path.join(OUTPUT_DIR, f))
            print(f"  {f}: {size:,} bytes")

if __name__ == "__main__":
    main()
