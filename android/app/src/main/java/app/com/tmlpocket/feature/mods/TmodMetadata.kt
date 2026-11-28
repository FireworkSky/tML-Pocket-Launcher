package app.com.tmlpocket.feature.mods

import java.io.BufferedInputStream
import java.io.ByteArrayInputStream
import java.io.DataInput
import java.io.DataInputStream
import java.io.File
import java.io.IOException
import java.io.RandomAccessFile
import java.nio.channels.Channels
import java.security.MessageDigest
import java.util.Base64
import java.util.zip.Inflater
import java.util.zip.InflaterInputStream
import org.json.JSONArray
import org.json.JSONObject

internal val modInternalName = Regex("""[\p{L}\p{Nl}_][\p{L}\p{Nl}\p{M}\p{Nd}\p{Pc}]{0,127}""")

/** Reads the public TMOD header, table and Info entry; never loads an assembly. */
data class TmodMetadata(
    val name: String,
    val version: String,
    val loaderVersion: String,
    val title: String,
    val author: String,
    val description: String,
    val dependencies: List<String>,
    val side: Int,
    val size: Long,
    val optionalDependencies: List<String> = emptyList(),
) {
    fun json(enabled: Boolean) =
        JSONObject()
            .put("id", name)
            .put("title", title)
            .put("version", version)
            .put("loaderVersion", loaderVersion)
            .put("author", author)
            .put("description", description)
            .put("deps", JSONArray(dependencies))
            .put("side", side)
            .put("size", size)
            .put("enabled", enabled)
}

object TmodReader {
    fun readIcon(file: File, requested: Set<String>): Pair<String, String>? = RandomAccessFile(file, "r").use { input ->
        fun integer(): Int = Integer.reverseBytes(input.readInt())
        fun string(): String = readString(input)
        val magic = ByteArray(4).also(input::readFully)
        require(String(magic, Charsets.US_ASCII) == "TMOD") { "游戏模组格式无效" }
        string()
        input.seek(input.filePointer + 20 + 256)
        val dataLength = integer()
        require(dataLength >= 0 && input.length() - input.filePointer == dataLength.toLong()) { "模组长度不匹配或已损坏" }
        val id = string()
        if (id !in requested) return@use null
        string()
        val count = integer()
        require(count in 1..100_000) { "模组文件表超出限制" }
        data class Entry(val name: String, val size: Int, val packed: Int, val offset: Long)
        val entries = ArrayList<Entry>()
        val tableBuffer = object : BufferedInputStream(Channels.newInputStream(input.channel)) {
            val unreadBytes get() = this.count - pos
        }
        val table = DataInputStream(tableBuffer)
        var offset = 0L
        repeat(count) {
            val name = readString(table)
            val size = Integer.reverseBytes(table.readInt())
            val packed = Integer.reverseBytes(table.readInt())
            require(size >= 0 && packed >= 0) { "模组文件长度无效" }
            entries.add(Entry(name, size, packed, offset))
            offset = Math.addExact(offset, packed.toLong())
            require(offset <= dataLength) { "模组文件表越界" }
        }
        val dataStart = input.filePointer - tableBuffer.unreadBytes
        require(dataStart + offset == input.length()) { "模组数据区不完整" }
        val choices = listOf(
            "icon_workshop.gif", "icon.gif", "icon_small.gif",
            "icon_workshop.webp", "icon.webp", "icon_small.webp",
            "icon_workshop.png", "icon.png", "icon_small.png",
        )
        val icon = choices.firstNotNullOfOrNull { target -> entries.firstOrNull {
            it.name.equals(target, true) && it.size in 1..MAX_ICON_BYTES && it.packed in 1..MAX_ICON_BYTES
        } }
            ?: return@use null
        require(dataStart + icon.offset + icon.packed <= input.length()) { "模组图标数据越界" }
        input.seek(dataStart + icon.offset)
        val packed = ByteArray(icon.packed).also(input::readFully)
        val bytes = if (icon.size == icon.packed) packed else inflateIcon(packed, icon.size)
        val mime = when {
            bytes.startsWith(byteArrayOf(0x47, 0x49, 0x46, 0x38)) -> "image/gif"
            bytes.size >= 12 && String(bytes, 8, 4, Charsets.US_ASCII) == "WEBP" -> "image/webp"
            bytes.startsWith(byteArrayOf(0x89.toByte(), 0x50, 0x4e, 0x47)) -> "image/png"
            else -> return@use null
        }
        id to "data:$mime;base64,${Base64.getEncoder().encodeToString(bytes)}"
    }

    fun read(file: File, verifyHash: Boolean = false): TmodMetadata =
        RandomAccessFile(file, "r").use { input ->
            fun integer(): Int = Integer.reverseBytes(input.readInt())
            fun string(): String = readString(input)
            val magic = ByteArray(4).also(input::readFully)
            require(String(magic, Charsets.US_ASCII) == "TMOD") { "游戏模组格式无效" }
            val loader = string()
            require(
                (loader.substringBefore('.').toIntOrNull() ?: 0) >= 1 || loader.startsWith("0.11")
            ) {
                "游戏模组格式过旧，请导入适用于新版 tModLoader 的模组"
            }
            val hash = ByteArray(20).also(input::readFully)
            input.seek(input.filePointer + 256)
            val length = integer()
            val hashStart = input.filePointer
            require(length >= 0 && input.length() - hashStart == length.toLong()) { "模组长度不匹配或已损坏" }
            val name = string()
            require(name.matches(modInternalName)) { "无效的模组内部名称" }
            val version = string()
            val count = integer()
            require(count in 1..100_000) { "模组文件表超出限制" }
            data class InfoEntry(
                val length: Int,
                val compressed: Int,
                val offset: Long,
            )
            // Buffer the sequential table reads, then seek directly to the Info payload.
            val tableBuffer = object : BufferedInputStream(Channels.newInputStream(input.channel)) {
                val unreadBytes get() = this.count - pos
            }
            val table = DataInputStream(tableBuffer)
            var infoEntry: InfoEntry? = null
            var offset = 0L
            repeat(count) {
                val path = readString(table)
                val unpacked = Integer.reverseBytes(table.readInt())
                val packed = Integer.reverseBytes(table.readInt())
                require(unpacked >= 0 && packed >= 0) { "模组文件长度无效" }
                if (path == "Info") {
                    if (infoEntry != null) throw IOException("缺少唯一的 Info 元数据")
                    infoEntry = InfoEntry(unpacked, packed, offset)
                }
                offset += packed
                require(offset <= length) { "模组文件表越界" }
            }
            val dataStart = input.filePointer - tableBuffer.unreadBytes
            require(dataStart + offset == input.length()) { "模组数据区不完整" }
            val info =
                infoEntry ?: throw IOException("缺少唯一的 Info 元数据")
            require(info.length <= 1024 * 1024 && info.compressed <= 1024 * 1024) { "模组元数据过大" }
            if (verifyHash) {
                input.seek(hashStart)
                val digest = MessageDigest.getInstance("SHA-1")
                val buffer = ByteArray(64 * 1024)
                while (true) {
                    val read = input.read(buffer)
                    if (read < 0) break
                    digest.update(buffer, 0, read)
                }
                require(MessageDigest.isEqual(hash, digest.digest())) { "模组 SHA-1 完整性校验失败" }
            }
            input.seek(dataStart + info.offset)
            val raw = ByteArray(info.compressed).also(input::readFully)
            val bytes =
                if (info.length == info.compressed) raw
                else
                    InflaterInputStream(ByteArrayInputStream(raw), Inflater(true)).use { stream ->
                        val output = java.io.ByteArrayOutputStream()
                        val buffer = ByteArray(8192)
                        while (true) {
                            val read = stream.read(buffer)
                            if (read < 0) break
                            require(output.size() + read <= info.length) { "模组 Info 解压长度不匹配" }
                            output.write(buffer, 0, read)
                        }
                        require(output.size() == info.length) { "模组 Info 解压长度不匹配" }
                        output.toByteArray()
                    }
            var title = name
            var author = ""
            var description = ""
            var side = 0
            val deps = mutableListOf<String>()
            val optionalDeps = mutableListOf<String>()
            val textTags = setOf("version", "homepage", "eacPath", "buildVersion", "modSource")
            val flags =
                setOf(
                    "noCompile",
                    "!playableOnPreview",
                    "translationMod",
                    "!hideCode",
                    "!hideResources",
                    "includeSource",
                )
            DataInputStream(ByteArrayInputStream(bytes)).use { reader ->
                fun text() = readString(reader)
                while (true) {
                    val tag = text()
                    if (tag.isEmpty()) break
                    when (tag) {
                        "displayName" -> title = text()
                        "author" -> author = text()
                        "description" -> description = text()
                        "side" -> side = reader.readUnsignedByte()
                        "dllReferences",
                        "modReferences",
                        "weakReferences",
                        "sortBefore",
                        "sortAfter" -> {
                            var entry = text()
                            var items = 0
                            while (entry.isNotEmpty()) {
                                require(++items <= 4096) { "模组依赖列表过长" }
                                if (tag == "modReferences") deps.add(entry)
                                if (tag == "weakReferences") optionalDeps.add(entry)
                                entry = text()
                            }
                        }
                        in textTags -> text()
                        in flags -> Unit
                        else -> throw IOException("未知的游戏模组元数据字段：$tag，请更新启动器")
                    }
                }
            }
            TmodMetadata(
                name,
                version,
                loader,
                title,
                author,
                description,
                deps,
                side,
                file.length(),
                optionalDeps,
            )
        }

    private fun readString(input: DataInput): String {
        var size = 0
        var shift = 0
        while (true) {
            val next = input.readUnsignedByte()
            require(shift < 28 || next <= 7) { "游戏模组中的字符串长度无效" }
            size = size or ((next and 127) shl shift)
            if (next and 128 == 0) break
            shift += 7
            require(shift <= 28) { "游戏模组中的字符串长度无效" }
        }
        require(size in 0..262_144) { "游戏模组中的字符串过长" }
        return ByteArray(size).also(input::readFully).toString(Charsets.UTF_8)
    }

    private fun inflateIcon(packed: ByteArray, expectedSize: Int): ByteArray =
        InflaterInputStream(ByteArrayInputStream(packed), Inflater(true)).use { stream ->
            val output = java.io.ByteArrayOutputStream(minOf(expectedSize, MAX_ICON_BYTES))
            val buffer = ByteArray(8192)
            while (true) {
                val read = stream.read(buffer)
                if (read < 0) break
                require(output.size() + read <= expectedSize && output.size() + read <= MAX_ICON_BYTES) { "模组图标解压长度超出限制" }
                output.write(buffer, 0, read)
            }
            require(output.size() == expectedSize) { "模组图标解压长度不匹配" }
            output.toByteArray()
        }

    private fun ByteArray.startsWith(prefix: ByteArray): Boolean =
        size >= prefix.size && prefix.indices.all { this[it] == prefix[it] }

    private const val MAX_ICON_BYTES = 512 * 1024
}
