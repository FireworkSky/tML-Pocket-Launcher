package app.com.tmlpocket.feature.launcher

import app.com.tmlpocket.feature.mods.ModRepository
import java.io.File
import org.json.JSONArray
import org.json.JSONObject

internal class MobileControlsSettings(userData: File) {
    private val target = File(userData, "MobileControls.json")
    private val legacy = File(userData, "ModConfigs/MobileJoystick_MobileJoystickConfig.json")
    fun read(): JSONObject = withoutRemovedControls(snapshot())

    fun snapshot(): JSONObject {
        val file = if (target.isFile) target else legacy
        if (!file.isFile) return JSONObject()
        require(file.length() <= MAX_BYTES) { "触控配置文件过大" }
        return JSONObject(file.readText().removePrefix("\uFEFF"))
    }

    fun save(value: JSONObject): JSONObject {
        val current = read()
        val category = value.getString("category")
        val changes = value.getJSONObject("settings")
        val allowed = when (category) {
            "controls" -> CONTROL_FIELDS
            "interface" -> INTERFACE_FIELDS
            "layout" -> CONTROL_FIELDS + INTERFACE_FIELDS
            else -> error("未知的触控设置类别")
        }
        require(changes.keys().asSequence().all { it in allowed }) { "触控设置字段无效" }
        changes.keys().forEach { key -> current.put(key, changes.get(key)) }
        withoutRemovedControls(current)
        ControlLayoutArchive.validateTextures(current)
        ModRepository.write(target, boundedJson(current))
        return current
    }

    fun applyPack(profile: JSONObject): JSONObject {
        // An absent layout field restores the game's default. Preserve extension
        // settings introduced locally after this snapshot was made.
        val previous = snapshot()
        for (key in previous.keys()) {
            if (!profile.has(key) && key !in PROFILE_FIELDS) profile.put(key, previous.get(key))
        }
        ModRepository.write(target, boundedJson(profile))
        return withoutRemovedControls(profile)
    }

    private fun withoutRemovedControls(profile: JSONObject): JSONObject {
        val presets = profile.optJSONArray("CustomPresets") ?: return profile
        for (index in 0 until presets.length()) {
            val preset = presets.optJSONObject(index) ?: continue
            val controls = preset.optJSONArray("Controls") ?: continue
            val supported = JSONArray()
            for (position in 0 until controls.length()) {
                val control = controls.optJSONObject(position) ?: continue
                val kind = control.optString("Kind", "0").lowercase()
                if (kind !in SUPPORTED_KINDS) continue
                val launcher = control.optJSONObject("Launcher")
                val data = launcher?.optJSONObject("Data")
                if (launcher != null && (data == null || data.optString("type") !in SUPPORTED_TYPES)) continue
                launcher?.remove("Assets")
                data?.remove("texture")
                data?.remove("useTextureAlphaHitTest")
                if (data != null) {
                    clearRemovedBinding(data)
                    data.optJSONArray("sectors")?.let { sectors ->
                        for (index in 0 until sectors.length()) {
                            val sector = sectors.optJSONObject(index) ?: continue
                            sector.remove("iconPath")
                            clearRemovedBinding(sector)
                        }
                    }
                    data.optJSONArray("joystickKeys")?.let { keys ->
                        for (index in 0 until keys.length()) {
                            if (keys.optString(index) in REMOVED_BINDINGS) keys.put(index, "UNKNOWN")
                        }
                    }
                }
                supported.put(control)
            }
            if (controls.length() > 0 && supported.length() == 0) {
                presets.put(index, JSONObject.NULL)
                if (index == profile.optInt("ActiveCustomPresetIndex", 0) &&
                    profile.optString("ControlLayout").lowercase() in setOf("2", "custom")) {
                    profile.put("ControlLayout", 1)
                }
            } else {
                preset.put("Controls", supported)
            }
        }
        return profile
    }

    private fun clearRemovedBinding(data: JSONObject) {
        if (data.optString("keycode") in REMOVED_BINDINGS) data.put("keycode", "UNKNOWN")
    }

    companion object {
        const val MAX_BYTES = 2 * 1024 * 1024
        fun boundedJson(profile: JSONObject): String = profile.toString().also {
            require(it.toByteArray(Charsets.UTF_8).size <= MAX_BYTES) { "触控配置文件过大（上限 2 MiB）" }
        }
        private val SUPPORTED_TYPES = setOf("joystick", "button", "radialmenu")
        private val SUPPORTED_KINDS = SUPPORTED_TYPES + setOf("0", "1", "2")
        private val REMOVED_BINDINGS = setOf("SPECIAL_TOUCHPAD_RIGHT_BUTTON", "MOUSE_WHEEL_UP", "MOUSE_WHEEL_DOWN")
        private val CONTROL_FIELDS = setOf(
            "ControlLayout", "ActiveCustomPresetIndex", "CustomPresets", "ControlScale",
            "LeftStickOffsetX", "LeftStickOffsetY", "RightStickOffsetX", "RightStickOffsetY",
            "Opacity", "JoystickDeadzone", "JoystickUpJumps", "EnablePinchZoom", "ShowTopButtons",
            "EnableTouchInput", "HideGameControls",
        )
        private val INTERFACE_FIELDS = setOf("UIFontScale", "HotbarOrientation", "InterfaceLayout", "EdgePadding")
        private val PROFILE_FIELDS = CONTROL_FIELDS + INTERFACE_FIELDS + setOf(
            "EnableMouseFallback", "ShowMouseCursor", "QuickStackToNearbyChests",
            "AimDistance", "CursorSensitivity", "CursorOpacity", "AimReticleSize", "AimCrosshairSize",
        )
    }
}
