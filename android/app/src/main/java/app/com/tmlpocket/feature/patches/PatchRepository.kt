package app.com.tmlpocket.feature.patches

import app.com.tmlpocket.feature.mods.ModRepository
import app.com.tmlpocket.feature.mods.ModRepository.Companion.strings
import java.io.File
import org.json.JSONArray
import org.json.JSONObject

/** Declarative launch patches. Packages cannot overwrite the game or load DLLs. */
class PatchRepository(private val directory: File) {
    private val builtinSettings = File(directory, ".builtin-settings")

    private data class Builtin(
        val id: String,
        val name: String,
        val description: String,
        val version: String,
        val required: Boolean = true,
        val target: String = "",
    )

    private val builtins =
        listOf(
            Builtin("mobile-base", "资源与平台兼容", "资源校验、离线创意工坊、日志与 Android 系统服务。复用启动器兼容层。", "1.2.0"),
            Builtin(
                "mobile-touch",
                "MobileJoystick 触摸适配",
                "连接 FNA 原生多点触摸，并加载双摇杆、触屏背包、菜单和自定义界面布局。",
                "1.0.14",
            ),
            Builtin("mobile-fullscreen", "全屏显示", "按实际 Surface 同步显示与全屏呈现，适配系统栏和窗口重建。", "1.1.2"),
            Builtin("mobile-console", "控制台兼容", "适配 Android 不支持的桌面控制台操作；日志由启动器管理。", "1.0.3"),
            Builtin(
                "mobile-framerate",
                "120 FPS 呈现",
                "保持 60 Hz 游戏逻辑，提高前台绘制上限；不支持的时序会自动回退。",
                "0.1.1",
                false,
            ),
            Builtin(
                "mobile-hosting",
                "手机联机托管 · 实验性",
                "在适配版本中，可从游戏多人入口或使用 /mobilehost 创建房间。",
                "0.1.0",
                target = "2026.9.3.1",
            ),
        )

    private fun builtinState(): JSONObject =
        if (builtinSettings.isFile) JSONObject(builtinSettings.readText()) else JSONObject()

    fun enabledBuiltinIds(): List<String> {
        val state = builtinState()
        return builtins.filter { !it.required && state.optBoolean(it.id, false) }.map { it.id }
    }

    fun list(): JSONArray =
        JSONArray().also { result ->
            val state = builtinState()
            builtins.forEach { patch ->
                result.put(
                    JSONObject()
                        .put("id", patch.id)
                        .put("name", patch.name)
                        .put("description", patch.description)
                        .put("version", patch.version)
                        .put("builtin", true)
                        .put("required", patch.required)
                        .put("targetVersion", patch.target)
                        .put("launchArguments", JSONArray())
                        .put("enabled", patch.required || state.optBoolean(patch.id, false))
                )
            }
            directory
                .listFiles()
                ?.filter { it.extension == "json" }
                ?.sortedBy { it.name }
                ?.forEach { result.put(JSONObject(it.readText())) }
        }

    fun importFile(file: File) {
        require(file.length() <= 64 * 1024) { "补丁清单过大" }
        val patch = JSONObject(file.readText())
        val id = patch.getString("id")
        require(builtins.none { it.id == id }) { "内置补丁由启动器维护，不能覆盖" }
        require(patch.getInt("format") == 1 && id.matches(Regex("[a-zA-Z0-9_-]{1,80}"))) {
            "不支持的补丁清单"
        }
        require(patch.getString("name").length in 1..100) { "补丁名称无效" }
        val args = patch.getJSONArray("launchArguments").strings()
        validateArguments(args)
        val target = patch.optString("targetVersion")
        require(target.matches(Regex("[0-9.a-zA-Z_-]{0,80}"))) { "目标版本无效" }
        val clean =
            JSONObject()
                .put("format", 1)
                .put("id", id)
                .put("name", patch.getString("name"))
                .put("description", patch.optString("description").take(2000))
                .put("targetVersion", target)
                .put("launchArguments", JSONArray(args))
                .put("enabled", false)
        ModRepository.write(File(directory, "$id.json"), clean.toString(2))
    }

    fun toggle(id: String, enabled: Boolean, version: String?) {
        val builtin = builtins.firstOrNull { it.id == id }
        if (builtin != null) {
            require(!builtin.required || enabled) { "必要适配不能停用" }
            if (builtin.required) return
            require(!enabled || matchesVersion(version, builtin.target)) {
                "此补丁需要 tModLoader ${builtin.target}"
            }
            ModRepository.write(builtinSettings, builtinState().put(id, enabled).toString(2))
            return
        }
        val file = path(id)
        val patch = JSONObject(file.readText())
        require(
            !enabled ||
                patch.optString("targetVersion").isBlank() ||
                version?.startsWith(patch.getString("targetVersion")) == true
        ) {
            "补丁目标版本不匹配，请先安装匹配的 tModLoader"
        }
        patch.put("enabled", enabled)
        ModRepository.write(file, patch.toString(2))
    }

    fun delete(id: String) {
        require(builtins.none { it.id == id }) { "内置补丁不能删除，可选功能可以关闭" }
        check(path(id).delete()) { "无法删除补丁" }
    }

    fun arguments(version: String): List<String> = buildList {
        val patches = list()
        for (i in 0 until patches.length()) {
            val patch = patches.getJSONObject(i)
            if (!patch.optBoolean("enabled")) continue
            if (patch.optBoolean("builtin")) {
                require(patch.optBoolean("required") || matchesVersion(version, patch.optString("targetVersion"))) {
                    "已启用补丁 ${patch.getString("name")} 与当前版本不兼容"
                }
                continue
            }
            require(
                patch.optString("targetVersion").isBlank() ||
                    version.startsWith(patch.getString("targetVersion"))
            ) {
                "已启用补丁 ${patch.getString("name")} 与当前游戏版本不兼容"
            }
            addAll(patch.getJSONArray("launchArguments").strings().also(::validateArguments))
        }
    }

    private fun path(id: String): File {
        require(id.matches(Regex("[a-zA-Z0-9_-]{1,80}"))) { "无效补丁名称" }
        return File(directory, "$id.json")
    }

    companion object {
        private fun matchesVersion(actual: String?, target: String): Boolean =
            target.isBlank() ||
                actual
                    ?.removePrefix("v")
                    ?.split('.')
                    ?.map { it.toIntOrNull() }
                    ?.take(target.split('.').size) == target.split('.').map { it.toIntOrNull() }

        fun validateArguments(args: List<String>) {
            require(args.size <= 64 && args.all { it.length <= 1024 && '\u0000' !in it }) {
                "启动参数过长或无效"
            }
            require(
                args.none {
                    it.lowercase().substringBefore('=') in
                        setOf(
                            "-tmlsavedirectory",
                            "-savedirectory",
                            "-modpath",
                            "-config",
                            "-logfile",
                        )
                }
            ) {
                "启动器数据目录参数由系统管理，不能覆盖"
            }
        }
    }
}
