package app.com.tmlpocket.feature.mods

import java.io.File
import java.nio.file.Files
import java.nio.file.StandardCopyOption
import org.json.JSONArray
import org.json.JSONObject
import org.json.JSONTokener

class ModRepository(private val userData: File) {
    private val root = File(userData, "Mods")
    private val enabledFile = File(root, "enabled.json")
    private val packsFile = File(userData, "launcher-mod-packs.json")

    fun icons(ids: List<String>): JSONObject {
        require(ids.size in 1..30) { "一次最多读取 30 个模组图标" }
        val result = JSONObject()
        val pending = ids.toMutableSet()
        require(pending.all(modInternalName::matches)) { "无效模组名称" }
        for (file in root.listFiles().orEmpty().sortedBy { it.name }) {
            if (pending.isEmpty()) break
            if (file.extension != "tmod" || Files.isSymbolicLink(file.toPath()) || !file.isFile) continue
            val (id, icon) = runCatching { TmodReader.readIcon(file, pending) }.getOrNull() ?: continue
            result.put(id, icon)
            pending.remove(id)
        }
        return result
    }

    fun list(): JSONArray {
        val enabled = enabled()
        val result = JSONArray()
        val scanned = root
            .listFiles()
            ?.filter { it.isFile && it.extension == "tmod" }
            ?.sortedBy { it.name }
            ?.map { file -> file to runCatching { TmodReader.read(file) } }
            .orEmpty()
        val installed = scanned.mapNotNull { (_, read) -> read.getOrNull() }.associateBy { it.name }
        scanned.forEach { (file, read) ->
            read.fold(
                onSuccess = { meta ->
                    result.put(
                        meta
                            .json(meta.name in enabled)
                            .put("changed", file.lastModified())
                            .put("dependencies", dependencyStatus(meta, installed, enabled))
                    )
                },
                onFailure = { failure ->
                    result.put(
                        JSONObject()
                            .put("id", file.nameWithoutExtension)
                            .put("title", file.name)
                            .put("size", file.length())
                            .put("changed", file.lastModified())
                            .put("error", failure.message)
                            .put("enabled", false)
                            .put("deps", JSONArray())
                    )
                },
            )
        }
        return result
    }

    private fun dependencyStatus(
        mod: TmodMetadata,
        installed: Map<String, TmodMetadata>,
        enabled: Set<String>,
    ): JSONArray {
        val dependencies = JSONArray()
        fun append(references: List<String>, optional: Boolean) {
            references.distinct().forEach { reference ->
                val (name, requiredVersion) = dependencyReference(reference)
                val local = installed[name]
                val status = when {
                    local == null -> "missing"
                    requiredVersion.isNotEmpty() && !versionAtLeast(local.version, requiredVersion) -> "outdated"
                    name !in enabled -> "disabled"
                    else -> "enabled"
                }
                dependencies.put(
                    JSONObject()
                        .put("name", name)
                        .put("title", local?.title ?: name)
                        .put("requiredVersion", requiredVersion)
                        .put("installedVersion", local?.version.orEmpty())
                        .put("optional", optional)
                        .put("status", status)
                )
            }
        }
        append(mod.dependencies, false)
        append(mod.optionalDependencies, true)
        return dependencies
    }

    fun importFile(file: File) {
        val meta = TmodReader.read(file, verifyHash = true)
        require(meta.name != LegacyTouchMod.ID) { "MobileJoystick 已作为启动器游戏补丁提供" }
        check(root.isDirectory || root.mkdirs()) { "无法创建模组目录" }
        val canonical = File(root, "${meta.name}.tmod")
        val existing = root.listFiles().orEmpty().filter { candidate ->
            candidate.isFile && candidate.extension == "tmod" && !Files.isSymbolicLink(candidate.toPath()) &&
                runCatching { TmodReader.read(candidate).name }.getOrNull() == meta.name
        }
        require(existing.size <= 1) { "发现多份同名模组 ${meta.name}，请保留一份后重新导入" }
        val destination = existing.singleOrNull() ?: canonical
        require(!Files.isSymbolicLink(destination.toPath())) { "模组目标不能是符号链接" }
        if (existing.isEmpty() && destination.isFile) {
            require(runCatching { TmodReader.read(destination) }.isFailure) { "目标文件名已被其他模组占用：${destination.name}" }
        }
        Files.move(file.toPath(), destination.toPath(), StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING)
    }

    fun createImportTemp(): File {
        check(userData.isDirectory || userData.mkdirs()) { "无法创建模组暂存目录" }
        return File.createTempFile(".mod-import-", ".tmp", userData)
    }

    fun setEnabled(ids: Set<String>, value: Boolean, replace: Boolean = false) {
        val mods = metadata()
        require(ids.all { it in mods }) { "找不到所选模组" }
        val selected = if (replace) mutableSetOf() else enabled().toMutableSet()
        if (value) {
            val validated = mutableSetOf<String>()
            fun enable(id: String, visiting: MutableSet<String>) {
                if (id in validated) return
                require(visiting.add(id)) { "模组存在循环依赖：$id" }
                val mod = mods[id] ?: error("缺少依赖模组：$id")
                mod.dependencies.forEach { reference ->
                    val (dependency, requiredVersion) = dependencyReference(reference)
                    val installed = mods[dependency] ?: error("缺少依赖模组：$reference")
                    require(
                        requiredVersion.isEmpty() ||
                            versionAtLeast(installed.version, requiredVersion)
                    ) {
                        "依赖版本不满足：$reference，已安装 ${installed.version}"
                    }
                    enable(dependency, visiting)
                }
                visiting.remove(id)
                selected.add(id)
                validated.add(id)
            }
            ids.forEach { enable(it, mutableSetOf()) }
        } else {
            selected.removeAll(ids)
            var changed: Boolean
            do {
                changed =
                    selected.removeAll { id ->
                        mods[id]?.dependencies?.any {
                            it.substringBefore('@').trim() !in selected
                        } == true
                    }
            } while (changed)
        }
        write(enabledFile, JSONArray(selected.sorted()).toString(2))
    }

    fun delete(id: String) {
        require(id.matches(modInternalName)) { "无效模组名称" }
        if (id in metadata()) setEnabled(setOf(id), false)
        // Metadata name, rather than a user-supplied path, selects the file.
        root
            .listFiles()
            ?.filter { it.extension == "tmod" }
            ?.forEach { file ->
                val result = runCatching { TmodReader.read(file).name }
                if (
                    result.getOrNull() == id ||
                        (result.isFailure && file.nameWithoutExtension == id)
                )
                    check(file.delete()) { "无法删除模组" }
            }
    }

    fun packs(): JSONArray = if (packsFile.isFile) JSONArray(packsFile.readText()) else JSONArray()

    fun importPack(file: File, defaultName: String) {
        val bytes = file.readBytes()
        val charset = when {
            bytes.size >= 2 && bytes[0] == 0xff.toByte() && bytes[1] == 0xfe.toByte() -> Charsets.UTF_16LE
            bytes.size >= 2 && bytes[0] == 0xfe.toByte() && bytes[1] == 0xff.toByte() -> Charsets.UTF_16BE
            else -> Charsets.UTF_8
        }
        val reader = JSONTokener(String(bytes, charset).removePrefix("\uFEFF"))
        val value = reader.nextValue()
        require(reader.nextClean() == '\u0000') { "模组包 JSON 末尾含有多余内容" }
        val (name, list) = when (value) {
            is JSONArray -> defaultName to value
            is JSONObject -> {
                val name = value.opt("name") as? String ?: error("模组包缺少名称")
                name to (value.optJSONArray("mods") ?: error("模组包缺少 mods 清单"))
            }
            else -> error("请选择包含模组清单的 JSON 文件")
        }
        val ids = (0 until list.length()).map { index ->
            list.get(index) as? String ?: error("模组清单第 ${index + 1} 项应为模组内部名称")
        }
        savePack(name, ids)
    }

    fun savePack(name: String, ids: List<String> = enabled().sorted()): JSONObject {
        require(name.trim().isNotEmpty() && name.length <= 80 && ids.size <= 4096) { "模组包名称或清单无效" }
        require(ids.all(modInternalName::matches)) { "无效模组名称" }
        val array = packs()
        val pack = JSONObject().put("name", name.trim()).put("mods", JSONArray(ids.distinct()))
        val result = JSONArray()
        for (i in 0 until array.length()) if (
            array.getJSONObject(i).getString("name") != name.trim()
        )
            result.put(array.get(i))
        require(result.length() < 200) { "模组包数量已达上限" }
        result.put(pack)
        write(packsFile, result.toString(2))
        return pack
    }

    fun applyPack(name: String) {
        val pack =
            packs().let { array ->
                (0 until array.length())
                    .map { array.getJSONObject(it) }
                    .firstOrNull { it.getString("name") == name }
            } ?: error("找不到模组包")
        val ids = pack.getJSONArray("mods").strings().filter { it != LegacyTouchMod.ID }.toSet()
        // Resolve the complete selection before its single atomic commit.
        setEnabled(ids, true, replace = true)
    }

    fun deletePack(name: String) {
        val array = packs()
        val remaining = JSONArray()
        for (index in 0 until array.length()) {
            val pack = array.getJSONObject(index)
            if (pack.getString("name") != name) remaining.put(pack)
        }
        write(packsFile, remaining.toString(2))
    }

    private fun metadata() =
        root
            .listFiles()
            ?.filter { it.isFile && it.extension == "tmod" }
            ?.mapNotNull { runCatching { TmodReader.read(it) }.getOrNull() }
            ?.associateBy { it.name } ?: emptyMap()

    fun validateForLaunch(version: String) {
        val mods = metadata()
        val active = enabled()
        require(active.all { it in mods }) { "启用清单包含缺失或损坏的模组" }
        active.forEach { id ->
            require(TmlModCompatibility.accepts(mods.getValue(id).loaderVersion, version)) {
                "模组 $id 的 tML 版本为 ${mods.getValue(id).loaderVersion}，当前发行版为 $version"
            }
        }
        setEnabled(active, true)
    }

    private fun enabled(): Set<String> =
        if (enabledFile.isFile) JSONArray(enabledFile.readText()).strings().toSet() else emptySet()

    companion object {
        private fun dependencyReference(reference: String): Pair<String, String> =
            reference.substringBefore('@').trim() to reference.substringAfter('@', "").trim()

        fun JSONArray.strings(): List<String> = (0 until length()).map { getString(it) }

        fun write(file: File, contents: String) {
            check(file.parentFile?.isDirectory == true || file.parentFile?.mkdirs() == true)
            val temp = File.createTempFile(".launcher-", ".json", file.parentFile)
            try {
                temp.outputStream().use { stream ->
                    stream.write(contents.toByteArray(Charsets.UTF_8))
                    stream.fd.sync()
                }
                Files.move(
                    temp.toPath(),
                    file.toPath(),
                    StandardCopyOption.ATOMIC_MOVE,
                    StandardCopyOption.REPLACE_EXISTING,
                )
            } finally {
                temp.delete()
            }
        }

        private fun versionAtLeast(actual: String, required: String): Boolean {
            val left = actual.split('.').map { it.toIntOrNull() ?: 0 }
            val right = required.split('.').map { it.toIntOrNull() ?: 0 }
            for (i in 0 until maxOf(left.size, right.size)) {
                val diff = left.getOrElse(i) { 0 }.compareTo(right.getOrElse(i) { 0 })
                if (diff != 0) return diff > 0
            }
            return true
        }
    }
}
