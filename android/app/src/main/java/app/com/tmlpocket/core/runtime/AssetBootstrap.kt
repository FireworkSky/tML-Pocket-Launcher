package app.com.tmlpocket.core.runtime

import android.content.Context
import android.system.Os
import android.system.OsConstants
import app.com.tmlpocket.feature.installation.GamePackageInstaller
import app.com.tmlpocket.feature.installation.MobileTerrariaPackage
import app.com.tmlpocket.feature.installation.GameStorage
import app.com.tmlpocket.feature.mods.LegacyTouchMod
import java.io.BufferedInputStream
import java.io.BufferedOutputStream
import java.io.File
import java.io.FileInputStream
import java.io.FileOutputStream
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import java.security.MessageDigest
import org.apache.commons.compress.archivers.tar.TarArchiveInputStream
import org.json.JSONObject
import org.tukaani.xz.XZInputStream

object AssetBootstrap {
    private const val TAG = "tMLPocketCoreClr"
    private const val RUNTIME_ASSET = "runtime/dotnet.tar.xz"
    private const val LAUNCHER_ASSET = "launcher"
    internal const val ANDROID_CRYPTO_LIBRARY =
        "shared/Microsoft.NETCore.App/10.0.4/libSystem.Security.Cryptography.Native.Android.so"
    internal val cryptoLibraryMode = OsConstants.S_IRUSR or OsConstants.S_IXUSR

    @Synchronized
    fun migrateTouchMod(userData: File) = LegacyTouchMod.migrate(userData)

    @Synchronized
    fun prepareLauncher(context: Context): Pair<File, File> {
        val runtimeRoot = File(context.filesDir, "runtime")
        val launcherRoot = File(context.filesDir, "launcher")
        if (!runtimeIsReady(runtimeRoot)) {
            val staging = File(context.filesDir, "runtime.staging")
            staging.deleteRecursively()
            check(staging.mkdirs()) { "无法创建运行时暂存目录" }
            extractRuntime(context, staging)
            replaceDirectory(staging, runtimeRoot)
        }
        val packagedManifest =
            context.assets.open("$LAUNCHER_ASSET/manifest.json").bufferedReader().use {
                JSONObject(it.readText().removePrefix("\uFEFF"))
            }
        if (!launcherAssetsAreReady(launcherRoot, packagedManifest)) {
            val staging = File(context.filesDir, "launcher.staging")
            staging.deleteRecursively()
            check(staging.mkdirs()) { "无法创建启动器暂存目录" }
            copyAssetTree(context, LAUNCHER_ASSET, staging)
            check(launcherAssetsAreReady(staging, packagedManifest)) { "启动器文件校验失败" }
            replaceDirectory(staging, launcherRoot)
        }
        val assembly = File(launcherRoot, packagedManifest.getString("assembly"))
        check(assembly.canonicalFile.parentFile == launcherRoot.canonicalFile && assembly.isFile) {
            "启动器程序集不存在或路径无效"
        }
        return runtimeRoot to launcherRoot
    }

    fun prepare(context: Context, arguments: Array<String>): PreparedGame {
        val storage = GameStorage(context)
        val installer = GamePackageInstaller(storage.packageRoot)
        val installed =
            installer.installation(storage.instances().versionId())
                ?: error("请先导入 Terraria 资源包和 tModLoader 发行包")
        val controlArt = MobileTerrariaPackage.requireLaunchArt(
            context, installed, installer.mobileTerrariaPackageName()
        )
        val (runtimeRoot, launcherRoot) = prepareLauncher(context)
        val assembly = File(launcherRoot, "MobileLauncher.Host.dll")
        val userData = storage.userData().apply { mkdirs() }
        migrateTouchMod(userData)
        return PreparedGame(
            assemblyPath = assembly.absolutePath,
            runtimeRoot = runtimeRoot.absolutePath,
            workingDirectory = installed.tmlRoot.absolutePath,
            arguments = arrayOf("-tmlsavedirectory", userData.absolutePath) + arguments,
            tmlAssembly = File(installed.tmlRoot, "tModLoader.dll").absolutePath,
            contentDirectory = installed.contentRoot.absolutePath,
            terrariaDirectory = installed.terrariaRoot.absolutePath,
            controlArtDirectory = controlArt?.absolutePath.orEmpty(),
            userDataDirectory = userData.absolutePath,
        )
    }

    private fun runtimeIsReady(root: File): Boolean {
        if (!root.isDirectory) return false
        val hostfxr = root.walkTopDown().firstOrNull { it.name == "libhostfxr.so" }
        val coreclr = root.walkTopDown().firstOrNull { it.name == "libcoreclr.so" }
        return hostfxr?.isFile == true && coreclr?.isFile == true
    }

    private fun launcherAssetsAreReady(root: File, manifest: JSONObject): Boolean {
        if (manifest.optInt("formatVersion") != 3) return false
        val files = manifest.optJSONObject("files") ?: return false
        return files.length() > 0 &&
            files.keys().asSequence().all { relative ->
                val file = File(root, relative)
                file.canonicalPath.startsWith(root.canonicalPath + File.separator) &&
                    file.isFile &&
                    sha256(file).equals(files.getString(relative), ignoreCase = true)
            }
    }

    private fun sha256(file: File): String =
        file.inputStream().use { input ->
            val digest = MessageDigest.getInstance("SHA-256")
            val buffer = ByteArray(DEFAULT_BUFFER_SIZE)
            while (true) {
                val count = input.read(buffer)
                if (count < 0) break
                digest.update(buffer, 0, count)
            }
            digest.digest().joinToString("") { "%02x".format(it.toInt() and 0xff) }
        }

    private fun extractRuntime(context: Context, targetRoot: File) {
        val archive = File(context.cacheDir, "dotnet.tar.xz")
        context.assets.open(RUNTIME_ASSET).use { input ->
            FileOutputStream(archive).use { output -> input.copyTo(output) }
        }

        val rootCanonical = targetRoot.canonicalFile
        TarArchiveInputStream(XZInputStream(BufferedInputStream(FileInputStream(archive)))).use {
            tar ->
            while (true) {
                val entry = tar.nextEntry ?: break
                val name = entry.name.removePrefix("dotnet/")
                if (name.isEmpty()) continue
                val output = File(targetRoot, name)
                require(output.canonicalFile.path.startsWith(rootCanonical.path + File.separator)) {
                    "runtime archive contains an unsafe path: ${entry.name}"
                }
                if (entry.isDirectory) {
                    output.mkdirs()
                } else if (!entry.isSymbolicLink && !entry.isLink) {
                    output.parentFile?.mkdirs()
                    FileOutputStream(output).use { stream ->
                        // Remove write permission before publishing any executable content.
                        if (name == ANDROID_CRYPTO_LIBRARY) Os.fchmod(stream.fd, cryptoLibraryMode)
                        BufferedOutputStream(stream).use { tar.copyTo(it) }
                    }
                    if (name != ANDROID_CRYPTO_LIBRARY && (name == "dotnet" || name.endsWith(".so"))) {
                        require(output.setExecutable(true, false)) {
                            "failed to mark runtime file executable: ${output.absolutePath}"
                        }
                    }
                }
            }
        }
        archive.delete()
    }

    private fun copyAssetTree(context: Context, assetPath: String, target: File) {
        val children = context.assets.list(assetPath).orEmpty()
        if (children.isEmpty()) {
            target.parentFile?.mkdirs()
            context.assets.open(assetPath).use { input ->
                FileOutputStream(target).use { outputStream -> input.copyTo(outputStream) }
            }
            return
        }

        for (child in children) {
            copyAssetTree(context, "$assetPath/$child", File(target, child))
        }
    }

    private fun replaceDirectory(staging: File, target: File) {
        if (target.exists()) target.deleteRecursively()
        Files.move(staging.toPath(), target.toPath(), StandardCopyOption.ATOMIC_MOVE)
    }
}
