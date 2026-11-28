package app.com.tmlpocket.feature.installation

import android.content.Context
import android.content.pm.PackageInfo
import android.content.pm.PackageManager
import app.com.tmlpocket.R
import java.io.DataInputStream
import java.io.File
import java.util.zip.ZipFile
import org.json.JSONObject
import org.json.JSONArray

internal object MobileTerrariaPackage {
    const val DATA = "assets/bin/Data/"
    const val TARGET_TML = "2026.09.3.1"
    val shaders = listOf("PixelShader.xnb", "ScreenShader.xnb", "TileShader.xnb")
    const val CONTROL_ART_DIRECTORY = "mobile-controls"
    const val CONTROL_ART_REVISION = "1"

    fun convertedControlArt(terrariaRoot: File): File? {
        val art = File(terrariaRoot, CONTROL_ART_DIRECTORY)
        val revision = File(art, "revision")
        return art.takeIf { revision.isFile && revision.readText() == CONTROL_ART_REVISION }
    }

    fun controlArt(context: Context, terrariaRoot: File): File? =
        convertedControlArt(terrariaRoot)
            ?: runCatching { GamePackageInstaller(GameStorage(context).packageRoot).convertedTouchArt() }.getOrNull()
            ?: MobileTerrariaConverter.extractInstalledControlArt(context)

    fun requireLaunchArt(context: Context, game: InstalledGame, mobilePackage: String?): File? {
        if (game.terrariaSource != TerrariaSource.ANDROID) {
            // Desktop content supplies the game itself. Mobile art is optional.
            return runCatching { controlArt(context, game.terrariaRoot) }.getOrNull()
        }
        val info = requireInstalled(context, mobilePackage ?: MobileTerrariaChannel.GOOGLE_PLAY.packageName)
        check(inspectArchives(archives(info))) { context.getString(R.string.mobile_art_unsupported) }
        val art = try {
            convertedControlArt(game.terrariaRoot)?.takeIf { hasControlSprites(context, it) }
                ?: MobileTerrariaConverter.extractInstalledControlArt(context, info)
        } catch (failure: Exception) {
            throw IllegalStateException(context.getString(R.string.mobile_art_unavailable), failure)
        }
        check(hasControlSprites(context, art)) { context.getString(R.string.mobile_art_unavailable) }
        return art
    }

    internal fun hasControlSprites(context: Context, root: File): Boolean {
        if (!root.isDirectory) return false
        val recipes = context.assets.open("mobile-controls/control-sprites.json").bufferedReader().use {
            JSONObject(it.readText()).getJSONObject("assets")
        }
        return recipes.keys().asSequence().all { File(root, "$it.rawimg").length() > 12 }
    }

    fun interface Progress {
        fun update(stage: String, current: Int, total: Int): Boolean
    }

    fun inspect(context: Context): JSONObject {
        val packages = MobileTerrariaChannel.entries.mapNotNull { channel ->
            val info = readInstalled(context, channel.packageName) ?: return@mapNotNull null
            val archives = archives(info)
            val status = when {
                !channel.hasTrustedSignature(context.packageManager) -> "signature-mismatch"
                runCatching { inspectArchives(archives) }.getOrDefault(false) -> "unity"
                else -> "unsupported"
            }
            JSONObject()
                .put("status", status)
                .put("packageName", info.packageName)
                .put("channel", channel.id)
                .put("version", info.versionName.orEmpty())
                .put("apkCount", archives.size)
        }
        val selected = packages.firstOrNull { it.optString("status") == "unity" }
            ?: packages.firstOrNull()
        return (selected?.let { JSONObject(it.toString()) }
            ?: JSONObject().put("status", "not-installed"))
            .put("packages", JSONArray(packages))
    }

    // Refresh installed content on the next explicit conversion after changing output rules.
    private const val CONVERTER_REVISION = 8

    fun fingerprint(info: PackageInfo): String =
        "${info.packageName}:${info.longVersionCode}:${info.lastUpdateTime}:$CONVERTER_REVISION"

    fun installed(context: Context, packageName: String? = null): PackageInfo? {
        val channels = MobileTerrariaChannel.entries.filter { packageName == null || it.packageName == packageName }
        val candidates = channels.mapNotNull { channel ->
            readInstalled(context, channel.packageName)?.takeIf { channel.hasTrustedSignature(context.packageManager) }
        }
        return candidates.firstOrNull { runCatching { inspectArchives(archives(it)) }.getOrDefault(false) }
            ?: candidates.firstOrNull()
    }

    class NotInstalled : IllegalStateException("本机未检测到 Google Play 版 Terraria PE。")

    fun requireInstalled(context: Context, packageName: String? = null): PackageInfo {
        val name = packageName ?: MobileTerrariaChannel.GOOGLE_PLAY.packageName
        val channel = requireNotNull(MobileTerrariaChannel.entries.find { it.packageName == name }) {
            "仅支持 Google Play 版 Terraria PE，请重新检测并转换资源"
        }
        val info = readInstalled(context, name) ?: throw NotInstalled()
        check(channel.hasTrustedSignature(context.packageManager)) { context.getString(R.string.mobile_signature_mismatch) }
        return info
    }

    @Suppress("DEPRECATION")
    private fun readInstalled(context: Context, packageName: String): PackageInfo? = try {
        context.packageManager.getPackageInfo(packageName, 0)
    } catch (_: PackageManager.NameNotFoundException) {
        null
    }

    fun archives(info: PackageInfo): List<File> {
        val application = checkNotNull(info.applicationInfo)
        return (listOf(application.sourceDir) + application.splitSourceDirs.orEmpty()).map(::File)
    }

    internal fun inspectArchives(archives: List<File>): Boolean {
        var bundle = false
        var streams = false
        for (file in archives) {
            ZipFile(file).use { zip ->
                val entry = zip.getEntry(DATA + "data.unity3d")
                if (!bundle && entry != null && !entry.isDirectory && entry.size >= 8) {
                    val signature = ByteArray(8)
                    DataInputStream(zip.getInputStream(entry)).use { it.readFully(signature) }
                    bundle = signature.contentEquals("UnityFS\u0000".toByteArray(Charsets.US_ASCII))
                }
                streams = streams || (zip.getEntry(DATA + "resources.resource")?.size ?: 0) > 0
            }
        }
        return bundle && streams
    }
}
