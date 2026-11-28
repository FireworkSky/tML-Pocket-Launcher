package app.com.tmlpocket.feature.mods

import java.io.File
import java.nio.file.Files
import java.util.UUID
import java.util.zip.ZipFile
import org.json.JSONObject

class WorkshopContentInstaller(
    private val userData: File,
    private val mods: ModRepository,
) {
    fun install(appId: Int, id: String, title: String, payload: File, loaderVersion: String? = null): String? {
        require(id.matches(Regex("[0-9]{1,20}"))) { "无效的工坊项目编号" }
        require(payload.isFile && payload.length() in 1..MAX_ARCHIVE_SIZE) { "工坊下载文件为空或超过大小限制" }
        return when (appId) {
            TML_APP_ID -> installMod(payload, loaderVersion)
            TERRARIA_APP_ID -> {
                installTerraria(id, title, payload)
                null
            }
            else -> throw IllegalArgumentException("不支持此 Steam 工坊应用")
        }
    }

    private fun installMod(payload: File, loaderVersion: String?): String {
        val packageStage = File(userData, ".workshop-mod-${UUID.randomUUID()}")
        try {
            val modFile = if (isZip(payload)) {
                extract(payload, packageStage)
                selectMod(packageStage, loaderVersion)
            } else payload
            val metadata = TmodReader.read(modFile, verifyHash = true)
            require(metadata.name != LegacyTouchMod.ID) { "MobileJoystick 已作为启动器游戏补丁提供" }
            val staged = File(userData, ".${metadata.name}-${UUID.randomUUID()}.tmod")
            try {
                Files.copy(modFile.toPath(), staged.toPath())
                mods.importFile(staged)
                return metadata.name
            } finally {
                staged.delete()
            }
        } finally {
            packageStage.deleteRecursively()
        }
    }

    private fun selectMod(directory: File, loaderVersion: String?): File {
        val candidates = directory.walkTopDown()
            .filter { it.isFile && it.extension.equals("tmod", true) }
            .mapNotNull { file -> runCatching { TmodReader.read(file) to file }.getOrNull() }
            .toList()
        require(candidates.isNotEmpty()) { "工坊项目中没有可读取的游戏模组" }
        require(candidates.map { it.first.name }.distinct().size == 1) { "工坊项目包含多个不同的模组" }
        val ordered = candidates.sortedByDescending { versionKey(it.first.loaderVersion) }
        val active = loaderVersion?.let(::versionKey)
        return (ordered.firstOrNull { active != null && versionKey(it.first.loaderVersion) <= active } ?: ordered.first()).second
    }

    private fun versionKey(value: String): Long =
        value.removePrefix("v").split('.').let { parts ->
            (0..3).fold(0L) { version, index ->
                version * 10_000 + (parts.getOrNull(index)?.toIntOrNull()?.coerceIn(0, 9_999) ?: 0)
            }
        }

    private fun installTerraria(id: String, title: String, payload: File) {
        val staging = File(userData, ".workshop-stage-${UUID.randomUUID()}")
        try {
            if (isZip(payload)) {
                extract(payload, staging)
            } else {
                check(staging.mkdirs()) { "无法创建工坊暂存目录" }
                val targetName = payload.name.takeIf { it.matches(Regex("[A-Za-z0-9 _.-]{1,180}\\.(wld|twld)", RegexOption.IGNORE_CASE)) }
                    ?: "world.wld"
                Files.copy(payload.toPath(), File(staging, targetName).toPath())
            }

            val manifest = File(staging, "workshop.json")
            require(manifest.isFile && manifest.length() <= 64 * 1024) { "工坊项目缺少有效的 workshop.json 类型清单" }
            val contentType = JSONObject(manifest.readText(Charsets.UTF_8)).optString("ContentType")
            val (directoryName, destinationName) = when (contentType) {
                "World" -> "Worlds" to "${safeName(title)} [$id]"
                "ResourcePack" -> {
                    require(File(staging, "pack.json").isFile) { "资源包缺少 pack.json" }
                    "ResourcePacks" to "${safeName(title)} [$id]"
                }
                else -> throw IllegalArgumentException("不支持的 Terraria 工坊内容类型：$contentType")
            }
            if (contentType == "World") require(staging.listFiles().orEmpty().any { it.extension.equals("wld", true) }) {
                "世界项目没有 .wld 文件"
            }
            val root = File(userData, directoryName)
            ensureSafeDirectory(root)
            if (contentType == "World") commitWorld(staging, root, destinationName)
            else commit(staging, File(root, destinationName))
        } finally {
            staging.deleteRecursively()
        }
    }

    private fun isZip(file: File): Boolean = file.inputStream().use { input ->
        input.read() == 0x50 && input.read() == 0x4b
    }

    private fun extract(archive: File, destination: File) {
        check(destination.mkdirs()) { "无法创建工坊暂存目录" }
        val canonicalRoot = destination.canonicalFile
        var total = 0L
        val seen = mutableSetOf<String>()
        ZipFile(archive).use { zip ->
            require(zip.size() in 1..MAX_ENTRIES) { "工坊压缩包条目数量无效" }
            val entries = zip.entries()
            while (entries.hasMoreElements()) {
                val entry = entries.nextElement()
                val normalized = entry.name.replace('\\', '/')
                require(!normalized.startsWith('/') && !Regex("^[A-Za-z]:").containsMatchIn(normalized)) { "工坊压缩包包含绝对路径" }
                val pieces = normalized.split('/').filter { it.isNotEmpty() }
                require(pieces.isNotEmpty() && pieces.none { it == "." || it == ".." || '\u0000' in it }) {
                    "工坊压缩包包含不安全路径"
                }
                val relative = pieces.joinToString("/")
                require(seen.add(relative.lowercase())) { "工坊压缩包包含重复路径" }
                val target = File(destination, relative).canonicalFile
                require(target.toPath().startsWith(canonicalRoot.toPath())) { "工坊压缩包路径越界" }
                if (entry.isDirectory) {
                    check(target.isDirectory || target.mkdirs()) { "无法创建工坊目录" }
                    continue
                }
                val size = entry.size
                require(size in 0..MAX_ARCHIVE_SIZE && total + size <= MAX_ARCHIVE_SIZE) { "工坊压缩包解压后超过大小限制" }
                check(target.parentFile?.isDirectory == true || target.parentFile?.mkdirs() == true) { "无法创建工坊目录" }
                var written = 0L
                zip.getInputStream(entry).use { input ->
                    target.outputStream().use { output ->
                        val buffer = ByteArray(32 * 1024)
                        while (true) {
                            val count = input.read(buffer)
                            if (count < 0) break
                            written += count
                            total += count
                            require(total <= MAX_ARCHIVE_SIZE && written <= MAX_ARCHIVE_SIZE) { "工坊压缩包解压后超过大小限制" }
                            output.write(buffer, 0, count)
                        }
                    }
                }
                require(size < 0 || written == size) { "工坊压缩包文件长度不匹配" }
            }
        }
        val roots = destination.listFiles().orEmpty()
        if (roots.size == 1 && roots.single().isDirectory) {
            val nested = roots.single()
            nested.listFiles().orEmpty().forEach { child ->
                Files.move(child.toPath(), File(destination, child.name).toPath())
            }
            nested.delete()
        }
    }

    private fun ensureSafeDirectory(directory: File) {
        require(!Files.isSymbolicLink(directory.toPath())) { "安装目录不能是符号链接" }
        check(directory.isDirectory || directory.mkdirs()) { "无法创建安装目录" }
    }

    private fun commit(staging: File, target: File) {
        require(!Files.isSymbolicLink(target.toPath())) { "安装目标不能是符号链接" }
        val incoming = File(target.parentFile, ".${target.name}.incoming-${UUID.randomUUID()}")
        val backup = File(target.parentFile, ".${target.name}.backup-${UUID.randomUUID()}")
        try {
            check(staging.renameTo(incoming) || copyTree(staging, incoming)) { "无法准备工坊安装文件" }
            if (target.exists()) check(target.renameTo(backup)) { "无法备份已安装的工坊内容" }
            if (!incoming.renameTo(target)) {
                if (backup.exists()) backup.renameTo(target)
                throw IllegalStateException("无法提交工坊内容安装")
            }
            backup.deleteRecursively()
        } finally {
            incoming.deleteRecursively()
            if (backup.exists() && !target.exists()) backup.renameTo(target)
        }
    }

    private fun commitWorld(staging: File, root: File, name: String) {
        val world = staging.listFiles().orEmpty().singleOrNull { it.extension.equals("wld", true) }
            ?: throw IllegalArgumentException("世界项目必须只包含一个 .wld 文件")
        val sidecar = File(staging, world.nameWithoutExtension + ".twld").takeIf { it.isFile }
        val sources = listOf(world, sidecar)
        val targets = listOf(File(root, "$name.wld"), File(root, "$name.twld"))
        require(targets.none { Files.isSymbolicLink(it.toPath()) }) { "世界安装目标不能是符号链接" }
        val incoming = targets.map { File(root, ".${it.name}.incoming-${UUID.randomUUID()}") }
        val backups = targets.map { File(root, ".${it.name}.backup-${UUID.randomUUID()}") }
        val backedUp = mutableListOf<Int>()
        val installed = mutableListOf<Int>()
        var committed = false
        try {
            sources.indices.forEach { index -> sources[index]?.let { Files.copy(it.toPath(), incoming[index].toPath()) } }
            targets.indices.forEach { index ->
                if (targets[index].exists()) {
                    check(targets[index].renameTo(backups[index])) { "无法备份已安装的世界文件" }
                    backedUp.add(index)
                }
                if (sources[index] != null) {
                    check(incoming[index].renameTo(targets[index])) { "无法安装世界文件" }
                    installed.add(index)
                }
            }
            committed = true
        } finally {
            incoming.forEach { it.delete() }
            if (committed) backups.forEach { it.delete() }
            else {
                installed.forEach { index -> targets[index].delete() }
                backedUp.forEach { index -> if (backups[index].exists()) backups[index].renameTo(targets[index]) }
            }
        }
    }

    private fun copyTree(source: File, destination: File): Boolean {
        if (source.isDirectory) {
            if (!destination.mkdirs() && !destination.isDirectory) return false
            return source.listFiles().orEmpty().all { copyTree(it, File(destination, it.name)) }
        }
        return runCatching { Files.copy(source.toPath(), destination.toPath()); true }.getOrDefault(false)
    }

    private fun safeName(value: String): String = value
        .replace(Regex("[\\x00-\\x1f<>:\"/\\\\|?*]"), "_")
        .trim().trim('.').take(100).ifBlank { "Workshop Item" }

    companion object {
        const val TERRARIA_APP_ID = 105600
        const val TML_APP_ID = 1281930
        private const val MAX_ARCHIVE_SIZE = 1024L * 1024 * 1024
        private const val MAX_ENTRIES = 20_000
    }
}
