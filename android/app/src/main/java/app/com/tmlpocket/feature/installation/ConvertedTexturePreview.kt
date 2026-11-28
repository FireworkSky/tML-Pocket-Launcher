package app.com.tmlpocket.feature.installation

import java.io.ByteArrayInputStream
import java.io.DataInputStream
import java.io.File
import java.nio.ByteBuffer
import java.nio.ByteOrder

/** Reads the small RGBA textures written by the mobile converter, without a GPU. */
internal object ConvertedTexturePreview {
    fun rawImage(file: File): ByteArray {
        require(file.length() in 10..1_000_000)
        val bytes = file.readBytes()
        val header = ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN)
        require(header.get().toInt() == 88 && header.get().toInt() == 78 && header.get().toInt() == 66)
        header.get()
        require(header.get().toInt() == 5)
        val flags = header.get().toInt() and 255
        require(header.int == bytes.size && flags and 0x40 == 0)
        val body = if (flags and 0x80 != 0) {
            val size = header.int
            require(size in 1..1_000_000)
            decodeLzx(bytes, 14, size)
        } else bytes.copyOfRange(10, bytes.size)
        val input = DataInputStream(ByteArrayInputStream(body))
        fun integer() = Integer.reverseBytes(input.readInt())
        fun seven(): Int {
            var value = 0
            for (shift in 0..28 step 7) {
                val next = input.readUnsignedByte()
                value = value or ((next and 127) shl shift)
                if (next and 128 == 0) return value
            }
            error("Invalid XNB integer")
        }
        require(seven() == 1)
        val readerLength = seven()
        require(readerLength in 1..1024)
        val reader = ByteArray(readerLength).also(input::readFully).toString(Charsets.UTF_8)
        require(reader.substringBefore(',').endsWith("Texture2DReader"))
        require(integer() == 0 && seven() == 0 && seven() == 1)
        require(integer() == 0) // SurfaceFormat.Color
        val width = integer()
        val height = integer()
        require(width in 1..512 && height in 1..512 && integer() == 1)
        val length = integer()
        require(length == width * height * 4 && input.available() == length)
        return ByteBuffer.allocate(12 + length).order(ByteOrder.LITTLE_ENDIAN)
            .putInt(1).putInt(width).putInt(height)
            .put(ByteArray(length).also(input::readFully)).array()
    }

    // The converter emits independent XNB bit frames with verbatim LZX blocks
    // and a 64 KiB window; see android/native/mobile-terraria/lzx.cpp.
    private fun decodeLzx(bytes: ByteArray, start: Int, size: Int): ByteArray {
        class Bits(private val data: ByteArray) {
            private var position = 0
            private var word = 0
            private var remaining = 0
            fun read(count: Int): Int {
                var value = 0
                repeat(count) {
                    if (remaining == 0) {
                        require(position + 1 < data.size)
                        word = (data[position].toInt() and 255) or ((data[position + 1].toInt() and 255) shl 8)
                        position += 2
                        remaining = 16
                    }
                    value = (value shl 1) or ((word ushr --remaining) and 1)
                }
                return value
            }
        }
        class Tree(lengths: IntArray) {
            private val symbols = HashMap<Int, Int>()
            init {
                val counts = IntArray(17)
                val codes = IntArray(17)
                lengths.forEach { require(it in 0..16); if (it > 0) counts[it]++ }
                for (i in 1..16) codes[i] = (codes[i - 1] + counts[i - 1]) shl 1
                lengths.forEachIndexed { symbol, length ->
                    if (length > 0) symbols[(1 shl length) or codes[length]++] = symbol
                }
            }
            fun read(bits: Bits): Int {
                var code = 1
                repeat(16) {
                    code = (code shl 1) or bits.read(1)
                    symbols[code]?.let { return it }
                }
                error("Invalid XNB Huffman code")
            }
        }
        fun lengths(bits: Bits, values: IntArray, startIndex: Int, end: Int) {
            val tree = Tree(IntArray(20) { bits.read(4) })
            var index = startIndex
            while (index < end) {
                when (val symbol = tree.read(bits)) {
                    17, 18 -> {
                        val count = bits.read(if (symbol == 17) 4 else 5) + if (symbol == 17) 4 else 20
                        require(count <= end - index)
                        repeat(count) { values[index++] = 0 }
                    }
                    19 -> {
                        val count = bits.read(1) + 4
                        val value = (values[index] + 17 - tree.read(bits)) % 17
                        require(count <= end - index)
                        repeat(count) { values[index++] = value }
                    }
                    else -> { values[index] = (values[index] + 17 - symbol) % 17; index++ }
                }
            }
        }
        val result = ByteArray(size)
        val main = IntArray(512)
        val length = IntArray(249)
        val recent = intArrayOf(1, 1, 1)
        var position = start
        var output = 0
        fun byte(): Int { require(position < bytes.size); return bytes[position++].toInt() and 255 }
        while (output < size) {
            var frame = 32768
            val first = byte()
            val block = if (first == 255) {
                frame = (byte() shl 8) or byte()
                (byte() shl 8) or byte()
            } else (first shl 8) or byte()
            require(frame in 1..minOf(32768, size - output) && block > 0 && block <= bytes.size - position)
            val bits = Bits(bytes.copyOfRange(position, position + block))
            position += block
            if (output == 0) require(bits.read(1) == 0)
            require(bits.read(3) == 1 && bits.read(24) == frame)
            lengths(bits, main, 0, 256)
            lengths(bits, main, 256, 512)
            lengths(bits, length, 0, 249)
            val mainTree = Tree(main)
            val lengthTree = Tree(length)
            val end = output + frame
            while (output < end) {
                val symbol = mainTree.read(bits)
                if (symbol < 256) { result[output++] = symbol.toByte(); continue }
                val slot = (symbol - 256) ushr 3
                val primary = (symbol - 256) and 7
                val count = 2 + primary + if (primary == 7) lengthTree.read(bits) else 0
                val distance = if (slot < 3) {
                    val value = recent[slot]
                    recent[slot] = recent[0]
                    recent[0] = value
                    value
                } else {
                    val extra = if (slot == 3) 0 else (slot ushr 1) - 1
                    val base = if (slot == 3) 3 else (2 + (slot and 1)) shl extra
                    val value = base - 2 + bits.read(extra)
                    recent[2] = recent[1]; recent[1] = recent[0]; recent[0] = value
                    value
                }
                require(distance in 1..minOf(output, 65536) && count <= end - output)
                repeat(count) { result[output] = result[output - distance]; output++ }
            }
        }
        require(position == bytes.size)
        return result
    }
}
