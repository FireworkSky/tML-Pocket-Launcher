package app.com.tmlpocket.feature.installation

import java.io.File
import org.json.JSONObject

internal object PackageVersionReader {
    const val UNKNOWN_TML = "tModLoader 版本未识别"
    const val UNKNOWN_TERRARIA = "Terraria 版本未识别"

    fun tmlVersion(value: String?): String? {
        if (value == null) return null
        // BuildInfo reads the first pipe-delimited value after '+'. deps.json contains Terraria's
        // version.
        val candidate =
            value.substringAfter('+', value).substringBefore('|').removePrefix("v").trim()
        return candidate.takeIf {
            it.matches(Regex("(?:20\\d{2}|9999)\\.\\d{1,2}(?:\\.\\d{1,5}){0,2}"))
        }
    }

    fun terrariaVersion(value: String?): String? =
        value?.trim()?.removePrefix("v")?.takeIf {
            it.matches(Regex("1\\.\\d{1,2}\\.\\d{1,2}(?:\\.\\d{1,2})?[a-z]?"))
        }

    fun tml(assembly: File, dependencyVersion: String?): String =
        tmlVersion(PeVersionReader.productVersion(assembly))
            ?: tmlVersion(dependencyVersion)
            ?: UNKNOWN_TML

    fun terraria(root: File, files: List<File>): String {
        for (name in listOf("Terraria.exe", "Terraria.dll", "TerrariaServer.exe")) {
            files
                .filter { it.isFile && it.name == name }
                .forEach {
                    terrariaVersion(PeVersionReader.productVersion(it))?.let { version ->
                        return version
                    }
                }
        }
        val metadata = File(root, "launcher-package.json")
        if (metadata.isFile && metadata.length() <= 20480)
            runCatching {
                    val json = JSONObject(metadata.readText())
                    if (json.optInt("format") == 1 && json.optInt("appId") == 105600)
                        terrariaVersion(json.optString("version"))
                    else null
                }
                .getOrNull()
                ?.let {
                    return it
                }
        files
            .filter { it.isFile && it.name == "gameinfo" && it.length() <= 20480 }
            .forEach {
                val lines = it.readLines()
                if (lines.firstOrNull()?.trim()?.equals("Terraria", true) == true)
                    terrariaVersion(lines.getOrNull(1))?.let { version ->
                        return version
                    }
            }
        return UNKNOWN_TERRARIA
    }

    fun installed(kind: PackageKind, recorded: String, detected: String): String =
        when (kind) {
            PackageKind.TMODLOADER ->
                if (
                    terrariaVersion(recorded) != null ||
                        recorded.startsWith("Imported ") ||
                        recorded == UNKNOWN_TML
                )
                    tmlVersion(detected) ?: UNKNOWN_TML
                else recorded.ifBlank { detected }
            PackageKind.TERRARIA, PackageKind.TERRARIA_STEAM, PackageKind.TERRARIA_GOG ->
                terrariaVersion(detected) ?: terrariaVersion(recorded) ?: UNKNOWN_TERRARIA
            PackageKind.MOBILE_TERRARIA -> recorded
        }
}
