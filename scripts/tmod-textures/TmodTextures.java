package app.com.tmlpocket.feature.mods;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.*;
import java.util.zip.*;

/** TMOD v0.11+ and uncompressed XNB v5. Pixel conversion is bounded by the chunk size. */
final class TmodTextures {
    public static void main(String[] args) {
        boolean bc3 = args.length > 0 && args[0].equals("--bc3");
        int first = bc3 ? 1 : 0;
        if (args.length - first < 1 || args.length - first > 2) {
            System.err.println("用法: java -jar tmod-textures.jar [--bc3] 输入.tmod [输出.tmod]");
            System.exit(2);
        }
        try (RandomAccessFile src = new RandomAccessFile(args[first], "r")) {
            TmodTextures archive = new TmodTextures(src, bc3);
            archive.verify(src);
            System.out.printf(Locale.ROOT, "%s %s: %d BGRA4444 + %d BC3, %.2f MiB potential texture savings%n",
                archive.name, archive.version, archive.textures - archive.bc3Count, archive.bc3Count, archive.savedBytes / 1048576.0);
            if (args.length - first == 1) return;
            if (archive.bc3Count > 0) {
                Path jar = Path.of(TmodTextures.class.getProtectionDomain().getCodeSource().getLocation().toURI());
                System.load(jar.resolveSibling(System.mapLibraryName("tmod_bc3")).toString());
            }
            Path out = Path.of(args[first + 1]).toAbsolutePath();
            check(!Files.exists(out), "输出文件已存在，请选择新的文件名");
            Path temp = Files.createTempFile(out.getParent(), ".tmod-", ".tmp");
            try {
                archive.write(src, temp.toFile());
                try (RandomAccessFile result = new RandomAccessFile(temp.toFile(), "r")) {
                    new TmodTextures(result, false).verify(result);
                }
                Files.move(temp, out);
                System.out.println(out);
            } finally { Files.deleteIfExists(temp); }
        } catch (Exception | UnsatisfiedLinkError error) {
            System.err.println(error.getMessage());
            System.exit(1);
        }
    }
    private static final int MIN_PIXELS = 256 * 256;
    private static final String READER = "Microsoft.Xna.Framework.Content.Texture2DReader";
    private static final int XNB_HEADER = 10 + 1 + 1 + READER.length() + 4 + 2 + 20;

    private static native void encodeBc3Strip(byte[] pixels, byte[] blocks, int width);

    private static final class Entry {
        String name, target;
        int size, packed;
        boolean bc3;
        int pixelBytes() { return (size - 12) / (bc3 ? 4 : 2); }
        long offset, sizeField;
        boolean convert() { return target != null; }
    }

    final String loader, name, version;
    final byte[] hash = new byte[20];
    final List<Entry> entries = new ArrayList<>();
    final long payload;
    int textures, bc3Count;
    long savedBytes;

    TmodTextures(RandomAccessFile file, boolean useBc3) throws IOException {
        file.seek(0);
        BufferedInputStream buffer = new BufferedInputStream(new Slice(file, file.length()), 65536);
        DataInputStream in = new DataInputStream(buffer);
        check(in.readInt() == 0x544d4f44, "无效 TMOD 文件");
        loader = string(in);
        in.readFully(hash);
        in.readFully(new byte[256]);
        int dataLength = le(in);
        payload = file.getFilePointer() - buffer.available();
        check(dataLength == file.length() - payload, "TMOD 长度错误");
        name = string(in);
        version = string(in);
        int count = le(in);
        check(count > 0 && count <= 100000, "无效的模组文件表");
        Set<String> paths = new HashSet<>();
        long total = 0;
        for (int i = 0; i < count; i++) {
            Entry e = new Entry();
            e.name = string(in);
            paths.add(e.name.replace('\\','/').toLowerCase(Locale.ROOT));
            e.size = le(in);
            e.packed = le(in);
            check(e.size >= 0 && e.packed >= 0 && e.packed <= dataLength - total, "模组资源长度错误");
            total += e.packed;
            entries.add(e);
        }
        long pos = file.getFilePointer() - buffer.available();
        check(pos + total == file.length(), "模组资源表长度错误");
        for (Entry e : entries) {
            e.offset = pos;
            pos += e.packed;
            if (!e.name.endsWith(".rawimg") || e.size - 12 < MIN_PIXELS * 4 || (e.size - 12) % 4 != 0) continue;
            if (requiresColorReadback(name, e.name)) continue;
            String target = e.name.substring(0, e.name.length() - 7) + ".xnb";
            if (!paths.add(target.replace('\\','/').toLowerCase(Locale.ROOT))) continue;
            if (useBc3) {
                // D3D11 requires whole 4x4 blocks for the top level. Keep other sizes in 16-bit.
                file.seek(e.offset);
                try (DataInputStream raw = openRaw(e, new Slice(file, e.packed))) {
                    check(le(raw) == 1, "不支持的 RAWIMG 版本：" + e.name);
                    int w = le(raw), h = le(raw);
                    validateDimensions(e, w, h);
                    e.bc3 = w <= 16384 && w % 4 == 0 && h % 4 == 0;
                    if (e.bc3) bc3Count++;
                }
            }
            e.target = target;
            textures++;
            savedBytes += e.size - 12 - e.pixelBytes();
        }
    }

    // Calamity's deferred grayscale and framed-mask loaders read Color[] on game ticks.
    // BGRA4444 also has the wrong pixel layout for those callers.
    private static boolean requiresColorReadback(String mod, String path) {
        if (!mod.equals("CalamityMod")) return false;
        path = path.replace('\\', '/').toLowerCase(Locale.ROOT);
        if (path.startsWith("extratextures/greyscalegradients/")) {
            return path.endsWith("/blobbynoise.rawimg") || path.endsWith("pulse.rawimg");
        }
        if (!path.startsWith("tiles/") && !path.startsWith("walls/")) return false;
        return path.endsWith("glow.rawimg") || path.endsWith("glowmask.rawimg")
            || path.endsWith("lightmask.rawimg") || path.endsWith("reflect.rawimg")
            || path.endsWith("/seaprismwall_blue.rawimg") || path.endsWith("/seaprismwall_green.rawimg")
            || path.endsWith("/seaprismwall_purple.rawimg");
    }

    void write(RandomAccessFile src, File staging) throws IOException {
        check(textures > 0, "没有可转换的大贴图");
        try (RandomAccessFile dst = new RandomAccessFile(staging, "rw")) {
            dst.setLength(0);
            dst.writeBytes("TMOD");
            string(dst, loader);
            long hashPos = dst.getFilePointer();
            dst.write(new byte[280]); // Fresh hash, unsigned local package, data length.
            long start = dst.getFilePointer();
            string(dst, name);
            string(dst, version);
            le(dst, entries.size());
            for (Entry e : entries) {
                string(dst, e.convert() ? e.target : e.name);
                e.sizeField = dst.getFilePointer();
                le(dst, e.size);
                le(dst, e.packed);
            }
            byte[] pixels = new byte[65536];
            byte[] packedPixels = new byte[pixels.length / 2];
            for (Entry e : entries) {
                src.seek(e.offset);
                Slice entry = new Slice(src, e.packed);
                if (!e.convert()) {
                    while (entry.left > 0) {
                        int n = entry.read(pixels);
                        dst.write(pixels, 0, n);
                    }
                    continue;
                }
                int outputSize = XNB_HEADER + e.pixelBytes();
                long begin = dst.getFilePointer();
                OutputStream sink = new OutputStream() {
                    public void write(int b) throws IOException { dst.write(b); }
                    public void write(byte[] b, int off, int n) throws IOException { dst.write(b, off, n); }
                };
                Deflater deflater = new Deflater(Deflater.DEFAULT_COMPRESSION, true);
                try (DeflaterOutputStream deflated = new DeflaterOutputStream(sink, deflater, 65536)) {
                    writeTexture(e, entry, deflated, outputSize, pixels, packedPixels);
                } finally { deflater.end(); }
                long end = dst.getFilePointer();
                int stored = Math.toIntExact(end - begin);
                if (stored >= outputSize) {
                    // Equal lengths mean an uncompressed entry in TMOD.
                    src.seek(e.offset);
                    dst.seek(begin);
                    writeTexture(e, new Slice(src, e.packed), sink, outputSize, pixels, packedPixels);
                    end = dst.getFilePointer();
                    dst.setLength(end);
                    stored = outputSize;
                }
                dst.seek(e.sizeField);
                le(dst, outputSize);
                le(dst, stored);
                dst.seek(end);
            }
            int length = Math.toIntExact(dst.length() - start);
            byte[] digest = digest(dst, start);
            dst.seek(hashPos);
            dst.write(digest);
            dst.seek(hashPos + 276);
            le(dst, length);
            dst.getFD().sync();
        }
    }

    private void verify(RandomAccessFile file) throws IOException {
        check(MessageDigest.isEqual(hash, digest(file, payload)), "TMOD SHA-1 校验失败");
    }

    private static byte[] digest(RandomAccessFile file, long start) throws IOException {
        MessageDigest digest;
        try { digest = MessageDigest.getInstance("SHA-1"); }
        catch (NoSuchAlgorithmException failure) { throw new AssertionError(failure); }
        file.seek(start);
        byte[] bytes = new byte[65536];
        int n;
        while ((n = file.read(bytes)) != -1) digest.update(bytes, 0, n);
        return digest.digest();
    }

    private static void writeTexture(Entry e, Slice slice, OutputStream sink, int size,
                                     byte[] pixels, byte[] packed) throws IOException {
        try (DataInputStream in = openRaw(e, slice)) {
            check(le(in) == 1, "不支持的 RAWIMG 版本：" + e.name);
            int w = le(in), h = le(in);
            validateDimensions(e, w, h);
            DataOutputStream out = new DataOutputStream(sink);
            out.writeBytes("XNBw");
            out.writeByte(5);
            out.writeByte(0);
            le(out, size);
            out.writeByte(1);
            string(out, READER);
            le(out, 0);
            out.writeByte(0);
            out.writeByte(1);
            le(out, e.bc3 ? 6 : 3); // SurfaceFormat.Dxt5 or SurfaceFormat.Bgra4444
            le(out, w);
            le(out, h);
            le(out, 1);
            le(out, e.pixelBytes());
            if (e.bc3) {
                byte[] strip = pixels.length >= w * 16 ? pixels : new byte[w * 16];
                byte[] blocks = packed.length >= w * 4 ? packed : new byte[w * 4];
                for (int y = 0; y < h; y += 4) {
                    in.readFully(strip, 0, w * 16);
                    encodeBc3Strip(strip, blocks, w);
                    out.write(blocks, 0, w * 4);
                }
            } else for (int left = e.size - 12; left > 0;) {
                int n = Math.min(left, pixels.length);
                in.readFully(pixels, 0, n);
                for (int i = 0, j = 0; i < n; i += 4) {
                    packed[j++] = (byte)((nibble(pixels[i+1]) << 4) | nibble(pixels[i+2]));
                    packed[j++] = (byte)((nibble(pixels[i+3]) << 4) | nibble(pixels[i]));
                }
                out.write(packed, 0, n / 2);
                left -= n;
            }
            check(in.read() == -1, "贴图数据长度错误：" + e.name);
        }
    }

    private static void validateDimensions(Entry e, int w, int h) throws IOException {
        check(w > 0 && h > 0 && (long) w * h == (e.size - 12) / 4, "贴图尺寸无效：" + e.name);
    }

    private static DataInputStream openRaw(Entry e, Slice slice) {
        if (e.packed == e.size) return new DataInputStream(slice);
        return new DataInputStream(new InflaterInputStream(slice, new Inflater(true), 32768) {
            @Override public void close() throws IOException {
                try { super.close(); } finally { inf.end(); }
            }
        });
    }

    private static int nibble(byte value) { return ((value & 255) + 8) / 17; }
    private static int le(DataInput in) throws IOException { return Integer.reverseBytes(in.readInt()); }
    private static void le(DataOutput out, int n) throws IOException { out.writeInt(Integer.reverseBytes(n)); }

    static String string(DataInput in) throws IOException {
        int n = 0;
        for (int shift = 0; shift <= 28; shift += 7) {
            int b = in.readUnsignedByte();
            check(shift < 28 || b <= 7, "无效的字符串长度");
            n |= (b & 127) << shift;
            if (b < 128) {
                check(n <= 262144, "模组字符串过长");
                byte[] data = new byte[n];
                in.readFully(data);
                return new String(data, StandardCharsets.UTF_8);
            }
        }
        throw new IOException("无效的模组字符串");
    }

    private static void string(DataOutput out, String s) throws IOException {
        byte[] bytes = s.getBytes(StandardCharsets.UTF_8);
        int n = bytes.length;
        while (n >= 128) { out.writeByte(n | 128); n >>>= 7; }
        out.writeByte(n);
        out.write(bytes);
    }

    private static void check(boolean ok, String message) throws IOException {
        if (!ok) throw new IOException(message);
    }

    private static final class Slice extends InputStream {
        private final RandomAccessFile file;
        long left;
        Slice(RandomAccessFile file, long length) { this.file = file; left = length; }
        public int read() throws IOException {
            if (left == 0) return -1;
            left--;
            return file.readUnsignedByte();
        }
        public int read(byte[] bytes, int off, int n) throws IOException {
            if (n == 0) return 0;
            if (left == 0) return -1;
            n = (int)Math.min(n, left);
            file.readFully(bytes, off, n);
            left -= n;
            return n;
        }
    }
}
