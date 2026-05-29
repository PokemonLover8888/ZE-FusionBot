#r "nuget: System.Drawing.Common, 8.0.0"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.IO;
using System.Threading.Tasks;

async Task CreatePreview(string name, int species, string eggUrl, string spriteUrl)
{
    using var httpClient = new HttpClient();
    
    // Download egg
    var eggBytes = await httpClient.GetByteArrayAsync(eggUrl);
    using var eggStream = new MemoryStream(eggBytes);
    using var eggImg = new Bitmap(eggStream);
    
    // Download Pokemon sprite
    var spriteBytes = await httpClient.GetByteArrayAsync(spriteUrl);
    using var spriteStream = new MemoryStream(spriteBytes);
    using var spriteImg = new Bitmap(spriteStream);
    
    // Create canvas (220x220 egg)
    int eggSize = 220;
    using var result = new Bitmap(eggSize + 40, eggSize + 40);
    using var g = Graphics.FromImage(result);
    
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.SmoothingMode = SmoothingMode.HighQuality;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.Clear(Color.Transparent);
    
    // Draw egg
    g.DrawImage(eggImg, 20, 20, eggSize, eggSize);
    
    // Draw Pokemon at 72% size
    int pokemonSize = (int)(eggSize * 0.72);
    int offset = (eggSize - pokemonSize) / 2 + 20;
    g.DrawImage(spriteImg, offset, offset, pokemonSize, pokemonSize);
    
    result.Save($"{name.ToLower()}_egg_preview.png", System.Drawing.Imaging.ImageFormat.Png);
    Console.WriteLine($"Created {name} preview");
}

// Create both previews
await CreatePreview(
    "Togepi", 
    175, 
    "https://creator.pkm-universe.com/assets/anime-eggs/175.png",
    "https://raw.githubusercontent.com/hexbyt3/HomeImages/master/512x512/poke_capture_0175_000_mf_n_00000000_f_n.png"
);

await CreatePreview(
    "Eevee",
    133,
    "https://creator.pkm-universe.com/assets/anime-eggs/133.png", 
    "https://raw.githubusercontent.com/hexbyt3/HomeImages/master/512x512/poke_capture_0133_000_md_n_00000000_f_n.png"
);
