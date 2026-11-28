package app.com.tmlpocket.feature.installation

import java.io.File

enum class PackageKind {
    TERRARIA,
    TERRARIA_STEAM,
    TERRARIA_GOG,
    MOBILE_TERRARIA,
    TMODLOADER,
}

enum class TerrariaSource(val id: String) {
    STEAM("steam"),
    GOG("gog"),
    ANDROID("android"),

    ;

    companion object {
        fun fromId(id: String): TerrariaSource =
            entries.firstOrNull { it.id == id } ?: error("未知 Terraria 来源")
    }
}

data class InstalledTmlVersion(
    val id: String,
    val version: String,
    val active: Boolean,
    val available: Boolean,
)

data class InstalledGame(
    val tmlRoot: File,
    val terrariaRoot: File,
    val contentRoot: File,
    val version: String,
    val terrariaSource: TerrariaSource = TerrariaSource.STEAM,
    val requiresSteamVerification: Boolean = terrariaSource == TerrariaSource.STEAM,
    val legacySteamTransaction: String? = null,
)
