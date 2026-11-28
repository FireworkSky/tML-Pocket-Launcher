#include "control_art.h"
#include "adpcm.h"

#include <array>
#include <charconv>
#include <list>
#include <set>
#include <unordered_set>
#include <zlib.h>

extern "C" int tml_decode_fsb5(const char *input, const char *output, int *channels, int *rate);

namespace terraria {
namespace {
constexpr std::array<std::string_view, 1> textureReaders{"Microsoft.Xna.Framework.Content.Texture2DReader"};
constexpr std::array<std::string_view, 1> soundReaders{"Microsoft.Xna.Framework.Content.SoundEffectReader"};
constexpr std::array<std::string_view, 8> fontReaders{
    "ReLogic.Graphics.DynamicSpriteFontReader, ReLogic",
    "Microsoft.Xna.Framework.Content.Texture2DReader",
    "Microsoft.Xna.Framework.Content.ListReader`1[[Microsoft.Xna.Framework.Rectangle, FNA]]",
    "Microsoft.Xna.Framework.Content.ListReader`1[[System.Char, mscorlib]]",
    "Microsoft.Xna.Framework.Content.ListReader`1[[Microsoft.Xna.Framework.Vector3, FNA]]",
    "Microsoft.Xna.Framework.Content.RectangleReader",
    "Microsoft.Xna.Framework.Content.CharReader",
    "Microsoft.Xna.Framework.Content.Vector3Reader",
};
std::unordered_set<uint32_t> audioSetupIds;

bool setupId(View fsb, uint32_t &id)
{
    require(fsb.size() >= 68 && std::string_view(reinterpret_cast<const char *>(fsb.data()), 4) == "FSB5",
        "Invalid FSB5 audio header");
    Reader fileHeader{fsb, 0x18};
    if (fileHeader.read<uint32_t>() != 0x0f) return false;
    Reader reader{fsb, 60};
    (void)reader.read<uint64_t>();
    require(reader.bytes[60] & 1, "FSB5 Vorbis setup metadata is missing");
    for (;;) {
        const uint32_t header = reader.read<uint32_t>();
        const uint32_t type = header >> 25;
        const uint32_t size = (header >> 1) & 0xffffff;
        require(size <= reader.bytes.size() - reader.pos, "Invalid FSB5 audio metadata");
        if (type == 0x0b) {
            require(size >= 4, "Empty FSB5 Vorbis setup ID");
            id = reader.read<uint32_t>();
            return true;
        }
        reader.take(size);
        if (!(header & 1)) break;
    }
    throw std::runtime_error("FSB5 Vorbis setup ID is missing");
}

uint32_t key(std::string name)
{
    for (auto &c : name) if (c >= 'A' && c <= 'Z') c += 'a' - 'A';
    return crc32(0, reinterpret_cast<const uint8_t *>(name.data()), name.size());
}

struct Required {
    std::string path;
    int width, height;
};

std::vector<Required> inventory(const fs::path &path)
{
    std::ifstream input(path, std::ios::binary);
    require(input.is_open(), "Resource inventory is missing");
    std::vector<Required> result;
    std::string name;
    int width, height;
    while (std::getline(input, name, '\t')) {
        require(bool(input >> width >> height) && input.get() == '\n', "Invalid resource inventory");
        result.push_back({name, width, height});
    }
    require(!result.empty(), "Empty resource inventory");
    return result;
}

struct Audio {
    int channels, rate;
    Bytes samples;
    uint32_t frames() const { return samples.size() / (channels * 2); }
};

Audio decode(const fs::path &workspace, View fsb)
{
    const auto input = workspace / "clip.fsb";
    const auto output = workspace / "clip.pcm";
    save(input, fsb);
    Audio result{};
    require(tml_decode_fsb5(input.c_str(), output.c_str(), &result.channels, &result.rate) == 0,
        "Could not decode FSB5 audio");
    MappedFile pcm(output);
    require(pcm.bytes().size() % (result.channels * 2) == 0, "Truncated PCM frames");
    result.samples.assign(pcm.bytes().begin(), pcm.bytes().end());
    return result;
}

Bytes soundPayload(const Audio &audio)
{
    Bytes output;
    const uint32_t alignment = audio.channels * 2;
    put(output, 18);
    put(output, 1, 2);
    put(output, audio.channels, 2);
    put(output, audio.rate);
    put(output, audio.rate * alignment);
    put(output, alignment, 2);
    put(output, 16, 2);
    put(output, 0, 2); // PCM WAVEFORMATEX has no extension.
    put(output, audio.samples.size());
    append(output, audio.samples);
    put(output, 0);
    put(output, 0);
    put(output, uint64_t(audio.frames()) * 1000 / audio.rate);
    return output;
}

std::vector<int> musicOrder(View soundBank)
{
    Reader bank{soundBank};
    require(bank.read<uint32_t>() == 0x4b424453 && bank.read<uint16_t>() == 46 &&
        bank.read<uint16_t>() == 43, "Unsupported XACT sound bank");
    bank.pos = 19;
    const auto simple = bank.read<uint16_t>();
    const auto complex = bank.read<uint16_t>();
    bank.pos = 27;
    require(simple > 0 && !complex && bank.read<uint8_t>() == 1 && bank.read<uint16_t>() == simple,
        "Expected a single XACT wave bank");
    bank.pos = 34;
    const auto cues = bank.read<uint32_t>();
    bank.pos += 20;
    const auto waveBank = bank.read<uint32_t>();
    bank.pos += 4;
    const auto names = bank.read<uint32_t>();
    const auto sounds = bank.read<uint32_t>();
    require(waveBank >= 138 && waveBank <= sounds && sounds <= cues && cues <= names &&
        uint64_t(cues) + 5 * simple <= names && uint64_t(names) + 6 * simple <= soundBank.size(),
        "Invalid XACT sound bank offsets");
    Reader waveName{slice(soundBank, waveBank, 64)};
    require(waveName.string() == "Wave Bank", "Unexpected XACT wave bank name");
    std::vector<int> order(simple);
    std::set<int> numbers;
    for (int i = 0; i < simple; ++i) {
        bank.pos = cues + 5 * i;
        require(bank.read<uint8_t>() == 4, "Unsupported XACT cue");
        const auto sound = bank.read<uint32_t>();
        bank.pos = names + 6 * i;
        const auto nameOffset = bank.read<uint32_t>();
        require(nameOffset < soundBank.size() && sound >= sounds && uint64_t(sound) + 12 <= cues,
            "Invalid XACT cue offsets");
        bank.pos = nameOffset;
        const auto name = bank.string();
        require(name.starts_with("Music_"), "Unexpected XACT cue name");
        int number = 0;
        auto parsed = std::from_chars(name.data() + 6, name.data() + name.size(), number);
        require(parsed.ec == std::errc{} && parsed.ptr == name.data() + name.size() && number > 0 &&
            numbers.insert(number).second, "Invalid XACT music number");
        bank.pos = sound;
        require(!(bank.read<uint8_t>() & 1), "Unsupported XACT sound type");
        bank.pos = sound + 9;
        const auto slot = bank.read<uint16_t>();
        require(bank.read<uint8_t>() == 0 && slot < order.size() && !order[slot], "Invalid XACT wave slot");
        order[slot] = number;
    }
    return order;
}

int music(const UnityResources &unity, const fs::path &workspace, const fs::path &content,
    const Progress &progress)
{
    constexpr uint32_t alignment = 2048, headerSize = 52, bankInfoSize = 96, entrySize = 24;
    const std::array<uint8_t, alignment> zeros{};
    const auto aligned = [](uint64_t size) { return (size + alignment - 1) / alignment * alignment; };
    MappedFile settings(workspace / "TerrariaMusic.xgs");
    Reader settingsReader{settings.bytes()};
    require(settings.bytes().size() >= 77 && settingsReader.read<uint32_t>() == 0x46534758 &&
        settingsReader.read<uint16_t>() == 46 && settingsReader.read<uint16_t>() == 42,
        "Unsupported XACT settings");
    MappedFile soundBank(workspace / "Sound Bank.xsb");
    const auto tracks = musicOrder(soundBank.bytes());
    const uint32_t metadataEnd = headerSize + bankInfoSize + tracks.size() * entrySize;
    const uint32_t dataStart = aligned(metadataEnd);
    auto bank = outputFile(content / "Wave Bank.xwb");
    bank.seekp(dataStart);
    Bytes entries;
    uint32_t offset = 0;
    for (size_t i = 0; i < tracks.size(); ++i) {
        progress("music", i, tracks.size());
        const auto source = unity.audio(unity.get("content/music/music_" + std::to_string(tracks[i])));
        uint32_t id = 0;
        if (setupId(source, id)) audioSetupIds.insert(id);
        const auto audio = decode(workspace, source);
        require(audio.rate > 0 && audio.rate < (1 << 18), "Unsupported XACT sample rate");
        const auto encoded = encodeMsAdpcm(audio.samples, audio.channels);
        const uint32_t frames = encoded.size() / (adpcmBytesPerChannel * audio.channels) * adpcmFramesPerBlock;
        require(frames < (1 << 28), "XACT ADPCM track exceeds the sample limit");
        const uint64_t length = aligned(encoded.size());
        require(dataStart + offset + length < (1ull << 32), "XACT wave bank size limit exceeded");
        constexpr uint32_t adpcmTag = 2;
        const uint32_t format = adpcmTag | uint32_t(audio.channels) << 2 | uint32_t(audio.rate) << 5 |
            uint32_t(adpcmBytesPerChannel - 22) << 23;
        for (uint32_t field : {frames << 4, format, offset,
            uint32_t(encoded.size()), 0u, frames}) put(entries, field);
        write(bank, encoded);
        write(bank, View(zeros).first(length - encoded.size()));
        offset += length;
    }
    Bytes metadata;
    append(metadata, "WBND");
    for (uint32_t field : {46u, 44u, headerSize, bankInfoSize, headerSize + bankInfoSize,
        uint32_t(entries.size()), metadataEnd, 0u, 0u, 0u, dataStart, offset}) put(metadata, field);
    put(metadata, 1);
    put(metadata, tracks.size());
    append(metadata, "Wave Bank");
    metadata.resize(headerSize + 8 + 64);
    for (uint32_t field : {entrySize, 64u, alignment, 0u, 0u, 0u}) put(metadata, field);
    append(metadata, entries);
    bank.seekp(0);
    write(bank, metadata);
    bank.close();
    save(content / "TerrariaMusic.xgs", settings.bytes());
    save(content / "Sound Bank.xsb", soundBank.bytes());
    return tracks.size();
}

struct AtlasAlias {
    std::string_view desktopPath, mobilePath;
};

// The mobile atlas indexes these NPC textures under their town profile names.
constexpr AtlasAlias atlasAliases[] = {
    {"Images/NPC_107.xnb", "TownNPCs/GoblinTinkerer_Default.png"},
    {"Images/NPC_108.xnb", "TownNPCs/Wizard_Default.png"},
    {"Images/NPC_108_Alt_1.xnb", "TownNPCs/Wizard_Default_Party.png"},
    {"Images/NPC_124.xnb", "TownNPCs/Mechanic_Default.png"},
    {"Images/NPC_124_Alt_1.xnb", "TownNPCs/Mechanic_Default_Party.png"},
    {"Images/NPC_142.xnb", "TownNPCs/Santa_Default.png"},
    {"Images/NPC_142_Alt_1.xnb", "TownNPCs/Santa_Default_Party.png"},
    {"Images/NPC_160.xnb", "TownNPCs/Truffle_Default.png"},
    {"Images/NPC_17.xnb", "TownNPCs/Merchant_Default.png"},
    {"Images/NPC_178.xnb", "TownNPCs/Steampunker_Default.png"},
    {"Images/NPC_178_Alt_1.xnb", "TownNPCs/Steampunker_Default_Party.png"},
    {"Images/NPC_17_Alt_1.xnb", "TownNPCs/Merchant_Default_Party.png"},
    {"Images/NPC_18.xnb", "TownNPCs/Nurse_Default.png"},
    {"Images/NPC_18_Alt_1.xnb", "TownNPCs/Nurse_Default_Party.png"},
    {"Images/NPC_19.xnb", "TownNPCs/ArmsDealer_Default.png"},
    {"Images/NPC_20.xnb", "TownNPCs/Dryad_Default.png"},
    {"Images/NPC_207.xnb", "TownNPCs/DyeTrader_Default.png"},
    {"Images/NPC_207_Alt_1.xnb", "TownNPCs/DyeTrader_Default_Party.png"},
    {"Images/NPC_208.xnb", "TownNPCs/PartyGirl_Default.png"},
    {"Images/NPC_209.xnb", "TownNPCs/Cyborg_Default.png"},
    {"Images/NPC_209_Alt_1.xnb", "TownNPCs/Cyborg_Default_Party.png"},
    {"Images/NPC_22.xnb", "TownNPCs/Guide_Default.png"},
    {"Images/NPC_227.xnb", "TownNPCs/Painter_Default.png"},
    {"Images/NPC_227_Alt_1.xnb", "TownNPCs/Painter_Default_Party.png"},
    {"Images/NPC_228.xnb", "TownNPCs/WitchDoctor_Default.png"},
    {"Images/NPC_229.xnb", "TownNPCs/Pirate_Default.png"},
    {"Images/NPC_229_Alt_1.xnb", "TownNPCs/Pirate_Default_Party.png"},
    {"Images/NPC_353.xnb", "TownNPCs/Stylist_Default.png"},
    {"Images/NPC_353_Alt_1.xnb", "TownNPCs/Stylist_Default_Party.png"},
    {"Images/NPC_368.xnb", "TownNPCs/TravelingMerchant_Default.png"},
    {"Images/NPC_368_Alt_1.xnb", "TownNPCs/TravelingMerchant_Default_Party.png"},
    {"Images/NPC_369.xnb", "TownNPCs/Angler_Default.png"},
    {"Images/NPC_369_Alt_1.xnb", "TownNPCs/Angler_Default_Party.png"},
    {"Images/NPC_37.xnb", "TownNPCs/OldMan_Default.png"},
    {"Images/NPC_38_Alt_1.xnb", "TownNPCs/Demolitionist_Default_Party.png"},
    {"Images/NPC_441.xnb", "TownNPCs/TaxCollector_Default.png"},
    {"Images/NPC_441_Alt_1.xnb", "TownNPCs/TaxCollector_Default_Party.png"},
    {"Images/NPC_453.xnb", "TownNPCs/SkeletonMerchant_Default.png"},
    {"Images/NPC_453_Alt_1.xnb", "TownNPCs/SkeletonMerchant_Default_Party.png"},
    {"Images/NPC_54.xnb", "TownNPCs/Clothier_Default.png"},
    {"Images/NPC_54_Alt_1.xnb", "TownNPCs/Clothier_Default_Party.png"},
    {"Images/NPC_588.xnb", "TownNPCs/Golfer_Default.png"},
    {"Images/NPC_588_Alt_1.xnb", "TownNPCs/Golfer_Default_Party.png"},
    {"Images/NPC_633.xnb", "TownNPCs/BestiaryGirl_Default.png"},
    {"Images/NPC_633_Alt_1.xnb", "TownNPCs/BestiaryGirl_Default_Transformed.png"},
    {"Images/NPC_633_Alt_2.xnb", "TownNPCs/BestiaryGirl_Default_Transformed.png"},
    {"Images/NPC_637.xnb", "TownNPCs/Cat_Siamese.png"},
    {"Images/NPC_638.xnb", "TownNPCs/Dog_Labrador.png"},
    {"Images/NPC_656.xnb", "TownNPCs/Bunny_White.png"},
    {"Images/NPC_663.xnb", "TownNPCs/Princess_Default.png"},
    {"Images/NPC_670.xnb", "TownNPCs/SlimeBlue_Default.png"},
    {"Images/NPC_678.xnb", "TownNPCs/SlimeGreen_Default.png"},
    {"Images/NPC_679.xnb", "TownNPCs/SlimeOld_Default.png"},
    {"Images/NPC_680.xnb", "TownNPCs/SlimePurple_Default.png"},
    {"Images/NPC_681.xnb", "TownNPCs/SlimeRainbow_Default.png"},
    {"Images/NPC_682.xnb", "TownNPCs/SlimeRed_Default.png"},
    {"Images/NPC_683.xnb", "TownNPCs/SlimeYellow_Default.png"},
    {"Images/NPC_684.xnb", "TownNPCs/SlimeCopper_Default.png"},
};

struct AtlasHashAlias {
    std::string_view desktopPath;
    uint32_t spriteHash;
};

// These atlas records have the target dimensions and matching premultiplied pixels.
constexpr AtlasHashAlias atlasHashAliases[] = {
    {"Images/Background_276.xnb", 0xe326a66f},
    {"Images/NPC_363.xnb", 0xda796757},
    {"Images/NPC_365.xnb", 0x553992f7},
    {"Images/NPC_373.xnb", 0x1125b4f2},
    {"Images/NPC_565.xnb", 0x36e9a7cd},
    {"Images/NPC_567.xnb", 0x4c29f4ad},
    {"Images/NPC_573.xnb", 0x72f581c8},
    {"Images/NPC_577.xnb", 0x87752708},
    {"Images/NPC_603.xnb", 0x5e18abed},
    {"Images/NPC_609.xnb", 0x14a8b34c},
    {"Images/UI/DisplaySlots_4.xnb", 0xfa94b98c},
    {"Images/UI/DisplaySlots_5.xnb", 0xbd34c35c},
};

// The same PE sprites back the launcher's mobile list and search controls.
constexpr AtlasHashAlias uiIconAliases[] = {
    {"Images/UI/ButtonDelete.xnb", 0x0bde99f1},
    {"Images/UI/ButtonPlay.xnb", 0x9c60fdad},
    {"Images/UI/ButtonRename.xnb", 0x11a35941},
    {"Images/UI/ButtonSeed.xnb", 0xd361f275},
    {"Images/UI/SearchCancel.xnb", 0x8bbb7e6a},
};

// PE shares indexed background pixels across variants and keeps each variant's palette.
constexpr AtlasAlias backgroundPaletteSources[] = {
    {"Images/Background_10.xnb", "background_30.png"},
    {"Images/Background_102.xnb", "background_104.png"},
    {"Images/Background_103.xnb", "background_104.png"},
    {"Images/Background_11.xnb", "background_31.png"},
    {"Images/Background_16.xnb", "background_15.png"},
    {"Images/Background_177.xnb", "background_275.png"},
    {"Images/Background_208.xnb", "background_207.png"},
    {"Images/Background_211.xnb", "background_213.png"},
    {"Images/Background_212.xnb", "background_213.png"},
    {"Images/Background_23.xnb", "background_22.png"},
    {"Images/Background_24.xnb", "background_22.png"},
    {"Images/Background_25.xnb", "background_22.png"},
    {"Images/Background_48.xnb", "background_46.png"},
    {"Images/Background_50.xnb", "background_52.png"},
    {"Images/Background_51.xnb", "background_52.png"},
    {"Images/Background_57.xnb", "background_56.png"},
    {"Images/Background_58.xnb", "background_56.png"},
    {"Images/Background_61.xnb", "background_60.png"},
    {"Images/Background_9.xnb", "background_29.png"},
    {"Images/Background_97.xnb", "background_95.png"},
};

Image sharedPaletteTexture(const UnityResources &unity, const Regions &regions, const Region &target,
    const Required &item)
{
    const auto background = std::ranges::find(backgroundPaletteSources, item.path, &AtlasAlias::desktopPath);
    const bool water = item.path.starts_with("Images/Misc/water_") && item.width == 48 && item.height == 1360;
    require(background != std::end(backgroundPaletteSources) || water, "Missing PE atlas image");
    const auto sourceName = water ? "misc/water_0.png" : std::string(background->mobilePath);
    const auto source = regions.find(key(sourceName));
    require(source != regions.end() && source->second.size() == 1, "Missing or ambiguous shared PE atlas image");
    const auto &region = source->second.front();
    auto indices = unpackSprite(unity.texture(unity.get(region.sheet)), region);
    const auto palette = unity.texture(unity.get(target.sheet + "_p"));
    require(palette.width == 256 && palette.height == 1 && indices.width == item.width &&
        target.width * target.divisor == item.width && target.height * target.divisor == item.height,
        "Unexpected shared PE atlas dimensions");
    const bool extendBottom = item.path == "Images/Background_208.xnb" && indices.height == 400 && item.height == 464;
    require(indices.height == item.height || extendBottom, "Unexpected shared PE atlas height");

    Image result(item.width, item.height);
    for (size_t i = 0; i < indices.rgba.size(); i += 4)
        std::copy_n(palette.rgba.data() + palette.offset(indices.rgba[i + 3], 0), 4, result.rgba.data() + i);
    if (extendBottom) {
        const auto row = result.rgba.data() + result.offset(0, indices.height - 1);
        for (int x = 1; x < result.width; ++x)
            require(std::equal(row, row + 4, row + x * 4), "PE background does not end in a solid row");
        for (int y = indices.height; y < result.height; ++y)
            std::copy_n(row, size_t(result.width) * 4, result.rgba.data() + result.offset(0, y));
    }
    return result;
}

Image repackSunflower(const Image &image)
{
    constexpr int frames = 19;
    constexpr int mobileFrameHeight = 54, targetFrameHeight = 53;
    require(image.width == 52 && image.height == 1028, "Unexpected PE sunflower frame layout");
    Image result(image.width, frames * targetFrameHeight);
    const auto checkPadding = [&](int row) {
        for (int x = 0; x < image.width; ++x)
            require(image.rgba[image.offset(x, row) + 3] == 0, "PE sunflower frame padding is not transparent");
    };
    for (int frame = 0; frame < frames; ++frame) {
        checkPadding(frame * mobileFrameHeight);
        const auto source = image.rgba.data() + image.offset(0, frame * mobileFrameHeight + 1);
        const auto target = result.rgba.data() + result.offset(0, frame * targetFrameHeight);
        std::copy_n(source, size_t(image.width) * targetFrameHeight * 4, target);
    }
    for (int row = frames * mobileFrameHeight; row < image.height; ++row) checkPadding(row);
    return result;
}

struct MinimapLayout {
    std::string_view name;
    std::array<int, 4> mobileOpening, targetOpening;
};

constexpr MinimapLayout minimapLayouts[] = {
    {"Default", {20, 18, 252, 160}, {8, 14, 240, 246}},
    {"Golden", {18, 16, 254, 162}, {8, 8, 244, 244}},
    {"Leaf", {26, 22, 246, 160}, {22, 22, 246, 250}},
    {"Remix", {20, 18, 252, 160}, {10, 10, 242, 242}},
    {"Retro", {20, 18, 252, 160}, {10, 10, 242, 242}},
    {"Sticks", {22, 20, 250, 160}, {12, 12, 240, 242}},
    {"StoneGold", {18, 16, 254, 160}, {12, 12, 248, 248}},
    {"TwigLeaf", {20, 20, 252, 152}, {32, 20, 246, 242}},
    {"Valkyrie", {20, 24, 252, 160}, {10, 16, 242, 242}},
};

Image fitSlices(const Image &image, int width, int height,
    const std::array<int, 4> &sourceOpening, const std::array<int, 4> &targetOpening)
{
    const std::array srcX{0, sourceOpening[0], sourceOpening[2], image.width};
    const std::array srcY{0, sourceOpening[1], sourceOpening[3], image.height};
    const std::array dstX{0, targetOpening[0], targetOpening[2], width};
    const std::array dstY{0, targetOpening[1], targetOpening[3], height};
    require(srcX[2] < image.width && srcY[2] < image.height && dstX[2] < width && dstY[2] < height,
        "Unexpected sliced texture dimensions");
    Image result(width, height);
    const auto sample = [](int pos, const auto &src, const auto &dst) {
        const int segment = pos < dst[1] ? 0 : pos < dst[2] ? 1 : 2;
        return src[segment] + (pos - dst[segment]) * (src[segment + 1] - src[segment]) /
            (dst[segment + 1] - dst[segment]);
    };
    for (int y = 0; y < height; ++y) {
        const int sy = sample(y, srcY, dstY);
        for (int x = 0; x < width; ++x) {
            const int sx = sample(x, srcX, dstX);
            std::copy_n(image.rgba.data() + image.offset(sx, sy), 4, result.rgba.data() + result.offset(x, y));
        }
    }
    return result;
}

Image fitIcon(const Image &image, int width, int height)
{
    Image result(width, height);
    const double scale = std::min({1.0, double(width) / image.width, double(height) / image.height});
    const int iconWidth = std::max(1, int(image.width * scale));
    const int iconHeight = std::max(1, int(image.height * scale));
    const int left = (width - iconWidth) / 2, top = (height - iconHeight) / 2;
    for (int y = 0; y < iconHeight; ++y) {
        for (int x = 0; x < iconWidth; ++x) {
            const int sx = (2 * x + 1) * image.width / (2 * iconWidth);
            const int sy = (2 * y + 1) * image.height / (2 * iconHeight);
            std::copy_n(image.rgba.data() + image.offset(sx, sy), 4, result.rgba.data() + result.offset(left + x, top + y));
        }
    }
    return result;
}

Image fitBestiaryStat(const Image &image, int width, int height)
{
    // The desktop UI right-aligns the stat text within the texture's full width.
    Image result(width, height);
    const auto icon = fitIcon(image, 30, height);
    for (int y = 0; y < height; ++y)
        std::copy_n(icon.rgba.data() + icon.offset(0, y), size_t(icon.width) * 4, result.rgba.data() + result.offset(0, y));
    return result;
}

bool isPanel(std::string_view path)
{
    return path == "Images/Chat_Back.xnb" || path == "Images/HairStyleBack.xnb" || path == "Images/ClothesStyleBack.xnb";
}

bool isMapModeIcon(std::string_view path)
{
    return path.size() == sizeof("Images/Map_0.xnb") - 1 && path.starts_with("Images/Map_") &&
        path[11] >= '0' && path[11] <= '7';
}

bool isMinimapZoomButton(std::string_view path)
{
    return path.starts_with("Images/UI/Minimap/") &&
        (path.ends_with("/MinimapButton_ZoomIn.xnb") || path.ends_with("/MinimapButton_ZoomOut.xnb"));
}

// Verified against the decoded 2026.09.3.1 profile textures. The checksums cover
// the complete premultiplied RGBA canvas, including transparent sprite padding.
struct CanvasAdjustment {
    std::string_view path;
    int sourceWidth, sourceHeight, targetWidth, targetHeight, x, y;
    uint32_t pixelCrc;
};

constexpr CanvasAdjustment canvasAdjustments[] = {
    {"Images/Glow_110.xnb", 50, 252, 64, 252, 12, 0, 0x657596e9},
    {"Images/Glow_95.xnb", 28, 28, 32, 32, 2, 2, 0x771181c3},
    {"Images/Tiles_552.xnb", 214, 36, 208, 36, 0, 0, 0x49d474e8},
};

Bytes adjustedTexture(const Required &item, const Image &image)
{
    const auto rule = std::ranges::find(canvasAdjustments, item.path, &CanvasAdjustment::path);
    require(rule != std::end(canvasAdjustments) && image.width == rule->sourceWidth &&
        image.height == rule->sourceHeight && item.width == rule->targetWidth &&
        item.height == rule->targetHeight, "Unexpected texture dimensions");
    Image canvas(item.width, item.height);
    const auto width = std::min(image.width, canvas.width - rule->x);
    const auto height = std::min(image.height, canvas.height - rule->y);
    for (int y = 0; y < height; ++y) {
        std::copy_n(image.rgba.data() + image.offset(0, y), size_t(width) * 4,
            canvas.rgba.data() + canvas.offset(rule->x, y + rule->y));
    }
    auto payload = texturePayload(std::move(canvas));
    const auto pixels = View(payload).subspan(20);
    require(crc32(0, pixels.data(), pixels.size()) == rule->pixelCrc,
        "Adjusted texture pixels differ from the target profile");
    return payload;
}


}

std::string convert(const fs::path &workspace, const fs::path &output, const Progress &progress)
{
    const auto required = inventory(workspace / "inventory.tsv");
    const auto content = output / "Content";
    fs::create_directories(content);
    UnityResources unity(workspace, progress);
    auto regions = readAtlasRegions(unity);
    const auto controlRecipes = workspace / "control-recipes";
    int controlSprites = 0;
    if (fs::is_regular_file(controlRecipes))
        controlSprites = extractControlArt(unity, regions, controlRecipes, output / "mobile-controls");
    std::list<std::pair<std::string, Image>> sheets;
    const auto texture = [&](const Required &item, auto asset) {
        if (asset != unity.assets.end() && asset->second.type == UnityAsset::Texture)
            return unity.texture(asset->second);
        const fs::path relative = fs::path(item.path.substr(7)).replace_extension(".png");
        auto entries = regions.find(key(relative.generic_string()));
        if (entries == regions.end()) entries = regions.find(key(relative.filename().string()));
        if (entries == regions.end() || std::ranges::none_of(entries->second, [&](const Region &region) {
            return unity.assets.contains(region.sheet);
        })) {
            const auto alias = std::ranges::find(atlasHashAliases, item.path, &AtlasHashAlias::desktopPath);
            if (alias != std::end(atlasHashAliases)) {
                const auto alternate = regions.find(alias->spriteHash);
                if (alternate != regions.end()) entries = alternate;
            }
        }
        if (entries == regions.end()) {
            const auto alias = std::ranges::find(atlasAliases, item.path, &AtlasAlias::desktopPath);
            if (alias != std::end(atlasAliases)) {
                entries = regions.find(key(std::string(alias->mobilePath)));
                if (entries == regions.end())
                    entries = regions.find(key(fs::path(alias->mobilePath).filename().string()));
            }
        }
        if (entries == regions.end() && isMapModeIcon(item.path)) {
            const int state = item.path[11] - '0';
            switch (state % 4) {
                case 1: return loadAtlas(unity, "content/images/ui/minimapdefault/hideminimap");
                case 2: entries = regions.find(state < 4 ? 0xd2a2c6bb : 0xd2d1d2bc); break;
                case 3: return loadAtlas(unity, "content/images/ui/minimapdefault/showminimap");
                default: entries = regions.find(0x80987346); break;
            }
        }
        if (entries == regions.end() && isMinimapZoomButton(item.path)) {
            entries = regions.find(item.path.ends_with("/MinimapButton_ZoomIn.xnb") ? 0xb0ab9470 : 0xc5ab9c74);
        }
        if (entries == regions.end()) {
            const auto icon = std::ranges::find(uiIconAliases, item.path, &AtlasHashAlias::desktopPath);
            if (icon != std::end(uiIconAliases)) entries = regions.find(icon->spriteHash);
        }
        require(entries != regions.end(), "No atlas entry");
        const Region *chosen = nullptr;
        for (const auto &region : entries->second) {
            if (entries->second.size() > 1 && (region.width * region.divisor != item.width ||
                region.height * region.divisor != item.height)) continue;
            require(!chosen, "Ambiguous atlas entry");
            chosen = &region;
        }
        require(chosen, "No matching atlas entry");
        if (!unity.assets.contains(chosen->sheet))
            return sharedPaletteTexture(unity, regions, *chosen, item);
        auto cached = std::ranges::find_if(sheets, [&](const auto &sheet) { return sheet.first == chosen->sheet; });
        if (cached == sheets.end()) {
            if (sheets.size() == 4) sheets.pop_back();
            sheets.emplace_front(chosen->sheet, loadAtlas(unity, chosen->sheet));
        } else sheets.splice(sheets.begin(), sheets, cached);
        return unpackSprite(sheets.front().second, *chosen);
    };
    int images = 0, sounds = 0, fonts = 0;
    std::vector<int> silentItemSounds;
    for (size_t i = 0; i < required.size(); ++i) {
        progress("assets", i, required.size());
        const auto &item = required[i];
        auto mobile = "content/" + item.path.substr(0, item.path.size() - 4);
        for (auto &c : mobile) if (c >= 'A' && c <= 'Z') c += 'a' - 'A';
        const auto asset = unity.assets.find(mobile);
        try {
            if (item.path.starts_with("Images/")) {
                auto image = texture(item, asset);
                if (item.path == "Images/UI/Sunflower_Loading.xnb" && item.width == 52 &&
                    item.height == 1007 && image.height != item.height)
                    image = repackSunflower(image);
                if (item.width == 22 && item.height == 22 && image.width == 32 && image.height == 32 &&
                    (item.path == "Images/UI/ButtonCloudActive.xnb" || item.path == "Images/UI/ButtonCloudInactive.xnb" ||
                     item.path == "Images/UI/ButtonFavoriteActive.xnb" || item.path == "Images/UI/ButtonFavoriteInactive.xnb"))
                    image = fitIcon(image, item.width, item.height);
                if (image.width != item.width || image.height != item.height) {
                    const std::string_view path = item.path;
                    if (isMapModeIcon(path) || isMinimapZoomButton(path) ||
                        std::ranges::find(uiIconAliases, path, &AtlasHashAlias::desktopPath) != std::end(uiIconAliases)) {
                        image = fitIcon(image, item.width, item.height);
                    } else if (isPanel(path) && image.width == 32 && image.height == 32) {
                        image = fitSlices(image, item.width, item.height,
                            {8, 8, 24, 24}, {8, 8, item.width - 8, item.height - 8});
                    } else if (path.starts_with("Images/UI/Bestiary/Stat_") && image.width == 32 && image.height == 32 &&
                        item.height == 31 && (item.width == 47 || item.width == 96)) {
                        image = fitBestiaryStat(image, item.width, item.height);
                    } else if (path == "Images/UI/Bestiary/Portrait_Front.xnb" && image.width == 16 && image.height == 16 &&
                        item.width == 193 && item.height == 112) {
                        image = fitSlices(image, item.width, item.height,
                            {4, 4, 12, 12}, {4, 4, item.width - 4, item.height - 4});
                    } else if (path.starts_with("Images/UI/Minimap/") && path.ends_with("/MinimapFrame.xnb") &&
                        image.width == 272 && image.height == 180) {
                        const auto theme = path.substr(18, path.find('/', 18) - 18);
                        const auto layout = std::ranges::find(minimapLayouts, theme, &MinimapLayout::name);
                        require(layout != std::end(minimapLayouts), "Unknown minimap theme");
                        image = fitSlices(image, item.width, item.height, layout->mobileOpening, layout->targetOpening);
                    }
                }
                const auto payload = image.width == item.width && image.height == item.height
                    ? texturePayload(std::move(image))
                    : adjustedTexture(item, image);
                writeXnb(content / item.path, textureReaders, payload);
                ++images;
            } else if (item.path.starts_with("Sounds/")) {
                Audio audio;
                if (asset != unity.assets.end()) {
                    audio = decode(workspace, unity.audio(asset->second));
                } else {
                    constexpr std::string_view itemPrefix = "content/sounds/item_";
                    if (!mobile.starts_with(itemPrefix)) throw std::runtime_error("Missing Unity asset: " + mobile);
                    int number = 0;
                    const auto end = mobile.data() + mobile.size();
                    const auto parsed = std::from_chars(mobile.data() + itemPrefix.size(), end, number);
                    // Older PE builds omit these item sounds. Keep their XNB slots valid.
                    if (parsed.ec != std::errc{} || parsed.ptr != end || number < 199 || number > 210)
                        throw std::runtime_error("Missing Unity asset: " + mobile);
                    audio = Audio{1, 22050, Bytes(220 * 2, 0)};
                    silentItemSounds.push_back(number);
                }
                writeXnb(content / item.path, soundReaders, soundPayload(audio), false);
                ++sounds;
            } else {
                writeXnb(content / item.path, fontReaders, fontPayload(unity, mobile));
                ++fonts;
            }
        } catch (const std::exception &failure) {
            throw std::runtime_error(item.path + ": " + failure.what());
        }
    }
    sheets.clear();
    const auto tracks = music(unity, workspace, content, progress);
    std::string report = "{\"complete\":true,\"missing\":[],\"required_files\":" +
        std::to_string(required.size() + 3) + ",\"converted\":{\"Images\":" + std::to_string(images) +
        ",\"Sounds\":" + std::to_string(sounds) + ",\"Fonts\":" + std::to_string(fonts) +
        "},\"music_tracks_from_apk\":" + std::to_string(tracks) +
        ",\"control_sprites\":" + std::to_string(controlSprites) + ",\"fallback_textures\":[],\"silent_item_sounds\":[";
    for (size_t i = 0; i < silentItemSounds.size(); ++i) {
        if (i) report += ',';
        report += std::to_string(silentItemSounds[i]);
    }
    report += "],\"vorbis_setup_ids\":[";
    size_t setupIndex = 0;
    for (uint32_t id : audioSetupIds) {
        if (setupIndex++) report += ',';
        report += std::to_string(id);
    }
    report += "]}\n";
    save(output / "conversion-report.json", View(reinterpret_cast<const uint8_t *>(report.data()), report.size()));
    return report;
}
}
