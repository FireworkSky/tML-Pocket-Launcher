package app.com.tmlpocket.feature.installation

import android.content.Context
import android.content.pm.PackageInfo
import android.os.Looper
import android.util.Base64
import app.com.tmlpocket.R
import app.com.tmlpocket.feature.stores.StoreRuntime
import java.io.DataOutputStream
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.util.concurrent.CancellationException
import java.util.zip.ZipFile
import org.json.JSONArray
import org.json.JSONObject

internal object MobileTerrariaConverter {
    private val assetPath = Regex("""(?:Images|Sounds|Fonts)/(?:[\w. -]+/)*[\w. -]+\.xnb""")
    private val legacySplash = Regex("""Images/logo_[1-8]\.xnb""")

    private external fun convertNative(
        workspace: String,
        output: String,
        progress: MobileTerrariaPackage.Progress,
    ): String

    private external fun extractControlsNative(workspace: String, output: String): Int

    fun extractInstalledControlArt(context: Context): File? =
        MobileTerrariaPackage.installed(context)?.let { extractInstalledControlArt(context, it) }

    @Synchronized
    fun extractInstalledControlArt(context: Context, info: PackageInfo): File {
        val cache = File(context.cacheDir, "mobile-control-art")
        check(cache.isDirectory || cache.mkdirs())
        val fingerprint = MobileTerrariaPackage.fingerprint(info).replace(':', '-')
        val destination = File(cache, fingerprint)
        if (MobileTerrariaPackage.hasControlSprites(context, destination)) return destination

        val workspace = Files.createTempDirectory(cache.toPath(), ".extract-").toFile()
        try {
            extractUnity(MobileTerrariaPackage.archives(info), workspace, listOf("data.unity3d")) { _, _, _ -> true }
            val sprites = prepareControlRecipes(context, workspace)
            val staged = File(workspace, "result")
            System.loadLibrary("terraria_converter")
            val extracted = extractControlsNative(workspace.absolutePath, staged.absolutePath)
            completeControlArt(context, staged, sprites, extracted)
            check(MobileTerrariaPackage.fingerprint(MobileTerrariaPackage.requireInstalled(context, info.packageName)) ==
                MobileTerrariaPackage.fingerprint(info)) { "提取期间 Terraria PE 已更新，请重试" }
            if (destination.isDirectory && !MobileTerrariaPackage.hasControlSprites(context, destination)) {
                Files.move(destination.toPath(), File(workspace, "incomplete").toPath(), StandardCopyOption.ATOMIC_MOVE)
            }
            try {
                Files.move(staged.toPath(), destination.toPath(), StandardCopyOption.ATOMIC_MOVE)
            } catch (failure: IOException) {
                if (!MobileTerrariaPackage.hasControlSprites(context, destination)) throw failure
            }
            return destination
        } finally {
            workspace.deleteRecursively()
        }
    }

    @Synchronized
    fun convertInstalled(
        context: Context,
        destination: File,
        packageName: String,
        tmlContent: File,
        progress: MobileTerrariaPackage.Progress,
    ): JSONObject {
        check(Looper.myLooper() != Looper.getMainLooper()) { "Run conversion on a worker thread" }
        val info = MobileTerrariaPackage.requireInstalled(context, packageName)
        val archives = MobileTerrariaPackage.archives(info)
        require(MobileTerrariaPackage.inspectArchives(archives)) { "不支持的 Terraria PE 安装包结构" }
        val parent = checkNotNull(destination.parentFile)
        val outputParent = parent.canonicalFile.toPath()
        val privateRoot = context.filesDir.canonicalFile.toPath()
        require(outputParent.startsWith(privateRoot)) {
            "Conversion output must be in the launcher storage directory"
        }
        check(parent.isDirectory || parent.mkdirs()) { "Could not create the conversion directory" }
        require(!destination.exists()) { "Conversion output already exists" }

        val workspace = Files.createTempDirectory(parent.toPath(), ".conversion-").toFile()
        try {
            check(workspace.usableSpace >= 2L * 1024 * 1024 * 1024) { "本机转换需要至少 2 GiB 可用空间" }
            val assembly = File(tmlContent.parentFile, "tModLoader.dll")
            val resources = StoreRuntime.request(context, "tml.resources", JSONObject().put("assembly", assembly.absolutePath)) as JSONObject
            fun read(name: String) = Base64.decode(resources.getString(name), Base64.DEFAULT)
            val profileTextures = prepareInventory(context, read("layout"), workspace, tmlContent)
            extractUnity(archives, workspace, listOf("data.unity3d", "resources.resource"), progress)
            val controlSprites = prepareControlRecipes(context, workspace)
            val staged = File(workspace, "result")
            System.loadLibrary("terraria_converter")
            val report = JSONObject(convertNative(workspace.absolutePath, staged.absolutePath, progress))
            check(report.getBoolean("complete"))
            val drawnSprites = completeControlArt(
                context, File(staged, MobileTerrariaPackage.CONTROL_ART_DIRECTORY),
                controlSprites, report.getInt("control_sprites"),
            )
            report.put("drawn_control_sprites", JSONArray(drawnSprites))
            val tmlSprites = listOf("ButtonCollapsed", "DropdownIcon", "ButtonError").associateWith(::read)
            MobileUiTextures.write(tmlSprites, staged, profileTextures.generated)
            report.put("tml_textures", JSONArray(profileTextures.tml))
            report.put("generated_ui_textures", JSONArray(profileTextures.generated.keys))
            report.put("required_files", report.getInt("required_files") + profileTextures.generated.size)
            report.getJSONObject("converted").let { converted ->
                converted.put("Images", converted.getInt("Images") + profileTextures.generated.size)
            }
            File(staged, "conversion-report.json").writeText(report.toString())
            check(MobileTerrariaPackage.fingerprint(MobileTerrariaPackage.requireInstalled(context, packageName)) ==
                MobileTerrariaPackage.fingerprint(info)) { "转换期间 Terraria PE 已更新，请重新转换" }
            File(staged, "${MobileTerrariaPackage.CONTROL_ART_DIRECTORY}/revision")
                .writeText(MobileTerrariaPackage.CONTROL_ART_REVISION)
            check(progress.update("verify", 1, 1)) { "Mobile Terraria conversion cancelled" }
            Files.move(staged.toPath(), destination.toPath(), StandardCopyOption.ATOMIC_MOVE)
            return report
        } finally {
            workspace.deleteRecursively()
        }
    }

    private fun extractUnity(
        archives: List<File>,
        workspace: File,
        names: List<String>,
        progress: MobileTerrariaPackage.Progress,
    ) {
        val pending = names.toMutableSet()
        for (archive in archives) {
            ZipFile(archive).use { zip ->
                for (name in names) {
                    if (name !in pending) continue
                    val entry = zip.getEntry(MobileTerrariaPackage.DATA + name) ?: continue
                    require(!entry.isDirectory && entry.size in 1..MAX_UNITY_ENTRY) { "无效的 Unity 资源大小" }
                    zip.getInputStream(entry).use { input ->
                        copyBounded(input, File(workspace, name), entry.size) {
                            if (!progress.update("extract", names.indexOf(name), names.size)) {
                                throw CancellationException("Android 资源转换已取消")
                            }
                        }
                    }
                    pending.remove(name)
                }
            }
            if (pending.isEmpty()) break
        }
        check(pending.isEmpty()) { "安装包缺少 Unity 资源文件" }
    }

    private fun completeControlArt(context: Context, root: File, sprites: List<String>, extracted: Int): List<String> {
        val missing = sprites.filter { !File(root, "$it.rawimg").isFile }
        check(extracted == sprites.size - missing.size)
        if (missing.isEmpty()) return missing
        val drawings = TouchDrawings.load(context)
        for (name in missing) {
            check(drawings.recipes.has(name)) { "$name: Missing mobile UI sprite and drawing" }
            drawings.writeRaw(name, File(root, "$name.rawimg"))
        }
        return missing
    }

    private fun prepareControlRecipes(context: Context, workspace: File): List<String> {
        val recipes = context.assets.open("mobile-controls/control-sprites.json").bufferedReader().use {
            JSONObject(it.readText()).getJSONObject("assets")
        }
        val names = recipes.keys().asSequence().toList()
        DataOutputStream(File(workspace, "control-recipes").outputStream().buffered()).use { output ->
            fun number(value: Int) = output.writeInt(Integer.reverseBytes(value))
            fun coordinates(objectValue: JSONObject, key: String, size: Int, absent: Int = 0) {
                val array = objectValue.optJSONArray(key)
                check(array == null || array.length() == size)
                repeat(size) { number(array?.getInt(it) ?: absent) }
            }

            number(1)
            number(names.size)
            for (name in names) {
                val recipe = recipes.getJSONObject(name)
                val layers = recipe.optJSONArray("layers") ?: JSONArray().put(recipe)
                val palette = recipe.optJSONArray("palette")
                output.write(name.toByteArray(Charsets.US_ASCII))
                output.writeByte(0)
                coordinates(recipe, "size", 2)
                val flags = (if (recipe.optBoolean("flip_y")) 1 else 0) or
                    (if (palette != null) 2 else 0) or
                    (if (recipe.optBoolean("gold_contour")) 4 else 0) or
                    (if (recipe.optBoolean("iris")) 8 else 0)
                output.writeByte(flags)
                output.writeByte(layers.length())
                repeat(layers.length()) {
                    val layer = layers.getJSONObject(it)
                    number(layer.getString("sprite").toUInt(16).toInt())
                    coordinates(layer, "crop", 4)
                    coordinates(layer, "frame", 2)
                    coordinates(layer, "at", 2, -1)
                }
                if (palette != null) {
                    check(palette.length() == 2)
                    repeat(2) { index ->
                        val color = palette.getJSONArray(index)
                        check(color.length() == 4)
                        repeat(4) { output.writeByte(color.getInt(it)) }
                    }
                }
            }
        }
        return names
    }

    private data class ProfileTextures(
        val tml: List<String>,
        val generated: Map<String, Pair<Int, Int>>,
    )

    private fun prepareInventory(context: Context, layout: ByteArray, workspace: File, tmlContent: File): ProfileTextures {
        val inventory = sortedMapOf<String, Pair<Int, Int>>()
        val tmlTextures = mutableListOf<String>()
        val generated = sortedMapOf<String, Pair<Int, Int>>()
        for ((id, name) in mapOf(R.raw.pe_music_settings to "TerrariaMusic.xgs", R.raw.pe_music_cues to "Sound Bank.xsb")) {
            context.resources.openRawResource(id).use { copyBounded(it, File(workspace, name), MAX_PROFILE_ENTRY) }
        }
        layout.inputStream().buffered().use { input ->
            input.mark(2)
            val first = input.read()
            val second = input.read()
            input.reset()
            val encoding = if (first == 255 && second == 254 || first == 254 && second == 255)
                Charsets.UTF_16 else Charsets.UTF_8
            input.reader(encoding).buffered().useLines { lines ->
                val rows = lines.iterator()
                val header = rows.next().removePrefix("\uFEFF").split('\t')
                val path = header.indexOf("Path")
                val width = header.indexOf("Width")
                val height = header.indexOf("Height")
                require(minOf(path, width, height) >= 0) { "无效的贴图资源清单" }
                for (line in rows) {
                    if (line.isBlank()) continue
                    val fields = line.split('\t')
                    val name = fields[path] + ".xnb"
                    checkPath(name, "Images/")
                    if (legacySplash.matches(name) ||
                        name.startsWith("Images/UI/Minimap/") && "/MinimapButton_" in name) continue
                    val size = fields[width].toInt() to fields[height].toInt()
                    require(size.first > 0 && size.second > 0)
                    inventory[name] = size
                }
            }
        }
        val metadata = context.resources.openRawResource(R.raw.pe_conversion_inventory).bufferedReader().use {
            JSONObject(it.readText())
        }
        inventory["Images/UI/ModPackLocation.xnb"] = 14 to 14
        val assets = metadata.getJSONArray("soundsAndFonts")
        repeat(assets.length()) { index ->
            val name = assets.getString(index)
            checkPath(name, if (name.startsWith("Sounds/")) "Sounds/" else "Fonts/")
            inventory[name] = 0 to 0
        }
        metadata.getJSONArray("tmlTextures").let { textures ->
            for (index in 0 until textures.length()) {
                val name = textures.getString(index)
                checkPath(name, "Images/")
                require(inventory.remove(name) != null) { "Invalid tModLoader texture in profile: $name" }
                check(hasTexture(tmlContent, name)) { "所选 tModLoader 缺少贴图：$name" }
                tmlTextures.add(name)
            }
        }
        metadata.getJSONArray("uiTextures").let { textures ->
            for (index in 0 until textures.length()) {
                val name = textures.getString(index)
                checkPath(name, "Images/UI/")
                generated[name] = requireNotNull(inventory.remove(name)) { "Invalid generated UI texture: $name" }
            }
        }
        require(inventory.isNotEmpty()) { "资源清单为空" }
        File(workspace, "inventory.tsv").bufferedWriter().use { writer ->
            for ((name, size) in inventory) writer.write("$name\t${size.first}\t${size.second}\n")
        }
        return ProfileTextures(tmlTextures, generated)
    }

    fun hasTmlTextures(terrariaRoot: File, tmlContent: File): Boolean {
        if (MobileTerrariaPackage.shaders.any { !hasTexture(tmlContent, it) }) return false
        val report = JSONObject(File(terrariaRoot, "conversion-report.json").readText())
        val textures = report.optJSONArray("tml_textures") ?: return true
        return (0 until textures.length()).all { index ->
            val name = textures.getString(index)
            checkPath(name, "Images/")
            hasTexture(tmlContent, name)
        }
    }

    private fun hasTexture(content: File, name: String): Boolean =
        File(content, name).let { it.isFile && it.length() > 10 }

    private fun checkPath(name: String, prefix: String) {
        require(name.startsWith(prefix) && assetPath.matches(name) &&
            name.split('/').none { it == "." || it == ".." }) { "无效的资源路径：$name" }
    }

    private fun copyBounded(input: InputStream, file: File, limit: Long, progress: () -> Unit = {}) {
        file.parentFile?.mkdirs()
        file.outputStream().use { output ->
            val buffer = ByteArray(256 * 1024)
            var size = 0L
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                size += count
                check(size <= limit) { "资源文件超出大小限制" }
                progress()
                output.write(buffer, 0, count)
            }
        }
    }

    private const val MAX_UNITY_ENTRY = 1024L * 1024 * 1024
    private const val MAX_PROFILE_ENTRY = 64L * 1024 * 1024
}
