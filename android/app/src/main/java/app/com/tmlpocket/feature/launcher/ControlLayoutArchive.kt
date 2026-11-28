package app.com.tmlpocket.feature.launcher

import android.graphics.BitmapFactory
import android.util.JsonReader
import android.util.JsonToken
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.InputStream
import java.io.OutputStream
import java.util.Base64
import java.util.zip.CRC32
import java.util.zip.ZipEntry
import java.util.zip.ZipFile
import java.util.zip.ZipOutputStream
import org.json.JSONArray
import org.json.JSONObject

/** The live profile embeds PNGs. On disk a ZIP stores each distinct image once. */
internal object ControlLayoutArchive {
    const val MAX_ARCHIVE_BYTES = 4 * 1024 * 1024
    private const val MAX_IMAGE_BYTES = 128 * 1024
    private const val PNG_PREFIX = "data:image/png;base64,"
    private const val FORMAT = "tml-pocket-controls"
    private val assetName = Regex("""assets/[0-9]{1,4}\.png""")

    data class Pack(val name: String, val profile: JSONObject)

    fun name(value: String): String = value.trim().also {
        require(it.length in 1..80 && it.none(Char::isISOControl)) { "请输入 1 至 80 个字符的布局包名称" }
    }

    fun filename(name: String): String {
        val safe = name.take(80).map { if (it in "\\/:*?\"<>|" || it.isISOControl()) '_' else it }
            .joinToString("").trim(' ', '.')
        val stem = if (safe.endsWith(".zip", ignoreCase = true)) safe.dropLast(4) else safe
        return stem.ifBlank { "Control Layout" } + ".zip"
    }

    fun write(file: File, pack: Pack) {
        val profile = pack.profile
        MobileControlsSettings.boundedJson(profile)
        validateProfile(profile)
        val assets = mutableMapOf<String, String>()
        ZipOutputStream(file.outputStream().buffered()).use { zip ->
            fun entry(path: String, bytes: ByteArray) {
                zip.putNextEntry(ZipEntry(path))
                zip.write(bytes)
                zip.closeEntry()
            }
            val manifest = JSONObject().put("format", FORMAT).put("version", 1).put("name", name(pack.name))
            entry("manifest.json", manifest.toString().toByteArray())
            for (control in controls(profile)) {
                validateControl(control)
                val uri = texture(control) ?: continue
                val path = assets.getOrPut(uri) {
                    val path = "assets/${assets.size}.png"
                    entry(path, decodeTexture(uri))
                    path
                }
                control.put("CustomTexture", path)
            }
            entry("profile.json", profile.toString().toByteArray())
        }
    }

    fun read(file: File): Pack {
        require(file.length() <= MAX_ARCHIVE_BYTES) { "布局包过大" }
        return ZipFile(file).use { zip ->
            require(zip.size() <= 1026) { "布局包文件过多" }
            val paths = mutableSetOf<String>()
            var total = 0L
            for (entry in zip.entries()) {
                val limit = when (entry.name) {
                    "manifest.json" -> 4096
                    "profile.json" -> MobileControlsSettings.MAX_BYTES
                    else -> {
                        require(assetName.matches(entry.name)) { "布局包文件路径无效" }
                        MAX_IMAGE_BYTES
                    }
                }
                require(paths.add(entry.name) && entry.size in 0..limit.toLong()) { "布局包文件重复或过大" }
                total += entry.size
                require(total <= MAX_ARCHIVE_BYTES) { "布局包解压大小超限" }
            }
            val name = manifestName(zip)
            val profile = readJson(bytes(zip, "profile.json", MobileControlsSettings.MAX_BYTES))
            validateProfile(profile)
            val assets = mutableMapOf<String, String>()
            var expandedBytes = profile.toString().toByteArray().size
            for (control in controls(profile)) {
                validateControl(control)
                val path = texture(control) ?: continue
                require(assetName.matches(path)) { "贴图路径无效" }
                val uri = assets.getOrPut(path) {
                    val png = bytes(zip, path, MAX_IMAGE_BYTES)
                    validateImage(png)
                    PNG_PREFIX + Base64.getEncoder().encodeToString(png)
                }
                expandedBytes += uri.length - path.length
                require(expandedBytes <= MobileControlsSettings.MAX_BYTES) { "触控配置文件过大（上限 2 MiB）" }
                control.put("CustomTexture", uri)
            }
            require(paths.size == assets.size + 2) { "布局包包含未使用的文件" }
            MobileControlsSettings.boundedJson(profile)
            Pack(name, profile)
        }
    }

    fun manifestName(zip: ZipFile): String {
        val manifest = readJson(bytes(zip, "manifest.json", 4096))
        require(manifest.opt("format") == FORMAT && manifest.opt("version") == 1L) { "不支持的布局包格式或版本" }
        return name(manifest.opt("name") as? String ?: error("布局包缺少名称"))
    }

    fun validateTextures(profile: JSONObject) {
        val checked = mutableSetOf<String>()
        for (control in controls(profile)) {
            val uri = texture(control) ?: continue
            if (checked.add(uri)) decodeTexture(uri)
        }
    }

    // Validate fields consumed directly by the launcher UI. Unknown properties
    // stay in the profile for other runtime versions.
    private fun validateProfile(profile: JSONObject) {
        for (key in listOf("ControlLayout", "HotbarOrientation")) {
            if (profile.has(key)) require(profile.get(key) is String || profile.get(key) is Number) { "布局选项无效：$key" }
        }
        requireIntegerIn(profile, "ActiveCustomPresetIndex", 0L..3L)
        for (key in listOf("Opacity", "ControlScale", "JoystickDeadzone", "UIFontScale", "EdgePadding",
                "LeftStickOffsetX", "LeftStickOffsetY", "RightStickOffsetX", "RightStickOffsetY")) {
            if (profile.has(key)) require(profile.get(key) is Number) { "布局数值无效：$key" }
        }
        if (!profile.isNull("InterfaceLayout")) {
            val layout = profile.getJSONObject("InterfaceLayout")
            if (!layout.isNull("Regions")) layout.getJSONObject("Regions")
        }
    }

    private fun validateControl(control: JSONObject) {
        for (key in listOf("Id", "TextureName", "KeyboardKey", "ModKeybind")) {
            if (!control.isNull(key)) require(control.get(key) is String) { "控件文本无效：$key" }
        }
        for (key in listOf("X", "Y", "Size")) {
            if (control.has(key)) require(control.get(key) is Number) { "控件数值无效：$key" }
        }
        if (control.isNull("Launcher")) return
        val data = control.getJSONObject("Launcher").getJSONObject("Data")
        for (key in listOf("type", "name", "keycode", "shape", "mode", "nativeTexture")) {
            if (!data.isNull(key)) require(data.get(key) is String) { "控件字段无效：$key" }
        }
        requireIntegerIn(data, "sectorCount", 4L..12L)
        if (!data.isNull("sectors")) {
            val sectors = data.getJSONArray("sectors")
            require(sectors.length() <= 12) { "轮盘扇区数量无效" }
            for (i in 0 until sectors.length()) {
                val sector = sectors.getJSONObject(i)
                for (key in listOf("keycode", "label")) {
                    if (!sector.isNull(key)) require(sector.get(key) is String) { "轮盘扇区无效" }
                }
            }
        }
        if (!data.isNull("joystickKeys")) {
            val keys = data.getJSONArray("joystickKeys")
            require(keys.length() <= 4 && (0 until keys.length()).all { keys.get(it) is String }) { "摇杆按键无效" }
        }
    }

    private fun requireIntegerIn(objectValue: JSONObject, key: String, range: LongRange) {
        if (!objectValue.has(key)) return
        val number = objectValue.get(key)
        require((number is Int || number is Long) && objectValue.getLong(key) in range) { "数值超出范围：$key" }
    }

    private fun controls(profile: JSONObject): Sequence<JSONObject> = sequence {
        if (profile.isNull("CustomPresets")) return@sequence
        val presets = profile.getJSONArray("CustomPresets")
        require(presets.length() <= 4) { "触控预设数量超限" }
        for (slot in 0 until presets.length()) {
            if (presets.isNull(slot)) continue
            val preset = presets.getJSONObject(slot)
            if (!preset.isNull("Name")) require(preset.get("Name") is String) { "预设名称无效" }
            if (preset.isNull("Controls")) continue
            val controls = preset.getJSONArray("Controls")
            require(controls.length() <= 256) { "单个预设最多包含 256 个控件" }
            for (index in 0 until controls.length()) yield(controls.getJSONObject(index))
        }
    }

    private fun texture(control: JSONObject): String? {
        if (control.isNull("CustomTexture")) return null
        return (control.get("CustomTexture") as? String ?: error("自定义贴图无效")).ifEmpty { null }
    }

    private fun decodeTexture(uri: String): ByteArray {
        require(uri.startsWith(PNG_PREFIX) && uri.length <= PNG_PREFIX.length + 4 * ((MAX_IMAGE_BYTES + 2) / 3)) { "自定义 PNG 贴图无效或过大" }
        return Base64.getDecoder().decode(uri.substring(PNG_PREFIX.length)).also(::validateImage)
    }

    private fun validateImage(bytes: ByteArray) {
        require(bytes.size in 24..MAX_IMAGE_BYTES) { "PNG 贴图大小无效" }
        val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
        BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
        require(bounds.outMimeType == "image/png" && bounds.outWidth in 1..256 && bounds.outHeight in 1..256) {
            "自定义贴图必须是 256 × 256 像素以内的 PNG"
        }
        requireNotNull(BitmapFactory.decodeByteArray(bytes, 0, bytes.size)) { "PNG 贴图损坏" }.recycle()
    }

    private fun bytes(zip: ZipFile, path: String, limit: Int): ByteArray {
        val entry = zip.getEntry(path) ?: error("布局包缺少文件：$path")
        val output = ByteArrayOutputStream()
        zip.getInputStream(entry).use { copyBounded(it, output, limit) }
        val bytes = output.toByteArray()
        require(bytes.size.toLong() == entry.size && CRC32().apply { update(bytes) }.value == entry.crc) { "布局包文件损坏" }
        return bytes
    }

    fun copyBounded(input: InputStream, output: OutputStream, limit: Int) {
        val buffer = ByteArray(8192)
        var remaining = limit
        while (true) {
            val count = input.read(buffer)
            if (count < 0) return
            remaining -= count
            require(remaining >= 0) { "布局包文件过大" }
            output.write(buffer, 0, count)
        }
    }

    private fun readJson(bytes: ByteArray): JSONObject = JsonReader(bytes.inputStream().reader(Charsets.UTF_8)).use { reader ->
        fun value(depth: Int): Any {
            require(depth <= 40) { "布局包 JSON 层级过深" }
            return when (reader.peek()) {
                JsonToken.BEGIN_OBJECT -> JSONObject().apply {
                    reader.beginObject()
                    while (reader.hasNext()) {
                        val key = reader.nextName()
                        require(!has(key)) { "布局包 JSON 字段重复" }
                        put(key, value(depth + 1))
                    }
                    reader.endObject()
                }
                JsonToken.BEGIN_ARRAY -> JSONArray().apply {
                    reader.beginArray()
                    while (reader.hasNext()) put(value(depth + 1))
                    reader.endArray()
                }
                JsonToken.STRING -> reader.nextString()
                JsonToken.NUMBER -> reader.nextString().let { it.toLongOrNull() ?: it.toDouble() }
                JsonToken.BOOLEAN -> reader.nextBoolean()
                JsonToken.NULL -> { reader.nextNull(); JSONObject.NULL }
                else -> error("布局包 JSON 无效")
            }
        }
        val result = value(0) as? JSONObject ?: error("布局包 JSON 缺少对象")
        require(reader.peek() == JsonToken.END_DOCUMENT) { "布局包 JSON 无效" }
        result
    }
}
