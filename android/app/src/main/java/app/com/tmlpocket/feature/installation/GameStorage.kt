package app.com.tmlpocket.feature.installation

import android.content.Context
import android.os.Environment
import app.com.tmlpocket.feature.files.FileBrowserAccess
import java.io.File
import java.nio.file.Files
import java.nio.file.LinkOption.NOFOLLOW_LINKS
import java.nio.file.Path
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.nio.file.attribute.BasicFileAttributes
import java.util.concurrent.CancellationException

internal class GameStorage(private val context: Context) {
    private val preferences = context.getSharedPreferences("game-storage", Context.MODE_PRIVATE)
    val sharedEnabled: Boolean get() = preferences.getBoolean("shared", false)
    val granted: Boolean get() = FileBrowserAccess(context).granted()

    @Suppress("DEPRECATION")
    val sharedRoot: File get() = File(Environment.getExternalStorageDirectory(), "tML Pocket Launcher")
    val sharedExists: Boolean get() = granted && SharedGameMigration.isManaged(sharedRoot.toPath())

    fun root(): File {
        if (!sharedEnabled) return context.filesDir
        check(granted && Environment.getExternalStorageState() == Environment.MEDIA_MOUNTED) {
            "共享游戏目录不可访问，请恢复文件访问权限。"
        }
        return sharedRoot.also {
            check(it.canWrite() && SharedGameMigration.isManaged(it.toPath())) {
                "共享游戏目录不可用，请检查存储。"
            }
        }
    }

    fun instances() = GameInstances(root())

    fun userData() = instances().userData()

    val packageRoot: File get() = File(context.filesDir, "game-packages")

    fun preparePackages(cancelled: () -> Boolean, progress: (String, Int) -> Unit) {
        val shared = if (sharedEnabled) root() else null
        SharedGameMigration.relocatePackages(context.filesDir, shared, packageRoot, cancelled, progress)
    }

    fun enableShared(cancelled: () -> Boolean) {
        check(granted && Environment.getExternalStorageState() == Environment.MEDIA_MOUNTED) {
            "共享游戏目录不可访问，请恢复文件访问权限。"
        }
        SharedGameMigration.prepare(context.filesDir, sharedRoot, cancelled)
        val instances = GameInstances(sharedRoot)
        instances.ensureModsDirectory(GameInstances.DEFAULT)
        val active = instances.activeId()
        if (active != GameInstances.DEFAULT) instances.ensureModsDirectory(active)
        check(preferences.edit().putBoolean("shared", true).commit()) { "无法保存游戏存储设置。" }
    }
}

internal object SharedGameMigration {
    private const val MARKER = ".tml-pocket-storage"
    private val entries = listOf("userdata", "instances", "instances.json", "backups")
    private val packageEntries = listOf("games", "installed-packages.json")
    private const val CLEANUP = ".legacy-package-origin"

    fun relocatePackages(
        privateRoot: File,
        sharedRoot: File?,
        destination: File,
        cancelled: () -> Boolean,
        progress: (String, Int) -> Unit,
    ) {
        val target = destination.toPath()
        val cleanup = target.resolve(CLEANUP)
        if (!Files.exists(target, NOFOLLOW_LINKS)) {
            val source = sharedRoot?.takeIf { root ->
                packageEntries.any { Files.exists(root.toPath().resolve(it), NOFOLLOW_LINKS) } ||
                    File(root, ".package-install-transaction.json").isFile
            } ?: privateRoot
            val staging = Files.createTempDirectory(target.parent, ".game-package-migration-")
            try {
                GamePackageInstaller(source).recoverBeforeRelocation()
                if (packageEntries.any { File(source, it).exists() }) progress("正在迁移游戏资源…", -1)
                for (name in packageEntries) {
                    copyEntry(source.toPath().resolve(name), staging.resolve(name), cancelled)
                }
                Files.write(staging.resolve(CLEANUP), byteArrayOf(if (source == sharedRoot) 1 else 0))
                if (cancelled()) throw CancellationException("操作已取消")
                Files.move(staging, target, ATOMIC_MOVE)
            } finally {
                if (Files.exists(staging, NOFOLLOW_LINKS)) removeTree(staging)
            }
        }
        check(Files.isDirectory(target, NOFOLLOW_LINKS)) { "游戏资源目录无效。" }
        if (!Files.isRegularFile(cleanup, NOFOLLOW_LINKS)) return
        val source = if (Files.readAllBytes(cleanup).single() == 1.toByte()) sharedRoot ?: return else privateRoot
        for (name in packageEntries) {
            val legacy = source.toPath().resolve(name)
            if (Files.exists(legacy, NOFOLLOW_LINKS)) removeTree(legacy)
        }
        Files.delete(cleanup)
    }

    fun isManaged(directory: Path): Boolean =
        Files.isDirectory(directory, NOFOLLOW_LINKS) &&
            Files.isRegularFile(directory.resolve(MARKER), NOFOLLOW_LINKS)

    fun prepare(source: File, destination: File, cancelled: () -> Boolean) {
        val target = destination.toPath()
        if (cancelled()) throw CancellationException("操作已取消")
        if (Files.exists(target, NOFOLLOW_LINKS)) {
            check(isManaged(target)) {
                "共享游戏目录已存在，但不是启动器创建的目录。"
            }
            return
        }
        val staging = Files.createTempDirectory(target.parent, ".tml-storage-")
        try {
            for (name in entries) {
                copyEntry(source.toPath().resolve(name), staging.resolve(name), cancelled)
            }
            val instances = GameInstances(staging.toFile())
            instances.ensureModsDirectory(GameInstances.DEFAULT)
            val active = instances.activeId()
            if (active != GameInstances.DEFAULT) instances.ensureModsDirectory(active)
            if (cancelled()) throw CancellationException("操作已取消")
            Files.createFile(staging.resolve(".nomedia"))
            Files.createFile(staging.resolve(MARKER))
            check(!Files.exists(target, NOFOLLOW_LINKS)) { "共享游戏目录已存在，请重试。" }
            Files.move(staging, target, ATOMIC_MOVE)
        } finally {
            if (Files.exists(staging, NOFOLLOW_LINKS)) removeTree(staging)
        }
    }

    private fun copyEntry(source: Path, destination: Path, cancelled: () -> Boolean) {
        if (!Files.exists(source, NOFOLLOW_LINKS)) return
        Files.walk(source).use { paths ->
            paths.forEach { path ->
                if (cancelled()) throw CancellationException("操作已取消")
                val copy = destination.resolve(source.relativize(path))
                val attributes = Files.readAttributes(path, BasicFileAttributes::class.java, NOFOLLOW_LINKS)
                if (attributes.isDirectory) {
                    Files.createDirectory(copy)
                } else {
                    check(attributes.isRegularFile) { "游戏目录包含不支持的文件。" }
                    Files.copy(path, copy, NOFOLLOW_LINKS)
                    check(Files.size(copy) == attributes.size()) { "游戏文件复制不完整。" }
                    Files.setLastModifiedTime(copy, attributes.lastModifiedTime())
                }
            }
        }
    }

    private fun removeTree(root: Path) {
        Files.walk(root).use { paths ->
            paths.sorted(Comparator.reverseOrder()).forEach { Files.delete(it) }
        }
    }
}
