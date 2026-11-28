package app.com.tmlpocket.feature.launcher

import java.io.File
import java.io.InputStream
import java.nio.file.Files
import java.nio.file.StandardCopyOption.ATOMIC_MOVE
import java.util.UUID
import java.util.zip.ZipFile
import org.json.JSONArray
import org.json.JSONObject

/** Launcher-wide snapshots can be applied to any game instance. */
internal class ControlLayoutPacks(private val directory: File) {
    @Synchronized fun list(): JSONObject {
        val packs = JSONArray()
        val files = directory.listFiles { file -> file.isFile && file.extension == "zip" }.orEmpty()
        for (file in files.sortedByDescending { it.lastModified() }) {
            val name = ZipFile(file).use(ControlLayoutArchive::manifestName)
            packs.put(JSONObject().put("id", file.nameWithoutExtension).put("name", name)
                .put("createdAt", file.lastModified()).put("sizeBytes", file.length()))
        }
        return JSONObject().put("packs", packs)
    }

    fun save(name: String, profile: JSONObject) = install { temporary ->
        ControlLayoutArchive.write(temporary, ControlLayoutArchive.Pack(name, profile))
    }

    fun import(input: InputStream) = install { temporary ->
        temporary.outputStream().use { ControlLayoutArchive.copyBounded(input, it, ControlLayoutArchive.MAX_ARCHIVE_BYTES) }
        ControlLayoutArchive.read(temporary)
    }

    @Synchronized private fun install(write: (File) -> Unit): JSONObject {
        check(directory.isDirectory || directory.mkdirs())
        val temporary = File.createTempFile(".layout-", ".tmp", directory)
        try {
            write(temporary)
            Files.move(temporary.toPath(), File(directory, "${UUID.randomUUID()}.zip").toPath(), ATOMIC_MOVE)
        } finally {
            temporary.delete()
        }
        return list()
    }

    fun archive(id: String): File {
        require(UUID.fromString(id).toString() == id) { "布局包编号无效" }
        return File(directory, "$id.zip").also { require(it.isFile) { "布局包不存在" } }
    }

    @Synchronized fun delete(id: String): JSONObject {
        Files.delete(archive(id).toPath())
        return list()
    }
}
