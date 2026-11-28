package app.com.tmlpocket.feature.installation

import app.com.tmlpocket.feature.mods.ModRepository
import java.io.File
import java.nio.file.Files
import java.util.UUID
import org.json.JSONArray
import org.json.JSONObject

internal class GameInstances(private val root: File) {
    private val catalog = File(root, "instances.json")

    private fun read(): JSONObject {
        if (!catalog.exists()) return JSONObject().put("active", DEFAULT).put("entries", JSONArray()
            .put(JSONObject().put("id", DEFAULT).put("name", "默认实例")))
        require(!Files.isSymbolicLink(catalog.toPath()) && catalog.length() <= 1024 * 1024) { "实例目录记录无效" }
        return JSONObject(catalog.readText()).also { state ->
            val entries = state.getJSONArray("entries")
            require(entries.length() in 1..100) { "实例数量无效" }
            val ids = mutableSetOf<String>()
            for (index in 0 until entries.length()) {
                val entry = entries.getJSONObject(index)
                checkId(entry.getString("id"))
                require(ids.add(entry.getString("id"))) { "实例标识重复" }
            }
            require(DEFAULT in ids && state.getString("active") in ids) { "当前实例不存在" }
        }
    }

    fun activeId(): String = read().getString("active")

    private fun entry(state: JSONObject, id: String): JSONObject {
        checkId(id)
        val entries = state.getJSONArray("entries")
        return (0 until entries.length()).map { entries.getJSONObject(it) }
            .firstOrNull { it.getString("id") == id } ?: error("实例不存在")
    }

    fun userData(): File = directory(activeId())

    fun ensureModsDirectory(id: String) {
        val mods = File(directory(id), "Mods").toPath()
        require(!Files.isSymbolicLink(mods)) { "模组目录不能使用符号链接" }
        Files.createDirectories(mods)
    }

    private fun directory(id: String): File {
        checkId(id)
        val instances = File(root, "instances")
        val instance = File(instances, id)
        val result = if (id == DEFAULT) File(root, "userdata") else File(instance, "userdata")
        require(listOf(instances, instance, result).none { Files.isSymbolicLink(it.toPath()) }) { "实例目录不能使用符号链接" }
        return result
    }

    private fun activeEntry(): JSONObject {
        val state = read()
        return entry(state, state.getString("active"))
    }

    fun versionId(): String? = activeEntry().optString("versionId").takeIf { it.isNotBlank() }

    fun list(): JSONArray {
        val state = read()
        val entries = state.getJSONArray("entries")
        return JSONArray((0 until entries.length()).map { index ->
            val value = JSONObject(entries.getJSONObject(index).toString())
            value.put("active", value.getString("id") == state.getString("active"))
                .put("directory", directory(value.getString("id")).absolutePath)
        })
    }

    fun create(name: String, versionId: String?, preferences: JSONObject): JSONObject {
        val state = read()
        require(state.getJSONArray("entries").length() < 100) { "最多创建 100 个实例" }
        val value = JSONObject().put("id", UUID.randomUUID().toString()).put("name", validName(name))
            .put("preferences", preferences)
        versionId?.let { TmlVersionCatalog.checkId(it); value.put("versionId", it) }
        ensureModsDirectory(value.getString("id"))
        state.getJSONArray("entries").put(value)
        ModRepository.write(catalog, state.toString(2))
        return value
    }

    fun select(id: String) {
        val state = read()
        require(!entry(state, id).optBoolean("archived")) { "请先恢复归档实例" }
        state.put("active", id)
        ModRepository.write(catalog, state.toString(2))
    }

    fun rename(id: String, name: String) {
        val state = read()
        entry(state, id).put("name", validName(name))
        ModRepository.write(catalog, state.toString(2))
    }

    fun archive(id: String, archived: Boolean) {
        require(id != DEFAULT) { "默认实例不能归档" }
        val state = read()
        entry(state, id).put("archived", archived)
        if (archived && state.getString("active") == id) state.put("active", DEFAULT)
        ModRepository.write(catalog, state.toString(2))
    }

    fun setVersion(id: String?) {
        id?.let(TmlVersionCatalog::checkId)
        val state = read()
        entry(state, state.getString("active")).put("versionId", id)
        ModRepository.write(catalog, state.toString(2))
    }

    fun preferences(): JSONObject = activeEntry().optJSONObject("preferences") ?: JSONObject()

    fun savePreferences(value: JSONObject) {
        val state = read()
        entry(state, state.getString("active")).put("preferences", value)
        ModRepository.write(catalog, state.toString(2))
    }

    private fun validName(name: String): String = name.trim().also {
        require(it.length in 1..80 && it.none { c -> c.code < 32 }) { "请输入 1–80 个字符的实例名称" }
    }

    private fun checkId(id: String) {
        require(id == DEFAULT || runCatching { UUID.fromString(id).toString() == id }.getOrDefault(false)) { "无效实例标识" }
    }

    companion object { const val DEFAULT = "default" }
}
