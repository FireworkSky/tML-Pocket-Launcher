package app.com.tmlpocket.feature.launcher

import android.content.Context
import android.content.res.Configuration
import app.com.tmlpocket.feature.mods.ModRepository.Companion.strings
import app.com.tmlpocket.feature.installation.GameStorage
import app.com.tmlpocket.feature.patches.PatchRepository
import app.com.tmlpocket.feature.renderers.RendererSelection
import org.json.JSONArray
import org.json.JSONObject
import java.util.Locale

class LauncherSettings(private val context: Context) {
    private val storage = context.getSharedPreferences("launcher-settings", Context.MODE_PRIVATE)

    fun hasAcceptedNotice(): Boolean = storage.getBoolean("first-launch-notice-accepted", false)

    fun acceptNotice(): Boolean =
        hasAcceptedNotice() || storage.edit().putBoolean("first-launch-notice-accepted", true).commit()

    fun read(): JSONObject =
        runCatching { JSONObject(storage.getString("preferences", "{}") ?: "{}") }
            .getOrDefault(JSONObject())
            .also { result ->
                result.remove("threadStackMiB")
                result.remove("renderScale")
                result.remove("controlTextureSource")
                val preferences = runCatching { GameStorage(context).instances().preferences() }.getOrNull()
                if (preferences != null) {
                    for (key in INSTANCE_FIELDS) if (preferences.has(key)) result.put(key, preferences.get(key))
                }
            }

    fun localizedContext(): Context {
        val language = when (read().optString("language")) {
            "zh-CN" -> Locale.SIMPLIFIED_CHINESE
            "en" -> Locale.ENGLISH
            else -> if (context.resources.configuration.locales[0].language == "zh") {
                Locale.SIMPLIFIED_CHINESE
            } else {
                Locale.ENGLISH
            }
        }
        val configuration = Configuration(context.resources.configuration).apply { setLocale(language) }
        return context.createConfigurationContext(configuration)
    }

    fun save(patch: JSONObject): JSONObject {
        val result = read()
        for (key in patch.keys()) {
            val value = patch.get(key)
            when (key) {
                "language" -> require(value in listOf("system", "zh-CN", "en"))
                "theme" -> require(value in listOf("dark", "light", "system", "tml"))
                "themeStyle" -> require(value in listOf("default", "tml"))
                "orientation" -> require(value in listOf("landscape", "portrait"))
                "motion",
                "glassEffects",
                "touchEnabled",
                "wifiOnly",
                "highRefreshRate",
                "fixed1080p",
                "autoBackup" -> require(value is Boolean)
                "glass" -> require(value is Number && value.toInt() in 25..50)
                "touchOpacity" -> require(value is Number && value.toInt() in 20..100)
                "rendererPlugin" -> require(value is String && RendererSelection.isValidIdentity(value))
                "renderResolutionPercent" -> require(value is Int && value in listOf(60, 75, 100))
                "background" -> require(value in listOf("none", "diy", "custom"))
                "backgroundTint" ->
                    require(value is String && Regex("^#[0-9a-fA-F]{6}$").matches(value))
                "quickTools" ->
                    require(
                        value is JSONArray &&
                            value.length() == 4 &&
                            value.strings().toSet().size == 4 &&
                            value.strings().all {
                                it in
                                    setOf(
                                        "mods",
                                        "browse",
                                        "packs",
                                        "patches",
                                        "controls",
                                        "storage",
                                        "tasks",
                                        "accounts",
                                        "logs",
                                        "activity",
                                    )
                            }
                    )
                "launchArguments" -> {
                    require(value is JSONArray)
                    PatchRepository.validateArguments(value.strings())
                }
                else -> error("不支持的设置项：$key")
            }
            result.put(key, value)
        }
        if (INSTANCE_FIELDS.any { patch.has(it) }) {
            val instances = GameStorage(context).instances()
            val scoped = instances.preferences()
            for (key in INSTANCE_FIELDS) if (patch.has(key)) scoped.put(key, patch.get(key))
            instances.savePreferences(scoped)
        }
        val global = runCatching { JSONObject(storage.getString("preferences", "{}") ?: "{}") }.getOrDefault(JSONObject())
        for (key in patch.keys()) if (key !in INSTANCE_FIELDS) global.put(key, patch.get(key))
        storage.edit().putString("preferences", global.toString()).apply()
        return result
    }

    companion object {
        val INSTANCE_FIELDS = setOf("rendererPlugin", "renderResolutionPercent", "fixed1080p", "highRefreshRate", "launchArguments", "autoBackup")
    }
}
